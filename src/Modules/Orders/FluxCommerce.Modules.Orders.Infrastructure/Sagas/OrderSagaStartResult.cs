using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.3 result of starting (or re-delivering) the Order/Inventory Saga from an
/// <c>orders.order_created</c> message. <see cref="SagaCreated"/> is <c>true</c> only for the first,
/// actually-creating delivery: a duplicate or terminal saga is a no-op. <see cref="Commands"/> holds the
/// reservation request contracts staged for publication on the creating delivery, one per line item.
/// </summary>
public sealed record OrderSagaStartResult(
    bool SagaCreated,
    Guid SagaId,
    IReadOnlyCollection<InventoryReservationRequestedIntegrationEvent> Commands);
