using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 6.5 Orders outbox store claiming semantics on the real SQL Server engine. Claiming is a single
/// atomic conditional UPDATE guarded by Status = Pending, so concurrent workers can never claim the same
/// message, already-claimed/processed messages are never re-claimed, and stale InProgress claims are
/// recovered to Pending. The concurrent-claim test runs parallel workers over separate DI scopes (each
/// its own DbContext/connection) exactly like independent dispatcher instances.
/// </summary>
[Collection(SqlServerOrdersTestCollection.Name)]
public class OrdersOutboxStoreIntegrationTests
{
    private readonly SqlServerOrdersTestFixture _fixture;

    public OrdersOutboxStoreIntegrationTests(SqlServerOrdersTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress()
    {
        // F-TEST-05 isolation: use unique Type per test run so concurrent tests sharing the same
        // FluxCommerce_Orders DB (different collections) cannot pollute the pending set. The outbox
        // dispatcher claims by status/RetryAfterUtc only — without a per-test discriminator any
        // pending row from another running test would be claimed and break the exact-set assertion.
        var type = $"orders.test_event.{Guid.NewGuid():N}";
        var ids = await SeedAsync(3, type: type);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var claimed = await store.ClaimPendingAsync(batchSize: 10);
            var relevant = claimed.Where(m => m.Type == type).ToList();

            Assert.Equal(
                ids.OrderBy(id => id),
                relevant.Select(m => m.Id).OrderBy(id => id));
            Assert.Equal(ids.Count, relevant.Count);
            Assert.All(relevant, message => Assert.Equal(OutboxStatus.InProgress, message.Status));
            Assert.All(relevant, message => Assert.NotNull(message.ProcessedAtUtc));

            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => ids.Contains(message.Id))
                .ToListAsync();

            Assert.All(loaded, message => Assert.Equal(OutboxStatus.InProgress, message.Status));
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimPendingAsync_RespectsBatchSize()
    {
        var type = $"orders.test_event.{Guid.NewGuid():N}";
        var ids = await SeedAsync(5, type: type);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var claimed = await store.ClaimPendingAsync(batchSize: 3);
            var relevant = claimed.Where(m => m.Type == type).ToList();

            Assert.Equal(3, relevant.Count);
            Assert.All(relevant, message => Assert.Equal(OutboxStatus.InProgress, message.Status));

            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var remaining = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => ids.Contains(message.Id))
                .CountAsync(message => message.Status == OutboxStatus.Pending);

            Assert.Equal(2, remaining);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimPendingAsync_ClaimsNothing_WhenNothingIsPending()
    {
        var type = $"orders.test_event.{Guid.NewGuid():N}";
        var ids = await SeedAsync(3, OutboxStatus.Published, type: type);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var claimed = await store.ClaimPendingAsync(batchSize: 10);
            var relevant = claimed.Where(m => m.Type == type).ToList();

            Assert.Empty(relevant);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Theory]
    [InlineData(OutboxStatus.InProgress)]
    [InlineData(OutboxStatus.Published)]
    [InlineData(OutboxStatus.Failed)]
    public async Task ClaimPendingAsync_DoesNotClaimNonPendingMessages(OutboxStatus status)
    {
        var type = $"orders.test_event.{Guid.NewGuid():N}";
        var ids = await SeedAsync(3, status, type: type);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var claimed = await store.ClaimPendingAsync(batchSize: 10);
            var relevant = claimed.Where(m => m.Type == type).ToList();

            Assert.Empty(relevant);

            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => ids.Contains(message.Id))
                .ToListAsync();

            Assert.All(loaded, message => Assert.Equal(status, message.Status));
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimPendingAsync_DoesNotReclaimAlreadyClaimedMessages()
    {
        var type = $"orders.test_event.{Guid.NewGuid():N}";
        var ids = await SeedAsync(3, type: type);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var first = await store.ClaimPendingAsync(batchSize: 10);
            var firstRelevant = first.Where(m => m.Type == type).ToList();
            Assert.Equal(3, firstRelevant.Count);

            var second = await store.ClaimPendingAsync(batchSize: 10);
            var secondRelevant = second.Where(m => m.Type == type).ToList();

            Assert.Empty(secondRelevant);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ConcurrentClaims_FromSeparateWorkers_AreDisjoint_AndClaimedExactlyOnce()
    {
        var type = $"orders.test_event.{Guid.NewGuid():N}";
        var ids = await SeedAsync(60, type: type);
        const int workerCount = 4;
        const int batchSize = 20;

        try
        {
            // Each worker gets its own DI scope → its own DbContext and connection, like independent
            // dispatcher instances racing for the same pending queue. Each worker loops claiming batches
            // until none remain — the exact loop the Dispatcher will run — so whichever worker wins each
            // batch, the collective still claims every message exactly once.
            var tasks = Enumerable.Range(0, workerCount)
                .Select(_ => Task.Run(async () =>
                {
                    using var scope = _fixture.Services.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
                    var claimed = new List<OutboxMessage>();

                    while (true)
                    {
                        var batch = await store.ClaimPendingAsync(batchSize);
                        if (batch.Count == 0)
                            break;
                        claimed.AddRange(batch);
                    }

                    return claimed;
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var claimedIds = results.SelectMany(messages => messages.Select(message => message.Id)).Where(id => ids.Contains(id)).ToList();

            Assert.Equal(ids.Count, claimedIds.Count);
            Assert.Equal(ids.Count, claimedIds.Distinct().Count());
            Assert.Equal(ids.OrderBy(id => id), claimedIds.OrderBy(id => id));

            using var verifyScope = _fixture.Services.CreateScope();
            var context = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => ids.Contains(message.Id))
                .ToListAsync();

            Assert.Equal(ids.Count, loaded.Count);
            Assert.All(loaded, message => Assert.Equal(OutboxStatus.InProgress, message.Status));
            Assert.Equal(ids.Count, loaded.Count(message => message.ProcessedAtUtc is not null));
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimThenMarkPublished_TransitionsToPublished()
    {
        var ids = await SeedAsync(1);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var message = Assert.Single(await store.ClaimPendingAsync(batchSize: 1));

            var published = await store.MarkPublishedAsync(message.Id);

            Assert.True(published);
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
            Assert.Equal(OutboxStatus.Published, loaded.Status);
            Assert.NotNull(loaded.ProcessedAtUtc);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task MarkPublishedAsync_OnUnclaimedMessage_ReturnsFalse()
    {
        var ids = await SeedAsync(1);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var published = await store.MarkPublishedAsync(ids.Single());

            Assert.False(published);
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == ids.Single());
            Assert.Equal(OutboxStatus.Pending, loaded.Status);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimThenMarkRetryableFailure_ReturnsToPending_AndCanBeReclaimed()
    {
        var ids = await SeedAsync(1);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var message = Assert.Single(await store.ClaimPendingAsync(batchSize: 1));

            var failed = await store.MarkRetryableFailureAsync(message.Id, "broker unavailable");

            Assert.True(failed);
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
            Assert.Equal(OutboxStatus.Pending, loaded.Status);
            Assert.Equal(1, loaded.RetryCount);
            Assert.Equal("broker unavailable", loaded.Error);
            Assert.Null(loaded.ProcessedAtUtc);

            var reclaimed = await store.ClaimPendingAsync(batchSize: 1);
            Assert.Equal(message.Id, Assert.Single(reclaimed).Id);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimThenMarkRetryableFailure_WithBackoff_SchedulesRetryAfterUtc()
    {
        var ids = await SeedAsync(1);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var message = Assert.Single(await store.ClaimPendingAsync(batchSize: 1));
            var retryAfterUtc = DateTime.UtcNow.AddSeconds(30);

            var failed = await store.MarkRetryableFailureAsync(message.Id, "broker unavailable", retryAfterUtc);

            Assert.True(failed);
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
            Assert.Equal(OutboxStatus.Pending, loaded.Status);
            Assert.Equal(retryAfterUtc, loaded.RetryAfterUtc);

            // While the backoff window is open the message is Pending but not claimable.
            Assert.Empty(await store.ClaimPendingAsync(batchSize: 1));
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimPendingAsync_DoesNotClaimMessage_InBackoffWindow()
    {
        var ids = await SeedAsync(1, retryAfterUtc: DateTime.UtcNow.AddMinutes(5));

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var claimed = await store.ClaimPendingAsync(batchSize: 10);

            Assert.Empty(claimed);

            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == ids.Single());
            Assert.Equal(OutboxStatus.Pending, loaded.Status);
            Assert.NotNull(loaded.RetryAfterUtc);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ClaimPendingAsync_ClaimsMessage_OnceBackoffWindowHasElapsed()
    {
        var ids = await SeedAsync(1, retryAfterUtc: DateTime.UtcNow.AddMinutes(-1));

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            var claimed = await store.ClaimPendingAsync(batchSize: 10);

            var message = Assert.Single(claimed);
            Assert.Equal(ids.Single(), message.Id);
            Assert.Equal(OutboxStatus.InProgress, message.Status);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task RecoverExpiredClaimsAsync_RecoversStaleInProgressClaims_ToPending()
    {
        var ids = await SeedAsync(1, OutboxStatus.InProgress, claimedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var recovered = await store.RecoverExpiredClaimsAsync(TimeSpan.FromMinutes(5));

            Assert.Equal(1, recovered);
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == ids.Single());
            Assert.Equal(OutboxStatus.Pending, loaded.Status);
            Assert.Null(loaded.ProcessedAtUtc);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task RecoverExpiredClaimsAsync_DoesNotRecoverFreshClaims()
    {
        var ids = await SeedAsync(1, OutboxStatus.InProgress, claimedAtUtc: DateTime.UtcNow);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var recovered = await store.RecoverExpiredClaimsAsync(TimeSpan.FromMinutes(5));

            Assert.Equal(0, recovered);
            var loaded = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == ids.Single());
            Assert.Equal(OutboxStatus.InProgress, loaded.Status);
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    private async Task<List<Guid>> SeedAsync(
        int count,
        OutboxStatus status = OutboxStatus.Pending,
        DateTime? claimedAtUtc = null,
        DateTime? retryAfterUtc = null,
        string? type = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // F-TEST-05: remove stale test-event rows left by a previous run/crash so the pending set is
        // exactly the seeded rows. This handles the observed 380/381 flake where the first claim saw
        // 5 rows (3 seeded + 2 leftover). Only the synthetic test-event type is purged — production
        // types (orders.order_created etc.) are never deleted.
        if (type is null)
        {
            await context.OutboxMessages
                .Where(m => m.Type == "orders.test_event" || m.Type.StartsWith("orders.test_event."))
                .ExecuteDeleteAsync();
        }

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var effectiveType = type ?? "orders.test_event";
        var messages = Enumerable.Range(0, count)
            .Select(index => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                Type = effectiveType,
                Payload = $"{{\"orderId\":\"00000000-0000-0000-0000-{index:000000000000}\"}}",
                CorrelationId = Guid.NewGuid(),
                CausationId = null,
                OccurredAtUtc = baseTime.AddMinutes(index),
                CreatedAtUtc = baseTime.AddMinutes(index),
                ProcessedAtUtc = status == OutboxStatus.Pending ? null : (claimedAtUtc ?? DateTime.UtcNow),
                Status = status,
                RetryCount = status == OutboxStatus.Pending ? 0 : 1,
                RetryAfterUtc = retryAfterUtc,
                Error = null
            })
            .ToList();

        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync();

        return messages.Select(message => message.Id).ToList();
    }

    private async Task CleanupAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return;

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages
            .Where(message => idList.Contains(message.Id))
            .ExecuteDeleteAsync();
    }
}