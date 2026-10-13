using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.7 result of requesting the compensation release of an order's reservations from an
/// <c>orders.order_cancelled</c> message. <see cref="SagaFound"/> is <c>false</c> when the workflow
/// never reserved inventory for the order (a benign no-op). <see cref="ReleaseIssued"/> is <c>true</c>
/// only when the saga had recorded reservations and release commands were staged.
/// <see cref="Commands"/> holds the release request contracts staged for publication, one per recorded
/// reservation, in arrival order.
/// </summary>
public sealed record OrderSagaReleaseResult(
    bool SagaFound,
    bool ReleaseIssued,
    Guid? SagaId,
    IReadOnlyCollection<InventoryReleaseRequestedIntegrationEvent> Commands);
