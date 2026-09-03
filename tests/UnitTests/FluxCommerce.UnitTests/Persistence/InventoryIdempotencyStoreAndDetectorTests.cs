using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 7.3: <see cref="IIdempotencyStore"/> and <see cref="IDuplicateDetector"/> behavior for the
/// Inventory module, exercised against a real (Sqlite) engine so the unique composite constraint is the
/// arbiter of duplicate races. The SQL Server concurrency proof lives in the integration tests.
/// </summary>
public class InventoryIdempotencyStoreAndDetectorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryIdempotencyStoreAndDetectorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    // ---- IIdempotencyStore ----

    [Fact]
    public async Task AddAsync_PersistsTheMessage()
    {
        await using var context = CreateContext();
        var store = new InventoryIdempotencyStore(context);
        var message = CreateMessage();

        await store.AddAsync(message, CancellationToken.None);

        var loaded = await context.ProcessedMessages.SingleAsync(m => m.Key == message.Key);
        Assert.Equal(message.Handler, loaded.Handler);
        Assert.Equal(message.ProcessedAtUtc, loaded.ProcessedAtUtc);
    }

    [Fact]
    public async Task GetAsync_ReturnsByKeyAndHandler()
    {
        await using var context = CreateContext();
        var store = new InventoryIdempotencyStore(context);
        var message = CreateMessage();
        await store.AddAsync(message, CancellationToken.None);

        var loaded = await store.GetAsync(message.Key, message.Handler, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(message.ProcessedAtUtc, loaded!.ProcessedAtUtc);
    }

    [Fact]
    public async Task GetAsync_WithoutHandler_ReturnsAnyHandlerForTheKey()
    {
        await using var context = CreateContext();
        var store = new InventoryIdempotencyStore(context);
        var message = CreateMessage();
        await store.AddAsync(message, CancellationToken.None);

        var loaded = await store.GetAsync(message.Key, handler: null, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(message.Key, loaded!.Key);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        await using var context = CreateContext();
        var store = new InventoryIdempotencyStore(context);

        var loaded = await store.GetAsync("missing-key", "Inventory.ReserveStock", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task AddAsync_DuplicateKeyAndHandler_DoesNotThrow()
    {
        var message = CreateMessage();

        await using (var first = CreateContext())
        {
            await new InventoryIdempotencyStore(first).AddAsync(message, CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryIdempotencyStore(context);
        var duplicate = ProcessedMessage.Create(message.Key, message.Handler);

        await store.AddAsync(duplicate, CancellationToken.None);
    }

    // ---- IDuplicateDetector ----

    [Fact]
    public async Task IsProcessedAsync_ReturnsTrue_ForExistingKeyAndHandler()
    {
        await using var context = CreateContext();
        var detector = new InventoryDuplicateDetector(context);
        var message = CreateMessage();
        await context.ProcessedMessages.AddAsync(message);
        await context.SaveChangesAsync();

        Assert.True(await detector.IsProcessedAsync(message.Key, message.Handler, CancellationToken.None));
    }

    [Fact]
    public async Task IsProcessedAsync_ReturnsFalse_WhenNotProcessed()
    {
        await using var context = CreateContext();
        var detector = new InventoryDuplicateDetector(context);

        Assert.False(await detector.IsProcessedAsync("missing-key", "Inventory.ReserveStock", CancellationToken.None));
    }

    [Fact]
    public async Task IsProcessedAsync_WithoutHandler_ReturnsTrue_IfAnyHandlerProcessed()
    {
        await using var context = CreateContext();
        var detector = new InventoryDuplicateDetector(context);
        var message = CreateMessage();
        await context.ProcessedMessages.AddAsync(message);
        await context.SaveChangesAsync();

        Assert.True(await detector.IsProcessedAsync(message.Key, handler: null, CancellationToken.None));
    }

    [Fact]
    public async Task TryClaimAsync_FirstClaim_ReturnsNotProcessed()
    {
        await using var context = CreateContext();
        var detector = new InventoryDuplicateDetector(context);

        var result = await detector.TryClaimAsync(CreateMessage(), CancellationToken.None);

        Assert.False(result.IsDuplicate);
        Assert.Null(result.ProcessedAtUtc);
    }

    [Fact]
    public async Task TryClaimAsync_DuplicateClaim_ReturnsAlreadyProcessed_WithWinnerTimestamp()
    {
        var message = CreateMessage(processedAtUtc: new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc));

        await using (var first = CreateContext())
        {
            await first.ProcessedMessages.AddAsync(message);
            await first.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var detector = new InventoryDuplicateDetector(context);

        var result = await detector.TryClaimAsync(
            ProcessedMessage.Create(message.Key, message.Handler),
            CancellationToken.None);

        Assert.True(result.IsDuplicate);
        Assert.Equal(message.ProcessedAtUtc, result.ProcessedAtUtc);
    }

    [Fact]
    public async Task TryClaimAsync_SameEvent_DifferentHandler_ReturnsNotProcessed()
    {
        await using var context = CreateContext();
        var detector = new InventoryDuplicateDetector(context);
        var message = CreateMessage();
        await context.ProcessedMessages.AddAsync(message);
        await context.SaveChangesAsync();

        var result = await detector.TryClaimAsync(
            ProcessedMessage.Create(message.Key, "Inventory.SecondHandler"),
            CancellationToken.None);

        Assert.False(result.IsDuplicate);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static ProcessedMessage CreateMessage(DateTime? processedAtUtc = null) =>
        ProcessedMessage.Create(
            Guid.NewGuid().ToString(),
            "Inventory.ReserveStock") with
        {
            ProcessedAtUtc = processedAtUtc ?? new DateTime(2026, 8, 16, 8, 30, 0, DateTimeKind.Utc)
        };

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

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}