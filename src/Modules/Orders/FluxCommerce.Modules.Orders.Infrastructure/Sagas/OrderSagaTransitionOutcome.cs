namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.6 outcome of applying an Inventory reservation result (<c>inventory.reserved</c> /
/// <c>inventory.reservation_failed</c>) to the Order/Inventory Saga:
/// <list type="bullet">
/// <item><see cref="Applied"/> — the result moved the saga (and, for a failure, the Order).</item>
/// <item><see cref="Duplicate"/> — the same result was already applied (idempotent no-op).</item>
/// <item><see cref="Conflicting"/> — the saga is in the opposite terminal state; the result is
/// rejected and acknowledged (a success after a failure, or a failure after a success).</item>
/// <item><see cref="Invalid"/> — the saga is in a non-terminal state that cannot accept the result
/// (e.g. <c>Started</c>); an anomaly that is acknowledged without retrying.</item>
/// </list>
/// Only genuine anomalies (a result for an unknown saga/order) throw, so the delivery is retried then
/// dead-lettered; every well-formed result is always acknowledged without a retry loop.
/// </summary>
public enum OrderSagaTransitionOutcome
{
    Applied,
    Duplicate,
    Conflicting,
    Invalid
}