using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Orders consumer for <c>payments.payment_initiated</c>. Thin transport-agnostic adapter that
/// records the payment correlation on the saga (<c>MarkPaymentRequested</c>) so later steps
/// (e.g. refund) use tracked ids instead of placeholders. Idempotency is scoped by
/// <c>EventId + HandlerName</c> via the Orders idempotent processor; duplicate delivery with
/// the same payment id is a no-op, and a different payment id for the same order is rejected
/// as invalid (cross-payment contamination is never applied).
/// </summary>
public sealed class PaymentInitiatedConsumer : IIntegrationEventHandler<PaymentInitiatedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyPaymentInitiated";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<PaymentInitiatedConsumer> _logger;

    public PaymentInitiatedConsumer(OrderSagaOrchestrator orchestrator, ILogger<PaymentInitiatedConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(PaymentInitiatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);

        _logger.LogInformation("Payment {PaymentId} for order {OrderId} initiated (intent {IntentId}); recording saga correlation.",
            @event.PaymentId, @event.OrderId, @event.ProviderPaymentIntentId);

        return _orchestrator.ApplyPaymentInitiatedAsync(
            new OrderId(@event.OrderId),
            @event.PaymentId,
            @event.ProviderPaymentIntentId,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.ProcessedAtUtc,
            cancellationToken);
    }

    private static void Validate(PaymentInitiatedIntegrationEvent @event)
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
