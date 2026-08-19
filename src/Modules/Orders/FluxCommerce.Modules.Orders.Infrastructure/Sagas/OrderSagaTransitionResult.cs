namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.6 result of applying an Inventory reservation result event to the saga, describing how the
/// transition was classified (<see cref="OrderSagaTransitionOutcome"/>) and which saga it concerned.
/// </summary>
public sealed record OrderSagaTransitionResult(
    OrderSagaTransitionOutcome Outcome,
    Guid SagaId);