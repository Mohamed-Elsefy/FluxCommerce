# ADR-010: Payment Module Ownership Boundary

## Status

Accepted

## Context

Phase 9 introduces the Payments bounded context as a new module in the Modular Monolith. Following the established pattern from ADR-009 (Saga Orchestration & Contract Placement), each module must own its business state exclusively. The audit must define the precise boundary between Payments and other modules, particularly Orders/Saga, to prevent cross-module database access and ensure maintainable module isolation.

The Phase 8 completion verified that:
- Orders owns Order state and Saga/orchestration state
- Inventory owns Inventory item state and reservation state
- The Saga coordinates the workflow but does not own business state

Phase 9 must similarly define that Payments owns Payment state, while Orders/Saga continues to own Order state.

## Decision

### Ownership boundary

- **Payments owns:**
  - Payment state (Pending, Processing, Succeeded, Failed, Cancelled)
  - Payment lifecycle (Payment Attempts / Transactions)
  - Webhook processing state (ProcessedWebhookEvents)
  - Provider references (ProviderPaymentIntentId, StripeEventId)

- **Orders/Saga owns:**
  - Order state (Pending, Cancelled, Completed/Confirmed)
  - Order/Saga orchestration state (Saga state table)
  - Inventory reservation state

- **No module may directly access another module's DbContext:**
  - Payments must not reference OrdersDbContext or InventoryDbContext
  - Orders must not reference PaymentsDbContext
  - Inventory must not reference PaymentsDbContext

### Communication pattern

Cross-module workflows use explicit Integration Event contracts over RabbitMQ, published reliably through each module's Outbox, consumed by the idempotent consumer infrastructure (Phase 7). No distributed transactions.

### Approved reference

Orders.Infrastructure MAY reference `FluxCommerce.Modules.Payments.Contracts` (the `Integration` and `IntegrationEvents` namespaces only) so it can consume `PaymentSucceeded` and `PaymentFailed` integration events. This mirrors the existing Phase 4.5 carve-out that lets Orders.Application reference the Cart/Catalog Contracts.

The architecture tests `Orders_ShouldNot_ReferenceOtherModules` and `Orders_ShouldNot_ReferenceInventory` are updated to encode this narrow carve-out. Payments never references Orders or Inventory internal layers.

### Saga coordination

The Order/Inventory Saga (Phase 8) may request a Payment by publishing a `PaymentRequested` integration event. The Payments module consumes this event, creates a PaymentIntent, and publishes `PaymentSucceeded` or `PaymentFailed` integration events. The Saga then consumes these events to transition the workflow.

The Saga does not directly modify Payment state; it relies on the Payments module's idempotent consumer infrastructure to commit business state atomically with the ProcessedMessage record.

## Consequences

Positive:
- Payments module can be implemented, tested, and evolved independently
- Clear ownership boundaries prevent accidental cross-module database access
- Saga can coordinate Payment outcomes through explicit integration events
- Architecture tests enforce module isolation (existing tests updated with narrow carve-outs)

Negative:
- Orders.Infrastructure gains a narrow, approved dependency on Payments.Contracts; the architecture tests must encode the carve-out
- Payments module must not grow Order- or Inventory-related logic; those responsibilities remain with their respective modules
- The two-module coordination (Orders + Payments) requires careful mapping of correlation/causation across async boundaries

## References

- ADR-009: Saga Orchestration & Contract Placement (established ownership pattern)
- ADR-007: Integration Event Contract Placement (contract placement strategy)
- ADR-008: Consumer Contract and Idempotency (idempotency boundary)
- Phase 9 Implementation Plan §2, §5.1, §5.5
- Phase 8 Completion Report §3 (ownership boundary pattern)