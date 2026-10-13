using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Orders consumer for <c>payments.payment_canceled</c>. Thin adapter that validates the
/// payload and delegates compensation initiation to
/// <see cref="OrderSagaOrchestrator.ApplyPaymentCanceledAsync"/>. Idempotency is scoped by
/// <c>EventId + HandlerName</c> via the Orders idempotent processor so duplicate delivery does not
/// re-stage inventory release commands.
/// </summary>
public sealed class PaymentCanceledConsumer : IIntegrationEventHandler<PaymentCanceledIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyPaymentCanceled";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<PaymentCanceledConsumer> _logger;

    public PaymentCanceledConsumer(OrderSagaOrchestrator orchestrator, ILogger<PaymentCanceledConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(PaymentCanceledIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);

        _logger.LogInformation("Payment {PaymentId} for order {OrderId} canceled (reason '{Reason}', intent {IntentId}); initiating compensation.",
            @event.PaymentId, @event.OrderId, @event.Reason, @event.ProviderPaymentIntentId);

        return _orchestrator.ApplyPaymentCanceledAsync(
            new OrderId(@event.OrderId),
            @event.PaymentId,
            @event.ProviderPaymentIntentId,
            @event.Reason,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.ProcessedAtUtc,
            cancellationToken);
    }

    private static void Validate(PaymentCanceledIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(@event));
        if (@event.PaymentId == Guid.Empty)
            throw new ArgumentException("PaymentId is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.Reason))
            throw new ArgumentException("Reason is required.", nameof(@event));
        if (@event.ProcessedAtUtc == default)
            throw new ArgumentException("ProcessedAtUtc is required.", nameof(@event));
    }
}
