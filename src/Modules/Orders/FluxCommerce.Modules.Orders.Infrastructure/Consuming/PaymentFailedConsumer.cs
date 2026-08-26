using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 10.5: Orders consumer for <c>payments.payment_failed</c>. Thin adapter that validates the
/// payload and delegates compensation initiation to
/// <see cref="OrderSagaOrchestrator.ApplyPaymentFailedAsync"/>. Idempotency is scoped by
/// <c>EventId + HandlerName</c> via the Orders idempotent processor so duplicate delivery does not
/// re-stage inventory release commands.
/// </summary>
public sealed class PaymentFailedConsumer : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyPaymentFailed";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<PaymentFailedConsumer> _logger;

    public PaymentFailedConsumer(OrderSagaOrchestrator orchestrator, ILogger<PaymentFailedConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(PaymentFailedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);

        _logger.LogInformation("Payment {PaymentId} for order {OrderId} failed (reason '{Reason}', intent {IntentId}); initiating compensation.",
            @event.PaymentId, @event.OrderId, @event.FailureReason, @event.ProviderPaymentIntentId);

        return _orchestrator.ApplyPaymentFailedAsync(
            new OrderId(@event.OrderId),
            @event.PaymentId,
            @event.ProviderPaymentIntentId,
            @event.FailureReason,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.ProcessedAtUtc,
            cancellationToken);
    }

    private static void Validate(PaymentFailedIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(@event));
        if (@event.PaymentId == Guid.Empty)
            throw new ArgumentException("PaymentId is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.FailureReason))
            throw new ArgumentException("FailureReason is required.", nameof(@event));
        if (@event.ProcessedAtUtc == default)
            throw new ArgumentException("ProcessedAtUtc is required.", nameof(@event));
    }
}
