using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

public class InventoryOutboxPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryOutboxPersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task OutboxMessage_RoundTrips_WithAllFields()
    {
        await using var context = CreateContext();

        var message = CreateMessage();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var loaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);

        Assert.Equal(message.Id, loaded.Id);
        Assert.Equal(message.EventId, loaded.EventId);
        Assert.Equal(message.Type, loaded.Type);
        Assert.Equal(message.Payload, loaded.Payload);
        Assert.Equal(message.CorrelationId, loaded.CorrelationId);
        Assert.Equal(message.CausationId, loaded.CausationId);
        Assert.Equal(message.OccurredAtUtc, loaded.OccurredAtUtc);
        Assert.Equal(message.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(message.ProcessedAtUtc, loaded.ProcessedAtUtc);
        Assert.Equal(OutboxStatus.Pending, loaded.Status);
        Assert.Equal(message.RetryCount, loaded.RetryCount);
        Assert.Equal(message.Error, loaded.Error);
    }

    [Fact]
    public async Task OutboxMessage_AllStatuses_RoundTrip_AndPersistAsInt()
    {
        await using var context = CreateContext();

        foreach (var status in Enum.GetValues<OutboxStatus>())
        {
            context.OutboxMessages.Add(CreateMessage(status: status));
        }

        await context.SaveChangesAsync();

        var storedValues = await context.Database
            .SqlQueryRaw<int>("SELECT Status AS Value FROM OutboxMessages")
            .ToListAsync();

        Assert.Equal(
            Enum.GetValues<OutboxStatus>().Cast<int>().OrderBy(value => value),
            storedValues.OrderBy(value => value));

        foreach (var status in Enum.GetValues<OutboxStatus>())
        {
            var loaded = await context.OutboxMessages.SingleAsync(m => m.Status == status);
            Assert.Equal(status, loaded.Status);
        }
    }

    [Fact]
    public async Task OutboxMessage_NullableFields_ArePersistedAsNull()
    {
        await using var context = CreateContext();

        var message = CreateMessage() with
        {
            CausationId = null,
            ProcessedAtUtc = null,
            Error = null
        };

        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var loaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);

        Assert.Null(loaded.CausationId);
        Assert.Null(loaded.ProcessedAtUtc);
        Assert.Null(loaded.Error);
    }

    [Fact]
    public async Task OutboxMessage_Payload_CanStoreLargeSerializedPayload()
    {
        await using var context = CreateContext();

        var payload = "{" + string.Join(",", Enumerable.Range(0, 5000)
            .Select(index => $"\"key{index}\":\"value-{index}\"")) + "}";

        context.OutboxMessages.Add(CreateMessage() with { Payload = payload });
        await context.SaveChangesAsync();

        var loaded = await context.OutboxMessages.SingleAsync(m => m.Payload == payload);

        Assert.Equal(payload, loaded.Payload);
    }

    [Fact]
    public void OutboxMessage_IsMappedToTable_OutboxMessages()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(OutboxMessage));

        Assert.NotNull(entityType);
        Assert.Equal("OutboxMessages", entityType!.GetTableName());
    }

    [Fact]
    public void OutboxMessage_HasNoForeignKeys_AndNoNavigations()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(OutboxMessage));

        Assert.NotNull(entityType);
        Assert.Empty(entityType!.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    [Fact]
    public void OutboxMessage_StatusIndexes_AreDefined()
    {
        using var context = CreateContext();

        var entityType = GetDesignTimeEntityType(context);
        var indexNames = entityType.GetIndexes().Select(index => index.GetDatabaseName()).ToList();

        Assert.Contains("IX_OutboxMessages_Status_CreatedAtUtc", indexNames);
        Assert.Contains("IX_OutboxMessages_Status_ProcessedAtUtc", indexNames);
    }

    [Fact]
    public void OutboxMessage_SqlServerColumnTypes_AreConfigured()
    {
        using var context = CreateSqlServerContext();

        var entityType = GetDesignTimeEntityType(context);

        Assert.Equal("uniqueidentifier", GetColumnType(entityType, nameof(OutboxMessage.Id)));
        Assert.Equal("uniqueidentifier", GetColumnType(entityType, nameof(OutboxMessage.EventId)));
        Assert.Equal("nvarchar(512)", GetColumnType(entityType, nameof(OutboxMessage.Type)));
        Assert.Equal("nvarchar(max)", GetColumnType(entityType, nameof(OutboxMessage.Payload)));
        Assert.Equal("uniqueidentifier", GetColumnType(entityType, nameof(OutboxMessage.CorrelationId)));
        Assert.Equal("uniqueidentifier", GetColumnType(entityType, nameof(OutboxMessage.CausationId)));
        Assert.Equal("datetime2", GetColumnType(entityType, nameof(OutboxMessage.OccurredAtUtc)));
        Assert.Equal("datetime2", GetColumnType(entityType, nameof(OutboxMessage.CreatedAtUtc)));
        Assert.Equal("datetime2", GetColumnType(entityType, nameof(OutboxMessage.ProcessedAtUtc)));
        Assert.Equal("int", GetColumnType(entityType, nameof(OutboxMessage.Status)));
        Assert.Equal("int", GetColumnType(entityType, nameof(OutboxMessage.RetryCount)));
        Assert.Equal("nvarchar(max)", GetColumnType(entityType, nameof(OutboxMessage.Error)));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static OutboxMessage CreateMessage(OutboxStatus status = OutboxStatus.Pending) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Type = "FluxCommerce.UnitTests.TestDomainEvent",
            Payload = "{\"sku\":\"SKU-1\",\"quantity\":3,\"status\":\"pending\"}",
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = new DateTime(2026, 2, 10, 8, 15, 0),
            CreatedAtUtc = new DateTime(2026, 2, 10, 8, 15, 1),
            ProcessedAtUtc = new DateTime(2026, 2, 10, 8, 15, 2),
            Status = status,
            RetryCount = 2,
            Error = null
        };

    private static IEntityType GetDesignTimeEntityType(InventoryDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException($"Entity type '{nameof(OutboxMessage)}' was not found in the model.");

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