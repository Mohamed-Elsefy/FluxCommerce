using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.6 result of applying an Inventory reservation result event to the saga, describing how the
/// transition was classified (<see cref="OrderSagaTransitionOutcome"/>) and which saga it concerned.
/// </summary>
/// <remarks>
/// Phase 8.8 adds <see cref="Commands"/>: the immediate compensation release commands staged while
/// applying the result. Normally empty; a late <c>inventory.reserved</c> result that arrives on a saga
/// whose release was already requested (the order was cancelled) stages one
/// <c>inventory.release_requested</c> command per newly recorded reservation, so the reservation is
/// released immediately instead of leaking until Inventory expiry.
/// </remarks>
public sealed record OrderSagaTransitionResult(
    OrderSagaTransitionOutcome Outcome,
    Guid SagaId,
    IReadOnlyCollection<InventoryReleaseRequestedIntegrationEvent> Commands = null!);