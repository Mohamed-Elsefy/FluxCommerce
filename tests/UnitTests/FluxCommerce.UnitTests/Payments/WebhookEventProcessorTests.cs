using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Services;
using FluxCommerce.Modules.Payments.Domain.Entities;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class WebhookEventProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public WebhookEventProcessorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task TryProcessAsync_FirstDelivery_RecordsAndReturnsProcessed()
    {
        var (ctx, processor) = CreateProcessor();
        const string eventId = "evt_first_123";
        const string eventType = "payment_intent.succeeded";

        var result = await processor.TryProcessAsync(eventId, eventType);

        Assert.Equal(WebhookProcessResult.Processed, result);
        var stored = await ctx.Set<ProcessedWebhookEvent>().SingleOrDefaultAsync(e => e.StripeEventId == eventId);
        Assert.NotNull(stored);
        Assert.Equal(eventType, stored!.EventType);
    }

    [Fact]
    public async Task TryProcessAsync_DuplicateDelivery_ReturnsAlreadyProcessedWithoutDuplicateRecord()
    {
        var (ctx, processor) = CreateProcessor();
        const string eventId = "evt_dup_456";
        const string eventType = "payment_intent.succeeded";

        var first = await processor.TryProcessAsync(eventId, eventType);
        var second = await processor.TryProcessAsync(eventId, eventType);

        Assert.Equal(WebhookProcessResult.Processed, first);
        Assert.Equal(WebhookProcessResult.AlreadyProcessed, second);

        var count = await ctx.Set<ProcessedWebhookEvent>().CountAsync(e => e.StripeEventId == eventId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TryProcessAsync_ConcurrentDuplicate_UniqueIndexHandlesRaceAndReturnsSuccess()
    {
        // Two processors sharing the same underlying Sqlite connection (same DB)
        var (ctx1, processor1) = CreateProcessor();
        using var ctx2 = CreateContext();
        var processor2 = new WebhookEventProcessor(ctx2, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);

        const string eventId = "evt_concurrent_789";
        const string eventType = "payment_intent.succeeded";

        // Run concurrently; only one should succeed with Processed, other AlreadyProcessed
        var t1 = processor1.TryProcessAsync(eventId, eventType);
        var t2 = processor2.TryProcessAsync(eventId, eventType);

        var results = await Task.WhenAll(t1, t2);

        Assert.Contains(WebhookProcessResult.Processed, results);
        Assert.Contains(WebhookProcessResult.AlreadyProcessed, results);

        // Verify only one record exists (unique constraint enforced)
        using var verifyCtx = CreateContext();
        var count = await verifyCtx.Set<ProcessedWebhookEvent>().CountAsync(e => e.StripeEventId == eventId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TryProcessAsync_DifferentEventIds_BothProcessedIndependently()
    {
        var (_, processor) = CreateProcessor();

        var r1 = await processor.TryProcessAsync("evt_a_1", "payment_intent.succeeded");
        var r2 = await processor.TryProcessAsync("evt_b_2", "payment_intent.succeeded");

        Assert.Equal(WebhookProcessResult.Processed, r1);
        Assert.Equal(WebhookProcessResult.Processed, r2);
    }

    // 9.10: Stripe Event â†’ Payment State

    [Fact]
    public async Task TryProcessAsync_PaymentIntentSucceeded_TransitionsPaymentToSucceeded()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_succeed_123");
        const string evtId = "evt_succ_1";

        var result = await processor.TryProcessAsync(evtId, "payment_intent.succeeded", "pi_succeed_123");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, reloaded!.Status);
        Assert.Equal("pi_succeed_123", reloaded.ProviderPaymentIntentId);
    }

    [Fact]
    public async Task TryProcessAsync_PaymentIntentFailed_TransitionsPaymentToFailed()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_fail_123");
        const string evtId = "evt_fail_1";

        var result = await processor.TryProcessAsync(evtId, "payment_intent.payment_failed", "pi_fail_123");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Failed, reloaded!.Status);
    }

    [Fact]
    public async Task TryProcessAsync_OutOfOrderAfterSucceeded_IsSafelyIgnoredWithoutCorruptingState()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_ooo_123");
        // First succeed
        await processor.TryProcessAsync("evt_first", "payment_intent.succeeded", "pi_ooo_123");
        var afterSucceed = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, afterSucceed!.Status);

        // Late processing event (out-of-order) must not regress terminal state
        var outOfOrder = await processor.TryProcessAsync("evt_ooo_2", "payment_intent.processing", "pi_ooo_123");

        Assert.Equal(WebhookProcessResult.Processed, outOfOrder);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, reloaded!.Status);
    }

    [Fact]
    public async Task TryProcessAsync_UnsupportedEvent_ReturnsSuccessWithoutThrowing()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_unk_123");
        const string evtId = "evt_unsupported_1";

        var result = await processor.TryProcessAsync(evtId, "charge.refunded", "pi_unk_123");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        // No state change for unsupported
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Pending, reloaded!.Status);
        var stored = await ctx.Set<ProcessedWebhookEvent>().SingleAsync(e => e.StripeEventId == evtId);
        Assert.NotNull(stored);
    }

    // 9.11: Outbox integration

    [Fact]
    public async Task TryProcessAsync_Succeeded_CreatesPaymentSucceededOutboxInSameTransaction()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_outbox_succ");
        const string evtId = "evt_outbox_succ";

        var result = await processor.TryProcessAsync(evtId, "payment_intent.succeeded", "pi_outbox_succ");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var outbox = await ctx.Set<OutboxMessage>().SingleOrDefaultAsync(m => m.Type == "payments.payment_succeeded");
        Assert.NotNull(outbox);
        Assert.Equal(payment.OrderId, outbox!.CorrelationId);
        Assert.NotNull(outbox.CausationId);
        Assert.Contains("pi_outbox_succ", outbox.Payload);
        // Atomic: ProcessedWebhookEvent + Payment + Outbox all committed together
        Assert.True(await ctx.Set<ProcessedWebhookEvent>().AnyAsync(e => e.StripeEventId == evtId));
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, reloaded!.Status);
    }

    [Fact]
    public async Task TryProcessAsync_Failed_CreatesPaymentFailedOutboxWithCorrelation()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_outbox_fail");
        const string evtId = "evt_outbox_fail";

        var result = await processor.TryProcessAsync(evtId, "payment_intent.payment_failed", "pi_outbox_fail");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var outbox = await ctx.Set<OutboxMessage>().SingleOrDefaultAsync(m => m.Type == "payments.payment_failed");
        Assert.NotNull(outbox);
        Assert.Equal(payment.OrderId, outbox!.CorrelationId);
        Assert.NotNull(outbox.CausationId);
        Assert.Contains("pi_outbox_fail", outbox.Payload);
        Assert.True(await ctx.Set<ProcessedWebhookEvent>().AnyAsync(e => e.StripeEventId == evtId));
    }

    [Fact]
    public async Task TryProcessAsync_Unsupported_DoesNotCreateOutbox()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_outbox_unsupported");
        const string evtId = "evt_outbox_unsupported";

        var result = await processor.TryProcessAsync(evtId, "charge.refunded", "pi_outbox_unsupported");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var outboxCount = await ctx.Set<OutboxMessage>().CountAsync();
        Assert.Equal(0, outboxCount);
    }

    [Fact]
    public async Task TryProcessAsync_Duplicate_DoesNotCreateSecondOutbox()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_outbox_dup");
        const string evtId = "evt_outbox_dup";

        await processor.TryProcessAsync(evtId, "payment_intent.succeeded", "pi_outbox_dup");
        var second = await processor.TryProcessAsync(evtId, "payment_intent.succeeded", "pi_outbox_dup");

        Assert.Equal(WebhookProcessResult.AlreadyProcessed, second);
        var outboxCount = await ctx.Set<OutboxMessage>().CountAsync(m => m.Type == "payments.payment_succeeded");
        Assert.Equal(1, outboxCount);
    }

    // A2: amount verification

    [Fact]
    public async Task TryProcessAsync_MatchingAmount_TransitionsPaymentAndOutboxes()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_amt_ok");

        var result = await processor.TryProcessAsync("evt_amt_ok", "payment_intent.succeeded", "pi_amt_ok", 1999, "usd");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, reloaded!.Status);
        Assert.Equal(1, await ctx.Set<OutboxMessage>().CountAsync(m => m.Type == "payments.payment_succeeded"));
    }

    [Fact]
    public async Task TryProcessAsync_AmountMismatch_RecordsWithoutTransitionOrOutbox()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_amt_bad");

        var result = await processor.TryProcessAsync("evt_amt_bad", "payment_intent.succeeded", "pi_amt_bad", 1998, "usd");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Pending, reloaded!.Status);
        Assert.Equal(0, await ctx.Set<OutboxMessage>().CountAsync());
        Assert.True(await ctx.Set<ProcessedWebhookEvent>().AnyAsync(e => e.StripeEventId == "evt_amt_bad"));
    }

    [Fact]
    public async Task TryProcessAsync_CurrencyMismatch_RecordsWithoutTransitionOrOutbox()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_cur_bad");

        var result = await processor.TryProcessAsync("evt_cur_bad", "payment_intent.succeeded", "pi_cur_bad", 1999, "EUR");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Pending, reloaded!.Status);
        Assert.Equal(0, await ctx.Set<OutboxMessage>().CountAsync());
        Assert.True(await ctx.Set<ProcessedWebhookEvent>().AnyAsync(e => e.StripeEventId == "evt_cur_bad"));
    }

    [Fact]
    public async Task TryProcessAsync_FailedWithStripeReason_StoresRealReasonInOutbox()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_reason_1");

        var result = await processor.TryProcessAsync("evt_reason_1", "payment_intent.payment_failed", "pi_reason_1", null, null, "insufficient_funds");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var outbox = await ctx.Set<OutboxMessage>().SingleAsync(m => m.Type == "payments.payment_failed");
        Assert.Contains("insufficient_funds", outbox.Payload);
    }

    [Fact]
    public async Task TryProcessAsync_Canceled_TransitionsPaymentAndStagesCanceledOutbox()
    {
        var (ctx, processor) = CreateProcessor();
        var payment = CreatePaymentWithIntent(ctx, "pi_cancel_1");

        var result = await processor.TryProcessAsync("evt_cancel_1", "payment_intent.canceled", "pi_cancel_1");

        Assert.Equal(WebhookProcessResult.Processed, result);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Cancelled, reloaded!.Status);
        var outbox = await ctx.Set<OutboxMessage>().SingleOrDefaultAsync(m => m.Type == "payments.payment_canceled");
        Assert.NotNull(outbox);
        Assert.Equal(0, await ctx.Set<OutboxMessage>().CountAsync(m => m.Type == "payments.payment_succeeded" || m.Type == "payments.payment_failed"));
        Assert.True(await ctx.Set<ProcessedWebhookEvent>().AnyAsync(e => e.StripeEventId == "evt_cancel_1"));
    }

    private static FluxCommerce.Modules.Payments.Domain.Aggregates.Payment CreatePaymentWithIntent(PaymentsDbContext ctx, string paymentIntentId)
    {
        var payment = new FluxCommerce.Modules.Payments.Domain.Aggregates.Payment(Guid.NewGuid(), Guid.NewGuid(), 19.99m, "USD");
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().Add(payment);
        ctx.SaveChanges();
        // Detach to simulate fresh load inside processor
        ctx.ChangeTracker.Clear();
        return payment;
    }

    public void Dispose() => _connection.Dispose();

    private (PaymentsDbContext Context, WebhookEventProcessor Processor) CreateProcessor()
    {
        var ctx = CreateContext();
        var processor = new WebhookEventProcessor(ctx, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
        return (ctx, processor);
    }

    private PaymentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options;

        var ctx = new PaymentsDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        ctx.Database.EnsureCreated();
        return ctx;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor { }
}

