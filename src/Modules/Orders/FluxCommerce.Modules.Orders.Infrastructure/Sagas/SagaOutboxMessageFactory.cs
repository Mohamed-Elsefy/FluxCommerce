using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.3 mapping from the integration commands produced by the Order/Inventory Saga orchestrator
/// to <see cref="OutboxMessage"/> records. The orchestrator stages the saga and the outbox records
/// together inside the module's idempotent-processing unit of work, so a reservation request either
/// commits atomically with the saga state or is not issued at all. The integration event is already
/// fully formed (it carries the canonical metadata the saga assigned), so the mapping delegates to the
/// shared <see cref="IntegrationEventOutboxMessageFactory"/>.
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
}