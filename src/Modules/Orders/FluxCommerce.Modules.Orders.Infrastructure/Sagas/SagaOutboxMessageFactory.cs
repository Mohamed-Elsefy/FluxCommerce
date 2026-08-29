using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.3 mapping from the integration commands produced by the Order/Inventory Saga orchestrator
/// to <see cref="OutboxMessage"/> records. The orchestrator stages the saga and the outbox records
/// together inside the module's idempotent-processing unit of work, so a reservation request either
/// commits atomically with the saga state or is not issued at all. The integration event is already
/// fully formed (it carries the canonical metadata the saga assigned), so the mapping delegates to the
/// shared <see cref="IntegrationEventOutboxMessageFactory"/>. Phase 8.7 adds the compensation mapping:
/// the same factory maps the <see cref="InventoryReleaseRequestedIntegrationEvent"/> issued when a
/// cancelled order must release its previously reserved inventory.
/// Phase 10.3 adds payment initiation: the same factory maps
/// <see cref="CreatePaymentRequestedIntegrationEvent"/> staged after the saga enters
/// <c>WaitingForPayment</c>.
/// </summary>
public sealed class SagaOutboxMessageFactory
{
    private readonly IntegrationEventOutboxMessageFactory _outboxMessageFactory;

    public SagaOutboxMessageFactory(IntegrationEventOutboxMessageFactory outboxMessageFactory)
    {
        _outboxMessageFactory = outboxMessageFactory ?? throw new ArgumentNullException(nameof(outboxMessageFactory));
    }

    public OutboxMessage Create(InventoryReservationRequestedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return _outboxMessageFactory.Create(InventoryReservationRequestedIntegrationEvent.EventType, integrationEvent);
    }

    public OutboxMessage Create(InventoryReleaseRequestedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return _outboxMessageFactory.Create(InventoryReleaseRequestedIntegrationEvent.EventType, integrationEvent);
    }

    public OutboxMessage Create(CreatePaymentRequestedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return _outboxMessageFactory.Create(CreatePaymentRequestedIntegrationEvent.EventType, integrationEvent);
    }

    public OutboxMessage Create(RefundPaymentRequestedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return _outboxMessageFactory.Create(RefundPaymentRequestedIntegrationEvent.EventType, integrationEvent);
    }
}