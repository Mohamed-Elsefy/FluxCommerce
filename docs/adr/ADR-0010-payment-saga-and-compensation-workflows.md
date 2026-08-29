# ADR-0010: Payment Saga and Compensation Workflows

## Status

Accepted

## Context

Phase 10 introduces the Order/Payment/Saga with compensation workflows to achieve distributed order
payment consistency without relying on two-phase commit (2PC) or distributed transactions across modules.
The system must guarantee that an order, its reserved inventory, and its payment state reach a consistent
final state even when external systems (Stripe) and asynchronous message delivery (at-least-once) introduce
failures, retries, and duplicate deliveries.

Key constraints driving this decision:

- **Module boundary isolation**: No module may directly access another module's DbContext, repositories,
  or internal entities. Orders must not touch PaymentsDbContext or InventoryDbContext; Inventory must
  not touch PaymentsDbContext or OrdersDbContext; Payments must not touch OrdersDbContext or InventoryDbContext.
- **No distributed transactions**: The system is built as a Modular Monolith on a single SQL Server
  instance. Distributed transactions across module boundaries are explicitly forbidden (see ARCHITECTURE.md
  §11, §15).
- **At-least-once messaging**: RabbitMQ delivers messages at-least-once. Consumers must be idempotent.
- **Untrusted external integrations**: Stripe webhooks may be duplicated, delayed, or fail; the system
  must not assume exactly-once delivery from any external service.
- **Business invariants**: Overselling must be prevented, orders must not be confirmed without valid
  payment, and refunds must be properly tracked and applied.

The project has already established the foundational patterns in earlier phases:
- Phase 6: Outbox Pattern for reliable integration event publication (ARCHITECTURE.md §12)
- Phase 7: Idempotent consumers with `IIdempotentProcessor` and `ProcessedMessage` persistence
  (ARCHITECTURE.md §14, ADR-008)
- Phase 8: Saga Orchestration for Order/Inventory workflow (ADR-009, PHASE_8_COMPLETION_REPORT)
- Phase 9: Payments bounded context with Stripe integration, confined to `Payments.Infrastructure`
  only (ADR-010, PHASE_9_COMPLETION_REPORT)

## Decision

**Orchestrated Saga pattern with Outbox and Idempotent consumers**, extending the Phase 8
Order/Inventory Saga pattern.

The Saga Orchestrator (Orders-owned, persisted in Orders database) coordinates the workflow through
explicit Integration Event contracts over RabbitMQ. No module directly modifies another module's state.

### Saga flow (Order → Inventory → Payment → Confirmation / Cancellation)

1. **Order created** → Saga starts at `WaitingForInventory`
2. **Inventory reserved** → Saga transitions to `WaitingForPayment`
3. **Payment succeeded** → Order confirmed (`Confirmed`), Saga completed (`Completed`)
4. **Payment failed** → Inventory reservation released, Order cancelled (`Cancelled`)
5. **Post-payment compensation** → Refund initiated (Stripe), Inventory released, Order cancelled

### Integration events (contracts in `Payments.Contracts`, consumed by `Orders.Infrastructure`)

| Event | Source | Consumed by | Purpose |
|-------|--------|-------------|---------|
| `inventory.reservation_requested` | Orders → Inventory | Inventory.Reserve handler | Request stock reservation |
| `inventory.reserved` | Inventory → Orders | Orders.Saga handler | Reservation successful |
| `inventory.release_requested` | Orders → Inventory | Inventory.Release handler | Release reservation |
| `payments.payment_requested` | Orders → Payments | Payments.CreateIntent handler | Create PaymentIntent |
| `payments.payment_succeeded` | Payments → Orders | Orders.Saga handler | Payment succeeded |
| `payments.payment_failed` | Payments → Orders | Orders.Saga handler | Payment failed |
| `payments.refund_requested` | Orders → Payments | Payments.Refund handler | Initiate Stripe refund |
| `payment.refunded` | Payments → Orders | Orders.Saga handler | Refund completed |
| `orders.order_cancelled` | Orders → (external) | Order cancellation consumer | Order cancelled notification |

### Idempotency & concurrency controls

- **Saga state Version token**: Each Saga row in the Orders database has a `Version` column (bigint,
  `IsConcurrencyToken`) for EF Core optimistic concurrency protection. Duplicate or conflicting
  transition results apply at most once.
- **Consumer idempotency**: Every consumer uses `IIdempotentProcessor.TryProcessAsync` with
  `ProcessedMessage.Create(EventId, HandlerName)`. The composite `(Key, Handler)` primary key in
  each module's `ProcessedMessages` table is the final concurrency protection.
- **No distributed transactions**: Workflow coordination flows only through explicit Integration
  Event contracts over RabbitMQ. Each module commits its own transaction (business state + Outbox
  record + ProcessedMessage) independently.
- **Outbox Pattern**: Each module's Outbox ensures reliable event publication atomically with the
  business state change. Outbox Messages contain `CorrelationId`, `CausationId`, `OccurredAtUtc`,
  `RetryCount`, and `Error information`.

### W3C Trace Context propagation

- CorrelationId and CausationId are carried through the `IntegrationEventEnvelope` from producer to
  consumer, preserving the distributed trace across module boundaries.
- Structured logs include `OrderId/PaymentId/Stripe* /CorrelationId/CausationId/EventType/Version`
  for end-to-end observability (see ARCHITECTURE.md §19).

## Alternatives Considered

### Choreography Saga

In a choreography-based approach, each module reacts to events independently without a central
orchestrator. For example:

- OrderCreated → InventoryReservationRequested (Orders publishes, Inventory consumes)
- InventoryReserved → PaymentRequested (Inventory publishes, Payments consumes)
- PaymentSucceeded → OrderConfirmed (Payments publishes, Orders consumes)
- PaymentFailed → ReleaseInventory → CancelOrder (Payments publishes, Inventory & Orders consume)

**Pros:** No central coordinator; each module has full ownership of its reactions; potentially
simpler to understand per-module logic.

**Cons:** Global state consistency becomes harder to reason about; no single point of coordination
for compensation; compensating actions may race; harder to enforce global invariants (e.g., "an
order can only be in one of {Pending, Confirmed, Cancelled}"); the absence of an orchestrator
makes it difficult to guarantee that all compensation steps fire in the correct order, especially
when messages are re-delivered at-least-once. This pattern was rejected for Phase 10 because the
complexity of coordinating 3+ modules without orchestration outweighs the simplicity of per-module
reaction logic.

### Two-Phase Commit (2PC)

A traditional distributed transaction protocol spanning the Orders, Inventory, and Payments modules.

**Pros:** Strong atomicity guarantees; all participants commit or roll back together.

**Cons:** Requires a transaction coordinator, locks resources for the duration of the transaction,
does not scale across module boundaries, introduces tight coupling between modules, violates the
module isolation rules established in earlier phases (ADR-007, ADR-009, ADR-010), and is explicitly
forbidden by the architecture (ARCHITECTURE.md §11: "The system must avoid distributed database
transactions across modules"). 2PC was rejected because it contradicts the modular monolith
philosophy and the explicit constraint against cross-module DbContext access.

## Consequences

### Positive

- **Strict module isolation**: Each module owns its business state and DbContext. No module directly
  accesses another module's internal data. Cross-module communication uses only approved Integration
  Event contracts via `Payments.Contracts` and `Inventory.Contracts`.
- **Resilience to failures**: At-least-once delivery + idempotent consumers + Saga compensation
  guarantees that the system reaches a consistent final state even with retries, crashes, and
  duplicate deliveries.
- **No distributed transactions**: The system avoids the scalability and coupling problems of 2PC
  while still maintaining business correctness through compensation.
- **Observability**: W3C TraceContext correlation across all hops (API → Outbox → RabbitMQ →
  Consumer) provides end-to-end visibility into workflow state.
- **Independent module evolution**: Each module (Orders, Inventory, Payments) can be implemented,
  tested, and evolved independently as long as it respects the integration event contracts.
- **Test coverage**: 22 Phase 10–specific unit/E2E + integration + architecture tests verify all
  workflows, compensation paths, idempotency races, concurrency safety, and recovery scenarios.

### Negative

- **Operational complexity**: The Saga Orchestrator adds a coordination layer that must be monitored
  and maintained. Transitions must be guarded by optimistic concurrency tokens; failure modes must
  be explicitly tested.
- **Eventual consistency**: The system does not provide strong immediate consistency across modules.
  After a payment succeeds, there is a brief window before the Order is confirmed and the Saga
  is Complete. Business logic must be written to handle this window (e.g., displaying "Pending
  confirmation" UI state).
- **More code than choreography (but less than 2PC)**: The orchestrated Saga requires an
  orchestrator type, state machine transitions, and compensation logic, but fewer inter-module
  coupling issues than 2PC and more predictable compensation ordering than choreography.
- **Payments.Contracts dependency**: Orders.Infrastructure gains a narrow, approved dependency on
  `FluxCommerce.Modules.Payments.Contracts` (the `Integration` and `IntegrationEvents` namespaces
  only). The architecture tests (`Orders_ShouldNot_ReferenceOtherModules`, `Orders_ShouldNot_ReferenceInventory`)
  are updated to encode this carve-out, keeping the boundary enforceable.

## References

- ADR-007: Integration Event Contract Placement (contract placement strategy)
- ADR-008: Consumer Contract and Idempotency (idempotency boundary)
- ADR-009: Saga Orchestration & Contract Placement (established Saga pattern)
- ARCHITECTURE.md §6: Module Dependency Rules
- ARCHITECTURE.md §15: Saga Architecture
- ARCHITECTURE.md §16: Idempotency
- PHASE_8_COMPLETION_REPORT.md: Order/Inventory Saga implementation verification
- PHASE_9_COMPLETION_REPORT.md: Stripe Payments implementation verification
- PHASE_10_COMPLETION_REPORT.md: This phase's completion report
- BuildingBlocks.Idempotency: `IIdempotentProcessor`, `ProcessedMessage`, `IIdempotencyStore`
- BuildingBlocks.Messaging: `IIntegrationEventHandler`, `IntegrationEventEnvelope`