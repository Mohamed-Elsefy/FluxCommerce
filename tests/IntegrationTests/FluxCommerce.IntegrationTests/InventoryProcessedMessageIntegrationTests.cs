using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 7.3: ProcessedMessage persistence for the Inventory module against the real SQL Server engine.
/// Verifies the canonical <see cref="ProcessedMessage"/> record persists with the composite
/// <c>(Key, Handler)</c> identity (EventId + HandlerName), that distinct handlers process the same event
/// independently, that the unique composite constraint rejects a duplicate <c>(Key, Handler)</c>, that
/// <c>ProcessedAtUtc</c> survives the round trip, and — the central proof — that concurrent claims for
/// the same <c>(Key, Handler)</c> yield exactly one winner and the rest are recognized as duplicates.
/// The database constraint, not an application existence check, is the final arbiter.
/// </summary>
[Collection(SqlServerInventoryTestCollection.Name)]
public class InventoryProcessedMessageIntegrationTests
{
    private readonly SqlServerInventoryTestFixture _fixture;

    public InventoryProcessedMessageIntegrationTests(SqlServerInventoryTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_ThenGetAsync_FirstKeyAndHandler_IsStoredAndRetrieved()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var message = CreateMessage();

        try
        {
            await store.AddAsync(message, CancellationToken.None);

            var loaded = await store.GetAsync(message.Key, message.Handler, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(message.Key, loaded!.Key);
            Assert.Equal(message.Handler, loaded.Handler);
        }
        finally
        {
            await CleanupAsync(message.Key);
        }
    }

    [Fact]
    public async Task ProcessedAtUtc_IsPersistedCorrectly()
    {
        var processedAtUtc = new DateTime(2026, 8, 16, 9, 30, 15, DateTimeKind.Utc);
        var message = ProcessedMessage.Create(Guid.NewGuid().ToString(), "Inventory.ReserveStock") with
        {
            ProcessedAtUtc = processedAtUtc
        };

        try
        {
            await AddAsyncInNewScope(message);

            using var readScope = _fixture.Services.CreateScope();
            var store = readScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
            var loaded = await store.GetAsync(message.Key, message.Handler, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(processedAtUtc, loaded!.ProcessedAtUtc);
        }
        finally
        {
            await CleanupAsync(message.Key);
        }
    }

    [Fact]
    public async Task DifferentHandlers_CanProcess_SameEvent_Independently()
    {
        var eventId = Guid.NewGuid().ToString();
        const string firstHandler = "Inventory.ReserveStock";
        const string secondHandler = "Inventory.NotifySummary";

        try
        {
            var first = await ClaimInNewScope(eventId, firstHandler);
            var second = await ClaimInNewScope(eventId, secondHandler);

            Assert.False(first.IsDuplicate);
            Assert.False(second.IsDuplicate);

            using var readScope = _fixture.Services.CreateScope();
            var store = readScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
            var firstStored = await store.GetAsync(eventId, firstHandler, CancellationToken.None);
            var secondStored = await store.GetAsync(eventId, secondHandler, CancellationToken.None);

            Assert.NotNull(firstStored);
            Assert.NotNull(secondStored);
            Assert.NotEqual(firstStored!.Handler, secondStored!.Handler);
        }
        finally
        {
            await CleanupAsync(eventId);
        }
    }

    [Fact]
    public async Task SameHandler_CannotClaim_SameEvent_Twice()
    {
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";

        try
        {
            var first = await ClaimInNewScope(eventId, handler);
            var second = await ClaimInNewScope(eventId, handler);

            Assert.False(first.IsDuplicate);
            Assert.True(second.IsDuplicate);
            Assert.NotNull(second.ProcessedAtUtc);
        }
        finally
        {
            await CleanupAsync(eventId);
        }
    }

    [Fact]
    public async Task DuplicateAddAsync_DoesNotCreateSecondRecord()
    {
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";

        try
        {
            await AddAsyncInNewScope(ProcessedMessage.Create(eventId, handler));
            await AddAsyncInNewScope(ProcessedMessage.Create(eventId, handler));

            using var readScope = _fixture.Services.CreateScope();
            var records = await readScope.ServiceProvider.GetRequiredService<InventoryDbContext>()
                .ProcessedMessages
                .Where(m => m.Key == eventId && m.Handler == handler)
                .ToListAsync();

            Assert.Single(records);
        }
        finally
        {
            await CleanupAsync(eventId);
        }
    }

    [Fact]
    public async Task ConcurrentClaims_ForSameKeyAndHandler_ProduceExactlyOneWinner()
    {
        const int attempts = 10;
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";

        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => ClaimInNewScope(eventId, handler)));

            var winners = results.Where(result => !result.IsDuplicate).ToList();
            var duplicates = results.Where(result => result.IsDuplicate).ToList();

            Assert.Single(winners);
            Assert.Equal(attempts - 1, duplicates.Count);

            using var readScope = _fixture.Services.CreateScope();
            var store = readScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
            var persisted = await store.GetAsync(eventId, handler, CancellationToken.None);

            Assert.NotNull(persisted);
            Assert.All(duplicates, result => Assert.Equal(persisted!.ProcessedAtUtc, result.ProcessedAtUtc));
        }
        finally
        {
            await CleanupAsync(eventId);
        }
    }

    private async Task<IdempotencyResult> ClaimInNewScope(string eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var detector = scope.ServiceProvider.GetRequiredService<IDuplicateDetector>();
        return await detector.TryClaimAsync(
            ProcessedMessage.Create(eventId, handler),
            CancellationToken.None);
    }

    private async Task AddAsyncInNewScope(ProcessedMessage message)
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        await store.AddAsync(message, CancellationToken.None);
    }

    private static ProcessedMessage CreateMessage() =>
        ProcessedMessage.Create(Guid.NewGuid().ToString(), "Inventory.ReserveStock");

    private async Task CleanupAsync(string key)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await context.ProcessedMessages
            .Where(m => m.Key == key)
            .ExecuteDeleteAsync();
    }
}