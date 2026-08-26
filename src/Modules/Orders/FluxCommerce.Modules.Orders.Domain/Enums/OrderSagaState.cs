namespace FluxCommerce.Modules.Orders.Domain.Enums;

/// <summary>
/// Phase 8.2 Saga state of the Order/Inventory workflow (ADR-009). The Saga coordinates the workflow;
/// it never owns Order or Inventory business state. <c>Started</c> is the saga created from
/// <c>OrderCreated</c>; <c>WaitingForInventory</c> follows the issued reservation request;
/// <c>Completed</c> and <c>Failed</c> are the terminal outcomes driven by the Inventory result events.
/// </summary>
/// <remarks>
/// Phase 10.2 extends the state machine to include Payment and compensation states
/// (plan §10.2). <c>WaitingForPayment</c> is entered after <c>inventory.reserved</c>
/// (WaitingForInventory → WaitingForPayment). <c>ReleasingInventory</c> is entered
/// after <c>payments.payment_failed</c> (WaitingForPayment → ReleasingInventory).
/// <c>Cancelled</c> (OrderCancelled) is entered after the compensation release
/// succeeds (ReleasingInventory → Cancelled) and is then finalized to
/// <c>Completed</c>. <c>Completed</c> and <c>Failed</c> remain terminals;
/// <c>Cancelled</c> is a terminal compensation outcome that may be finalized to
/// <c>Completed</c> for E2E traceability. Duplicate deliveries on the target
/// state are idempotent no-ops; any other undocumented transition is rejected.
/// </remarks>
public enum OrderSagaState
{
    Started = 0,

    WaitingForInventory = 1,

    Completed = 2,

    Failed = 3,

    /// <summary>
    /// Phase 10.2: inventory reserved, awaiting payment outcome
    /// (<c>inventory.reserved</c> → <c>WaitingForPayment</c>).
    /// </summary>
    WaitingForPayment = 4,

    /// <summary>
    /// Phase 10.2: payment failed, compensation release in progress
    /// (<c>payments.payment_failed</c> → <c>ReleasingInventory</c>).
    /// </summary>
    ReleasingInventory = 5,

    /// <summary>
    /// Phase 10.2: compensation completed, order cancelled
    /// (<c>ReleasingInventory → Cancelled</c>, also referred to as
    /// <c>OrderCancelled</c> in the Phase 10 plan).
    /// </summary>
    Cancelled = 6,
}
