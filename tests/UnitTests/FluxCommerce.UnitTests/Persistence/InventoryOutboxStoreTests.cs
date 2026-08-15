using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Outbox;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 6.5 Inventory outbox store behavior. Claiming transitions Pending → InProgress and is guarded
/// by the current status (the concurrency token): a message that is already InProgress, Published or
/// Failed can never be claimed again, and completion/failure transitions only apply to the claiming
/// worker's InProgress message. Stale InProgress claims older than the claim timeout are recovered to
/// Pending. The atomic batch claim uses SQL Server–specific SQL and is covered by the real-SQL-Server
/// integration tests; only its batch-size guard is exercised here.
/// </summary>
public class InventoryOutboxStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryOutboxStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task AddAsync_PersistsTheMessage()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage();

        await store.AddAsync(message);

        var loaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Equal(message.Type, loaded.Type);
        Assert.Equal(OutboxStatus.Pending, loaded.Status);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyPending_OrderedByCreatedAtUtc()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        var older = CreateMessage() with { CreatedAtUtc = new DateTime(2026, 8, 10, 8, 0, 0) };
        var middle = CreateMessage() with { CreatedAtUtc = new DateTime(2026, 8, 10, 8, 15, 0) };
        var newer = CreateMessage() with { CreatedAtUtc = new DateTime(2026, 8, 10, 8, 30, 0) };
        context.OutboxMessages.AddRange(
            newer,
            CreateMessage() with { Status = OutboxStatus.Published },
            older,
            CreateMessage() with { Status = OutboxStatus.Failed },
            middle);
        await context.SaveChangesAsync();

        var pending = await store.GetPendingAsync(batchSize: 10);

        Assert.Equal([older.Id, middle.Id, newer.Id], pending.Select(m => m.Id).ToList());
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsUpToBatchSize()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        context.OutboxMessages.AddRange(
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
            CreateMessage());
        await context.SaveChangesAsync();

        var pending = await store.GetPendingAsync(batchSize: 2);

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task GetPendingAsync_BatchSizeLessThanOne_Throws()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.GetPendingAsync(0));
    }

    [Fact]
    public async Task ClaimPendingAsync_BatchSizeLessThanOne_Throws()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ClaimPendingAsync(0));
    }

    [Fact]
    public async Task MarkInProgressAsync_ClaimsPendingMessage_AndReturnsTrue()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var claimed = await store.MarkInProgressAsync(message.Id);

        Assert.True(claimed);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxStatus.InProgress, loaded.Status);
        Assert.NotNull(loaded.ProcessedAtUtc);
    }

    [Theory]
    [InlineData(OutboxStatus.InProgress)]
    [InlineData(OutboxStatus.Published)]
    [InlineData(OutboxStatus.Failed)]
    public async Task MarkInProgressAsync_DoesNotReclaimNonPending_ReturnsFalse(OutboxStatus status)
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(status);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var claimed = await store.MarkInProgressAsync(message.Id);

        Assert.False(claimed);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(status, loaded.Status);
    }

    [Fact]
    public async Task MarkPublishedAsync_TransitionsInProgress_ToPublished()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(OutboxStatus.InProgress);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var published = await store.MarkPublishedAsync(message.Id);

        Assert.True(published);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxStatus.Published, loaded.Status);
        Assert.NotNull(loaded.ProcessedAtUtc);
    }

    [Theory]
    [InlineData(OutboxStatus.Pending)]
    [InlineData(OutboxStatus.Published)]
    public async Task MarkPublishedAsync_DoesNotPublishUnclaimedOrProcessed_ReturnsFalse(OutboxStatus status)
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(status);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var published = await store.MarkPublishedAsync(message.Id);

        Assert.False(published);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(status, loaded.Status);
    }

    [Fact]
    public async Task MarkRetryableFailureAsync_ReturnsToPending_IncrementsRetryCount_AndStoresError()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(OutboxStatus.InProgress) with
        {
            ProcessedAtUtc = DateTime.UtcNow,
            RetryCount = 1
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var failed = await store.MarkRetryableFailureAsync(message.Id, "broker unavailable");

        Assert.True(failed);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxStatus.Pending, loaded.Status);
        Assert.Equal(2, loaded.RetryCount);
        Assert.Equal("broker unavailable", loaded.Error);
        Assert.Null(loaded.ProcessedAtUtc);
    }

    [Fact]
    public async Task MarkRetryableFailureAsync_DoesNotFailNonInProgress_ReturnsFalse()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(OutboxStatus.Pending);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var failed = await store.MarkRetryableFailureAsync(message.Id, "broker unavailable");

        Assert.False(failed);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxStatus.Pending, loaded.Status);
        Assert.Equal(0, loaded.RetryCount);
        Assert.Null(loaded.Error);
    }

    [Fact]
    public async Task MarkFailedAsync_TransitionsInProgress_ToFailed_WithError()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(OutboxStatus.InProgress);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var failed = await store.MarkFailedAsync(message.Id, "non-retryable");

        Assert.True(failed);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxStatus.Failed, loaded.Status);
        Assert.Equal("non-retryable", loaded.Error);
        Assert.NotNull(loaded.ProcessedAtUtc);
    }

    [Fact]
    public async Task MarkFailedAsync_DoesNotFailPending_ReturnsFalse()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);
        var message = CreateMessage(OutboxStatus.Pending);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var failed = await store.MarkFailedAsync(message.Id, "non-retryable");

        Assert.False(failed);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxStatus.Pending, loaded.Status);
    }

    [Fact]
    public async Task RecoverExpiredClaimsAsync_ResetsStaleInProgressClaims_ToPending()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        var stale = CreateMessage(OutboxStatus.InProgress) with
        {
            ProcessedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        context.OutboxMessages.Add(stale);
        await context.SaveChangesAsync();

        var recovered = await store.RecoverExpiredClaimsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, recovered);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == stale.Id);
        Assert.Equal(OutboxStatus.Pending, loaded.Status);
        Assert.Null(loaded.ProcessedAtUtc);
    }

    [Fact]
    public async Task RecoverExpiredClaimsAsync_DoesNotTouchFreshClaims()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        var fresh = CreateMessage(OutboxStatus.InProgress) with
        {
            ProcessedAtUtc = DateTime.UtcNow
        };
        context.OutboxMessages.Add(fresh);
        await context.SaveChangesAsync();

        var recovered = await store.RecoverExpiredClaimsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, recovered);
        var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == fresh.Id);
        Assert.Equal(OutboxStatus.InProgress, loaded.Status);
    }

    [Fact]
    public async Task RecoverExpiredClaimsAsync_NonPositiveTimeout_Throws()
    {
        await using var context = CreateContext();
        var store = new InventoryOutboxStore(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.RecoverExpiredClaimsAsync(TimeSpan.Zero));
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
            Type = "inventory.test_event",
            Payload = "{\"reservationId\":\"00000000-0000-0000-0000-000000000001\"}",
            CorrelationId = Guid.NewGuid(),
            CausationId = null,
            OccurredAtUtc = new DateTime(2026, 8, 10, 8, 15, 0),
            CreatedAtUtc = new DateTime(2026, 8, 10, 8, 15, 1),
            ProcessedAtUtc = status == OutboxStatus.Pending ? null : DateTime.UtcNow,
            Status = status,
            RetryCount = status == OutboxStatus.Pending ? 0 : 2,
            Error = null
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