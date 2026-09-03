using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 10.8: Orders consumer for payments.payment_refunded.
/// Transitions saga Refunding → ReleasingInventory and stages inventory release.
/// Idempotent via OrdersIdempotentProcessor.
/// </summary>
public sealed class PaymentRefundedConsumer : IIntegrationEventHandler<PaymentRefundedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyPaymentRefunded";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<PaymentRefundedConsumer> _logger;

    public PaymentRefundedConsumer(OrderSagaOrchestrator orchestrator, ILogger<PaymentRefundedConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(PaymentRefundedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);

        _logger.LogInformation("Payment {PaymentId} for order {OrderId} refunded (refund {RefundId}); initiating compensation release.",
            @event.PaymentId, @event.OrderId, @event.ProviderRefundId);

        return _orchestrator.ApplyPaymentRefundedAsync(
            new OrderId(@event.OrderId),
            @event.PaymentId,
            @event.ProviderRefundId,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.RefundedAtUtc,
            cancellationToken);
    }

    private static void Validate(PaymentRefundedIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty) throw new ArgumentException("OrderId is required.", nameof(@event));
        if (@event.PaymentId == Guid.Empty) throw new ArgumentException("PaymentId is required.", nameof(@event));
        if (@event.RefundedAtUtc == default) throw new ArgumentException("RefundedAtUtc is required.", nameof(@event));
    }
}
