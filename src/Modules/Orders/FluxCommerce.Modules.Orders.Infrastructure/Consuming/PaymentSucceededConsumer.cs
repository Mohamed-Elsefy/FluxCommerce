using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 10.4: Orders consumer for <c>payments.payment_succeeded</c>. Thin transport-agnostic adapter that
/// validates the payload and delegates the saga + order confirmation to
/// <see cref="OrderSagaOrchestrator.ApplyPaymentSucceededAsync"/>. Idempotency is scoped by
/// <c>EventId + HandlerName</c> via the Orders idempotent processor so duplicate delivery
/// returns success without re-confirming the order or bumping saga version.
/// </summary>
public sealed class PaymentSucceededConsumer : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyPaymentSucceeded";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<PaymentSucceededConsumer> _logger;

    public PaymentSucceededConsumer(OrderSagaOrchestrator orchestrator, ILogger<PaymentSucceededConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(PaymentSucceededIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);

        _logger.LogInformation("Payment {PaymentId} for order {OrderId} succeeded (intent {IntentId}); applying saga confirmation.",
            @event.PaymentId, @event.OrderId, @event.ProviderPaymentIntentId);

        return _orchestrator.ApplyPaymentSucceededAsync(
            new OrderId(@event.OrderId),
            @event.PaymentId,
            @event.ProviderPaymentIntentId,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.ProcessedAtUtc,
            cancellationToken);
    }

    private static void Validate(PaymentSucceededIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(@event));
        if (@event.PaymentId == Guid.Empty)
            throw new ArgumentException("PaymentId is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.ProviderPaymentIntentId))
            throw new ArgumentException("ProviderPaymentIntentId is required.", nameof(@event));
        if (@event.ProcessedAtUtc == default)
            throw new ArgumentException("ProcessedAtUtc is required.", nameof(@event));
    }
}
