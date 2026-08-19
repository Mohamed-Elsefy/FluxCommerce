namespace FluxCommerce.Modules.Orders.Domain.Enums;

/// <summary>
/// Phase 8.2 Saga state of the Order/Inventory workflow (ADR-009). The Saga coordinates the workflow;
/// it never owns Order or Inventory business state. <c>Started</c> is the saga created from
/// <c>OrderCreated</c>; <c>WaitingForInventory</c> follows the issued reservation request;
/// <c>Completed</c> and <c>Failed</c> are the terminal outcomes driven by the Inventory result events.
/// </summary>
public enum OrderSagaState
{
    Started = 0,

    WaitingForInventory = 1,

    Completed = 2,

    Failed = 3,
}