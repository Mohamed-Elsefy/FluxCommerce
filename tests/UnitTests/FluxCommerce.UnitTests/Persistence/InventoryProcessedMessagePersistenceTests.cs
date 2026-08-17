using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 7.3: the ProcessedMessage persistence model owned by the Inventory module DbContext. Verifies
/// the composite <c>(Key, Handler)</c> identity (EventId + HandlerName, per ADR-008), the lightweight
/// column set, the absence of foreign keys/navigations, and that <c>(E1, H1) != (E1, H2)</c> can coexist
/// while a duplicate <c>(E1, H1)</c> is rejected by the unique constraint.
/// </summary>
public class InventoryProcessedMessagePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryProcessedMessagePersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task ProcessedMessage_RoundTrips_WithIdentityAndProcessedAtUtc()
    {
        await using var context = CreateContext();

        var message = CreateMessage();
        context.ProcessedMessages.Add(message);
        await context.SaveChangesAsync();

        var loaded = await context.ProcessedMessages.SingleAsync(m => m.Key == message.Key);

        Assert.Equal(message.Key, loaded.Key);
        Assert.Equal(message.Handler, loaded.Handler);
        Assert.Equal(message.ProcessedAtUtc, loaded.ProcessedAtUtc);
    }

    [Fact]
    public async Task ProcessedMessage_SameEvent_DifferentHandlers_CanCoexist()
    {
        await using var context = CreateContext();

        var eventId = Guid.NewGuid().ToString();
        context.ProcessedMessages.Add(CreateMessage(eventId, "Inventory.ReserveStock"));
        context.ProcessedMessages.Add(CreateMessage(eventId, "Inventory.NotifySummary"));

        await context.SaveChangesAsync();

        var stored = await context.ProcessedMessages
            .Where(m => m.Key == eventId)
            .ToListAsync();

        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task ProcessedMessage_SameEvent_SameHandler_IsRejected()
    {
        var message = CreateMessage();

        await using (var first = CreateContext())
        {
            first.ProcessedMessages.Add(message);
            await first.SaveChangesAsync();
        }

        await using var context = CreateContext();
        context.ProcessedMessages.Add(ProcessedMessage.Create(message.Key, message.Handler));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void ProcessedMessage_IsMappedToTable_ProcessedMessages()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(ProcessedMessage));

        Assert.NotNull(entityType);
        Assert.Equal("ProcessedMessages", entityType!.GetTableName());
    }

    [Fact]
    public void ProcessedMessage_HasCompositePrimaryKey_OnKeyAndHandler()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(ProcessedMessage));

        Assert.NotNull(entityType);
        Assert.Equal(
            new[] { nameof(ProcessedMessage.Key), nameof(ProcessedMessage.Handler) },
            entityType!.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void ProcessedMessage_HasNoForeignKeys_AndNoNavigations()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(ProcessedMessage));

        Assert.NotNull(entityType);
        Assert.Empty(entityType!.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    [Fact]
    public void ProcessedMessage_SqlServerColumnTypes_AreConfigured()
    {
        using var context = CreateSqlServerContext();

        var entityType = GetDesignTimeEntityType(context);

        Assert.Equal("nvarchar(36)", GetColumnType(entityType, nameof(ProcessedMessage.Key)));
        Assert.Equal("nvarchar(128)", GetColumnType(entityType, nameof(ProcessedMessage.Handler)));
        Assert.Equal("datetime2", GetColumnType(entityType, nameof(ProcessedMessage.ProcessedAtUtc)));
    }

    [Fact]
    public void ProcessedMessage_IdentityFields_AreRequired()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(ProcessedMessage));

        Assert.NotNull(entityType);
        Assert.False(entityType!.GetProperty(nameof(ProcessedMessage.Key)).IsNullable);
        Assert.False(entityType.GetProperty(nameof(ProcessedMessage.Handler)).IsNullable);
        Assert.False(entityType.GetProperty(nameof(ProcessedMessage.ProcessedAtUtc)).IsNullable);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static ProcessedMessage CreateMessage(string? key = null, string? handler = null) =>
        ProcessedMessage.Create(
            key ?? Guid.NewGuid().ToString(),
            handler ?? "Inventory.ReserveStock");

    private static IEntityType GetDesignTimeEntityType(InventoryDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ProcessedMessage))
            ?? throw new InvalidOperationException($"Entity type '{nameof(ProcessedMessage)}' was not found in the model.");

    private static string? GetColumnType(IEntityType entityType, string propertyName) =>
        entityType.GetProperty(propertyName)?.GetColumnType();

    private InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new InventoryDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private static InventoryDbContext CreateSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=localhost;Database=Fake;User Id=sa;Password=Fake;TrustServerCertificate=True")
            .Options;

        return new InventoryDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}