using System.Text.Json;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.3 mapping from the integration commands produced by the Order/Inventory Saga orchestrator
/// to <see cref="OutboxMessage"/> records. The orchestrator stages the saga and the outbox records
/// together inside the module's idempotent-processing unit of work, so a reservation request either
/// commits atomically with the saga state or is not issued at all. Unlike the domain-event outbox
/// factory, the integration event is already fully formed (it carries the canonical metadata the saga
/// assigned), so the factory only serializes it.
/// </summary>
public sealed class SagaOutboxMessageFactory
{
    public OutboxMessage Create(InventoryReservationRequestedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = integrationEvent.EventId,
            Type = InventoryReservationRequestedIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent),
            CorrelationId = integrationEvent.CorrelationId,
            CausationId = integrationEvent.CausationId,
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            Error = null
        };
    }
}