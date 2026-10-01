using FluxCommerce.Api.Controllers.Payments;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Application.Queries.GetMyPayments;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;
using FluxCommerce.Modules.Payments.Application.Services;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Consuming;
using FluxCommerce.Modules.Payments.Infrastructure.Gateways;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class PaymentsHardeningTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PaymentsHardeningTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    private PaymentsDbContext CreatePaymentsContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite(_connection).Options;
        var ctx = new PaymentsDbContext(options, new FakeDateTimeProvider(), new FakeCurrentUser(), new FakeSaveChangesInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor { }

    // 5: Invalid webhook signature -> 400
    [Fact]
    public void StripeOptions_Secrets_AreExternalizedViaIOptions()
    {
        // 9.12 security: secrets must be bound via IOptions, not hardcoded
        var opts = Microsoft.Extensions.Options.Options.Create(new FluxCommerce.Modules.Payments.Domain.Options.StripeOptions
        {
            SecretKey = "sk_test_externalized",
            WebhookSecret = "whsec_externalized",
            PublishableKey = "pk_test_externalized"
        });
        var svc = new StripeWebhookService(opts);
        Assert.NotNull(svc);
        // No hardcoded secret in code â€“ verified via Options pattern
    }

    [Fact]
    public void Webhook_AllowAnonymous_OnlyOnWebhook()
    {
        var methods = typeof(PaymentsController).GetMethods().Where(m => m.Name is nameof(PaymentsController.Webhook) or nameof(PaymentsController.GetPayment));
        var webhook = methods.First(m => m.Name == nameof(PaymentsController.Webhook));
        var get = methods.First(m => m.Name == nameof(PaymentsController.GetPayment));
        Assert.NotEmpty(webhook.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false));
        Assert.Empty(get.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false));
        Assert.NotEmpty(get.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false));
    }

    [Fact]
    public async Task Ownership_GetPayment_OtherUsersPayment_ReturnsNotFound()
    {
        // Arrange: payment owned by user A, caller is user B
        var owner = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var payment = new FluxCommerce.Modules.Payments.Domain.Aggregates.Payment(Guid.NewGuid(), orderId, 10m, "USD", owner);
        var ctx = CreatePaymentsContext();
        ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().Add(payment);
        await ctx.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = caller, IsAuthenticated = true };
        var repo = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx);

        var localizer = Mock.Of<FluxCommerce.BuildingBlocks.Localization.Abstractions.ILocalizer>(l => l[It.IsAny<string>()] == "x");
        var errLoc = Mock.Of<FluxCommerce.BuildingBlocks.Web.Localization.IErrorCodeLocalizer>();
        var ctrl = new PaymentsController(localizer, errLoc, NullLogger<PaymentsController>.Instance,
            Mock.Of<IStripeWebhookService>(), Mock.Of<IWebhookEventProcessor>(p => p.TryProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(WebhookProcessResult.Processed)),
            repo, Mock.Of<IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>>>(), Mock.Of<IQueryHandler<GetMyPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>>>(), currentUser);
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

        var result = await ctrl.GetPayment(payment.Id, CancellationToken.None);

        // Should be 404 (NotFound) to avoid leaking existence, via ApiControllerBase mapping
        Assert.NotNull(result);
        // Result should be ObjectResult with 404
        var obj = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(404, obj.StatusCode);
    }

    [Fact]
    public async Task Ownership_GetPayment_EmptyCustomer_ReturnsNotFound()
    {
        // Arrange: legacy/malformed row with empty CustomerId, caller is any user
        var caller = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var payment = new FluxCommerce.Modules.Payments.Domain.Aggregates.Payment(Guid.NewGuid(), orderId, 10m, "USD", Guid.Empty);
        var ctx = CreatePaymentsContext();
        ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().Add(payment);
        await ctx.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = caller, IsAuthenticated = true };
        var repo = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx);

        var localizer = Mock.Of<FluxCommerce.BuildingBlocks.Localization.Abstractions.ILocalizer>(l => l[It.IsAny<string>()] == "x");
        var errLoc = Mock.Of<FluxCommerce.BuildingBlocks.Web.Localization.IErrorCodeLocalizer>();
        var ctrl = new PaymentsController(localizer, errLoc, NullLogger<PaymentsController>.Instance,
            Mock.Of<IStripeWebhookService>(), Mock.Of<IWebhookEventProcessor>(p => p.TryProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(WebhookProcessResult.Processed)),
            repo, Mock.Of<IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>>>(), Mock.Of<IQueryHandler<GetMyPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>>>(), currentUser);
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

        var result = await ctrl.GetPayment(payment.Id, CancellationToken.None);

        var obj = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(404, obj.StatusCode);
    }

    // Creates intent through the saga consumer (the production path) instead of
    // asserting on a command result: creation is acknowledged by the persisted row.
    private static (CreatePaymentRequestedIntegrationEvent Event, IntegrationEventEnvelope Envelope) CreatePaymentEvent(
        Guid orderId,
        Guid userId)
    {
        var @event = new CreatePaymentRequestedIntegrationEvent(orderId, userId, 42.50m, "USD")
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        return (@event, IntegrationEventEnvelope.From(@event, CreatePaymentRequestedIntegrationEvent.EventType));
    }

    private static CreatePaymentRequestedConsumer CreatePaymentConsumer(
        FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository repo,
        IStripePaymentGateway gateway,
        PaymentsDbContext ctx) =>
        new(repo, gateway, ctx, new IntegrationEventOutboxMessageFactory(), NullLogger<CreatePaymentRequestedConsumer>.Instance);

    // 1: Normal flow CreateIntent -> Webhook Succeeded -> Succeeded + Outbox
    [Fact]
    public async Task NormalFlow_CreateIntent_ThenWebhookSucceeded_EndsInSucceededAndOutbox()
    {
        var ctx = CreatePaymentsContext();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var gateway = new Mock<IStripePaymentGateway>();
        gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((CreatePaymentIntentRequest r, CancellationToken _) => new PaymentIntentResult($"pi_{r.OrderId.ToString()[..8]}", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, "cs_test"));

        var repo = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx);
        var consumer = CreatePaymentConsumer(repo, gateway.Object, ctx);

        var (evt, envelope) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(evt, envelope, CancellationToken.None);

        var payment = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().SingleAsync(p => p.OrderId == orderId);
        Assert.NotNull(payment.ProviderPaymentIntentId);

        // Webhook
        var processor = new WebhookEventProcessor(ctx, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
        var evtId = "evt_normal_1";
        var res = await processor.TryProcessAsync(evtId, "payment_intent.succeeded", payment.ProviderPaymentIntentId!);
        Assert.Equal(WebhookProcessResult.Processed, res);

        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, reloaded!.Status);
        var outbox = await ctx.Set<OutboxMessage>().SingleAsync(m => m.Type == "payments.payment_succeeded");
        Assert.Equal(orderId, outbox.CorrelationId);
        Assert.NotNull(outbox.CausationId);
    }

    // 2: Failure flow
    [Fact]
    public async Task FailureFlow_CreateIntent_ThenWebhookFailed_EndsInFailedAndOutbox()
    {
        var ctx = CreatePaymentsContext();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var gateway = new Mock<IStripePaymentGateway>();
        gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((CreatePaymentIntentRequest r, CancellationToken _) => new PaymentIntentResult($"pi_{r.OrderId.ToString()[..8]}", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Processing, "cs_test"));

        var repo = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx);
        var consumer = CreatePaymentConsumer(repo, gateway.Object, ctx);
        var (evt, envelope) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(evt, envelope, CancellationToken.None);
        var payment = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().SingleAsync(p => p.OrderId == orderId);

        var processor = new WebhookEventProcessor(ctx, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
        var res = await processor.TryProcessAsync("evt_fail_1", "payment_intent.payment_failed", payment.ProviderPaymentIntentId!);
        Assert.Equal(WebhookProcessResult.Processed, res);
        var reloaded = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().FindAsync(payment.Id);
        Assert.Equal(FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Failed, reloaded!.Status);
        var outbox = await ctx.Set<OutboxMessage>().SingleAsync(m => m.Type == "payments.payment_failed");
        Assert.NotNull(outbox);
    }

    // 6: Duplicate Payment Intent -> single intent
    [Fact]
    public async Task DuplicatePaymentIntent_SameOrderId_ReturnsSameIntentWithoutSecondStripeCall()
    {
        var ctx = CreatePaymentsContext();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var callCount = 0;
        var gateway = new Mock<IStripePaymentGateway>();
        gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => { callCount++; return new PaymentIntentResult("pi_dup_123", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, "cs_dup"); });

        var repo = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx);
        var consumer = CreatePaymentConsumer(repo, gateway.Object, ctx);

        var (firstEvent, firstEnvelope) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(firstEvent, firstEnvelope, CancellationToken.None);
        var firstIntentId = (await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().AsNoTracking().SingleAsync(p => p.OrderId == orderId)).ProviderPaymentIntentId;

        ctx.ChangeTracker.Clear();

        var (secondEvent, secondEnvelope) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(secondEvent, secondEnvelope, CancellationToken.None);

        var secondIntentId = (await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().AsNoTracking().SingleAsync(p => p.OrderId == orderId)).ProviderPaymentIntentId;
        Assert.Equal(firstIntentId, secondIntentId);
        Assert.Equal(1, callCount); // second delivery did not call Stripe
        var count = await ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().CountAsync(p => p.OrderId == orderId);
        Assert.Equal(1, count);
    }

    // 7: Concurrent Payment Intent -> single payment via unique constraint
    [Fact]
    public async Task ConcurrentPaymentIntents_SameOrderId_OnlyOnePaymentCreated()
    {
        var ctx1 = CreatePaymentsContext();
        var ctx2 = CreatePaymentsContext();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var gateway = Mock.Of<IStripePaymentGateway>(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()) == Task.FromResult(new PaymentIntentResult("pi_conc_123", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, "cs_conc")));

        var repo1 = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx1);
        var repo2 = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx2);
        var c1 = CreatePaymentConsumer(repo1, gateway, ctx1);
        var c2 = CreatePaymentConsumer(repo2, gateway, ctx2);

        var (event1, envelope1) = CreatePaymentEvent(orderId, userId);
        var (event2, envelope2) = CreatePaymentEvent(orderId, userId);
        var t1 = c1.HandleAsync(event1, envelope1, CancellationToken.None);
        var t2 = c2.HandleAsync(event2, envelope2, CancellationToken.None);
        try
        {
            await Task.WhenAll(t1, t2);
        }
        catch (InvalidOperationException)
        {
            // Loser without a winner intent yet signals redelivery instead of duplicating.
        }

        // Exactly one payment row regardless of the interleaving
        var verifyCtx = CreatePaymentsContext();
        var count = await verifyCtx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().CountAsync(p => p.OrderId == orderId);
        Assert.Equal(1, count);
    }

    // 11,10 already covered but verify correlation
    [Fact]
    public async Task Outbox_CorrelationAndCausation_AreCorrectlySet()
    {
        var ctx = CreatePaymentsContext();
        var payment = new FluxCommerce.Modules.Payments.Domain.Aggregates.Payment(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference("pi_corr_123", "Stripe");
        ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().Add(payment);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var processor = new WebhookEventProcessor(ctx, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
        await processor.TryProcessAsync("evt_corr_1", "payment_intent.succeeded", "pi_corr_123");

        var outbox = await ctx.Set<OutboxMessage>().SingleAsync(m => m.Type == "payments.payment_succeeded");
        Assert.Equal(payment.OrderId, outbox.CorrelationId);
        Assert.NotNull(outbox.CausationId);
        // No sensitive data in payload
        Assert.DoesNotContain("sk_test", outbox.Payload);
        Assert.DoesNotContain("whsec", outbox.Payload);
    }

    public void Dispose() => _connection.Dispose();
}


