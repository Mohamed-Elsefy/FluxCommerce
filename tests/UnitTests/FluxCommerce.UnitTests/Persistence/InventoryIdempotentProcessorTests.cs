using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 7.4: the atomic idempotent-processing boundary (<see cref="IIdempotentProcessor"/> /
/// <see cref="InventoryIdempotentProcessor"/>) against a real (Sqlite) engine. Proves that Business
/// State + ProcessedMessage commit together, that a business failure rolls both back (the message stays
/// retryable), that a duplicate is acknowledged without re-executing business logic, and that distinct
/// handlers for the same event remain independent. The concurrent-race proof lives on real SQL Server in
/// the integration tests.
/// </summary>
public class InventoryIdempotentProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryIdempotentProcessorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task TryProcessAsync_Success_CommitsBusinessStateAndProcessedMessageAtomically()
    {
        var (context, processor) = CreateProcessor();
        var message = CreateMessage();

        var result = await processor.TryProcessAsync(
            message,
            CreateItemBusinessOperation(context, "PROC-SUCCESS-001", 10),
            CancellationToken.None);

        Assert.False(result.IsDuplicate);
        Assert.True(await context.InventoryItems
            .AnyAsync(item => item.Id == new InventorySku("PROC-SUCCESS-001")));
        Assert.True(await context.ProcessedMessages
            .AnyAsync(processed => processed.Key == message.Key && processed.Handler == message.Handler));
    }

    [Fact]
    public async Task TryProcessAsync_BusinessFailure_RollsBackBusinessStateAndProcessedMessage()
    {
        var (context, processor) = CreateProcessor();
        var message = CreateMessage();

        async Task FailingOperation()
        {
            context.InventoryItems.Add(InventoryItem.Create(
                new InventorySku("PROC-FAIL-001"),
                10,
                DateTimeOffset.UtcNow));
            throw new InvalidOperationException("Business processing failed.");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.TryProcessAsync(message, FailingOperation, CancellationToken.None));

        Assert.False(await context.InventoryItems
            .AnyAsync(item => item.Id == new InventorySku("PROC-FAIL-001")));
        Assert.False(await context.ProcessedMessages
            .AnyAsync(processed => processed.Key == message.Key && processed.Handler == message.Handler));
    }

    [Fact]
    public async Task TryProcessAsync_FailedAttempt_ThenRetry_LeavesMessageRetryable()
    {
        var (context, processor) = CreateProcessor();
        var message = CreateMessage();
        var executionCount = 0;

        Func<Task> BusinessOperation() => () =>
        {
            executionCount++;
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.TryProcessAsync(
                message,
                () => throw new InvalidOperationException("First attempt fails."),
                CancellationToken.None));

        var retry = await processor.TryProcessAsync(message, BusinessOperation(), CancellationToken.None);

        Assert.False(retry.IsDuplicate);
        Assert.Equal(1, executionCount);
        Assert.True(await context.ProcessedMessages
            .AnyAsync(processed => processed.Key == message.Key && processed.Handler == message.Handler));
    }

    [Fact]
    public async Task TryProcessAsync_Duplicate_DoesNotReExecuteBusinessLogic()
    {
        var first = CreateMessage();
        var (firstContext, firstProcessor) = CreateProcessor();
        var executionCount = 0;

        Func<Task> BusinessOperation() => () =>
        {
            executionCount++;
            return Task.CompletedTask;
        };

        await firstProcessor.TryProcessAsync(first, BusinessOperation(), CancellationToken.None);
        Assert.Equal(1, executionCount);

        var second = ProcessedMessage.Create(first.Key, first.Handler);
        await using var secondContext = CreateContext();
        var secondProcessor = new InventoryIdempotentProcessor(
            secondContext,
            new InventoryDuplicateDetector(secondContext),
            new InventoryIdempotencyStore(secondContext));

        var duplicateResult = await secondProcessor.TryProcessAsync(second, BusinessOperation(), CancellationToken.None);

        Assert.True(duplicateResult.IsDuplicate);
        Assert.NotNull(duplicateResult.ProcessedAtUtc);
        Assert.Equal(1, executionCount);
        Assert.Single(await secondContext.ProcessedMessages
            .Where(processed => processed.Key == first.Key && processed.Handler == first.Handler)
            .ToListAsync());
    }

    [Fact]
    public async Task TryProcessAsync_SameEvent_DifferentHandlers_RemainIndependent()
    {
        var eventId = Guid.NewGuid().ToString();
        var (context, processor) = CreateProcessor();

        var first = await processor.TryProcessAsync(
            CreateMessage(eventId, "Inventory.ReserveStock"),
            CreateItemBusinessOperation(context, "PROC-IND-001", 10),
            CancellationToken.None);
        var second = await processor.TryProcessAsync(
            CreateMessage(eventId, "Inventory.NotifySummary"),
            CreateItemBusinessOperation(context, "PROC-IND-002", 10),
            CancellationToken.None);

        Assert.False(first.IsDuplicate);
        Assert.False(second.IsDuplicate);

        var stored = await context.ProcessedMessages
            .Where(processed => processed.Key == eventId)
            .ToListAsync();

        Assert.Equal(2, stored.Count);
        Assert.Equal(2, stored.Select(processed => processed.Handler).Distinct().Count());
    }

    [Fact]
    public async Task TryProcessAsync_RejectsMessageWithoutHandler()
    {
        var (context, processor) = CreateProcessor();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => processor.TryProcessAsync(
                ProcessedMessage.Create(Guid.NewGuid().ToString(), handler: null),
                () => Task.CompletedTask,
                CancellationToken.None));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static ProcessedMessage CreateMessage(string? key = null, string? handler = null) =>
        ProcessedMessage.Create(key ?? Guid.NewGuid().ToString(), handler ?? "Inventory.ReserveStock");

    private static Func<Task> CreateItemBusinessOperation(
        InventoryDbContext context,
        string sku,
        int quantity) => () =>
    {
        context.InventoryItems.Add(InventoryItem.Create(
            new InventorySku(sku),
            quantity,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    };

    private (InventoryDbContext Context, InventoryIdempotentProcessor Processor) CreateProcessor()
    {
        var context = CreateContext();
        return (
            context,
            new InventoryIdempotentProcessor(
                context,
                new InventoryDuplicateDetector(context),
                new InventoryIdempotencyStore(context)));
    }

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
