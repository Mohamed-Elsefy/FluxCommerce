using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 7.4: the atomic idempotent-processing boundary against the real SQL Server engine. Proves the
/// five required invariants: (1) successful processing commits Business State + ProcessedMessage
/// together, (2) a business failure rolls both back, (3) a duplicate is acknowledged without re-executing
/// the business operation, (4) distinct handlers for the same event remain independent, and (5) — the
/// central concurrency proof — parallel attempts for the same <c>(Key, Handler)</c> yield exactly one
/// business effect and exactly one processed message, while a failed attempt leaves the message retryable.
/// </summary>
[Collection(SqlServerInventoryTestCollection.Name)]
public class InventoryIdempotentProcessorIntegrationTests
{
    private readonly SqlServerInventoryTestFixture _fixture;

    public InventoryIdempotentProcessorIntegrationTests(SqlServerInventoryTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SuccessfulProcessing_CommitsBusinessStateAndProcessedMessage()
    {
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";
        var sku = UniqueSku("IDEM-SUCCESS");

        try
        {
            var result = await ProcessInNewScope(eventId, handler, context => CreateItemAsync(context, sku, 10));

            Assert.False(result.IsDuplicate);
            Assert.Equal(1, await CountItemsAsync(sku));
            Assert.True(await IsProcessedAsync(eventId, handler));
        }
        finally
        {
            await CleanupAsync(eventId, sku);
        }
    }

    [Fact]
    public async Task BusinessFailure_RollsBackBusinessStateAndProcessedMessage()
    {
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";
        var sku = UniqueSku("IDEM-FAIL");

        try
        {
            async Task FailingOperation(InventoryDbContext context)
            {
                context.InventoryItems.Add(InventoryItem.Create(
                    new InventorySku(sku),
                    10,
                    DateTimeOffset.UtcNow));
                throw new InvalidOperationException("Business processing failed.");
            }

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ProcessInNewScope(eventId, handler, FailingOperation));

            Assert.Equal(0, await CountItemsAsync(sku));
            Assert.False(await IsProcessedAsync(eventId, handler));
        }
        finally
        {
            await CleanupAsync(eventId, sku);
        }
    }

    [Fact]
    public async Task FailedProcessing_LeavesMessageRetryable()
    {
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";
        var sku = UniqueSku("IDEM-RETRY");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ProcessInNewScope(
                    eventId,
                    handler,
                    _ => throw new InvalidOperationException("First attempt fails.")));

            Assert.Equal(0, await CountItemsAsync(sku));
            Assert.False(await IsProcessedAsync(eventId, handler));

            var retry = await ProcessInNewScope(eventId, handler, context => CreateItemAsync(context, sku, 10));

            Assert.False(retry.IsDuplicate);
            Assert.Equal(1, await CountItemsAsync(sku));
            Assert.True(await IsProcessedAsync(eventId, handler));
        }
        finally
        {
            await CleanupAsync(eventId, sku);
        }
    }

    [Fact]
    public async Task Duplicate_DoesNotRepeatBusinessEffect()
    {
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";
        var sku = UniqueSku("IDEM-DUP");
        var executionCount = 0;

        try
        {
            Func<InventoryDbContext, Task> BusinessOperation() => context =>
            {
                executionCount++;
                return CreateItemAsync(context, sku, 10);
            };

            var first = await ProcessInNewScope(eventId, handler, BusinessOperation());
            var second = await ProcessInNewScope(eventId, handler, BusinessOperation());

            Assert.False(first.IsDuplicate);
            Assert.True(second.IsDuplicate);
            Assert.NotNull(second.ProcessedAtUtc);
            Assert.Equal(1, executionCount);
            Assert.Equal(1, await CountItemsAsync(sku));
            Assert.Single(await GetProcessedAsync(eventId, handler));
        }
        finally
        {
            await CleanupAsync(eventId, sku);
        }
    }

    [Fact]
    public async Task DifferentHandlers_SameEvent_RemainIndependent()
    {
        var eventId = Guid.NewGuid().ToString();
        var firstSku = UniqueSku("IDEM-H1");
        var secondSku = UniqueSku("IDEM-H2");

        try
        {
            var first = await ProcessInNewScope(
                eventId,
                "Inventory.ReserveStock",
                context => CreateItemAsync(context, firstSku, 10));
            var second = await ProcessInNewScope(
                eventId,
                "Inventory.NotifySummary",
                context => CreateItemAsync(context, secondSku, 10));

            Assert.False(first.IsDuplicate);
            Assert.False(second.IsDuplicate);
            Assert.Equal(1, await CountItemsAsync(firstSku));
            Assert.Equal(1, await CountItemsAsync(secondSku));
            Assert.Equal(2, (await GetProcessedAsync(eventId)).Count);
        }
        finally
        {
            await CleanupAsync(eventId, firstSku);
            await CleanupAsync(null, secondSku);
        }
    }

    [Fact]
    public async Task ConcurrentDuplicate_ExactlyOneBusinessEffectAndOneProcessedMessage()
    {
        const int attempts = 10;
        var eventId = Guid.NewGuid().ToString();
        const string handler = "Inventory.ReserveStock";
        var sku = UniqueSku("IDEM-CONCURRENT");

        try
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, attempts).Select(_ =>
                    ProcessInNewScope(eventId, handler, context => CreateItemAsync(context, sku, 10))));

            var winners = results.Where(result => !result.IsDuplicate).ToList();
            var duplicates = results.Where(result => result.IsDuplicate).ToList();

            Assert.Single(winners);
            Assert.Equal(attempts - 1, duplicates.Count);
            Assert.All(duplicates, result => Assert.NotNull(result.ProcessedAtUtc));

            Assert.Equal(1, await CountItemsAsync(sku));
            Assert.Single(await GetProcessedAsync(eventId, handler));
        }
        finally
        {
            await CleanupAsync(eventId, sku);
        }
    }

    private Task<IdempotencyResult> ProcessInNewScope(
        string eventId,
        string handler,
        Func<InventoryDbContext, Task> businessOperation) =>
        Task.Run(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IIdempotentProcessor>();
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            return await processor.TryProcessAsync(
                ProcessedMessage.Create(eventId, handler),
                () => businessOperation(context),
                CancellationToken.None);
        });

    private static Task CreateItemAsync(InventoryDbContext context, string sku, int quantity)
    {
        context.InventoryItems.Add(InventoryItem.Create(
            new InventorySku(sku),
            quantity,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    private async Task<int> CountItemsAsync(string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryItems.CountAsync(item => item.Id == new InventorySku(sku));
    }

    private async Task<bool> IsProcessedAsync(string eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        return await store.GetAsync(eventId, handler, CancellationToken.None) is not null;
    }

    private async Task<IReadOnlyList<ProcessedMessage>> GetProcessedAsync(string eventId, string? handler = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var query = context.ProcessedMessages.Where(message => message.Key == eventId);
        if (handler is not null)
            query = query.Where(message => message.Handler == handler);

        return await query.ToListAsync();
    }

    private async Task CleanupAsync(string? eventId, params string[] skus)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        if (eventId is not null)
        {
            await context.ProcessedMessages
                .Where(message => message.Key == eventId)
                .ExecuteDeleteAsync();
        }

        foreach (var sku in skus)
        {
            await context.InventoryItems
                .Where(item => item.Id == new InventorySku(sku))
                .ExecuteDeleteAsync();
        }
    }

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}
