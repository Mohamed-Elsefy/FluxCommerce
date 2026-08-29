# Phase 10 — Payment Saga + Compensation
## Implementation Plan

**Prerequisites:** Phase 8 — Order/Inventory Saga + Phase 9 — Stripe Payments  
**Status:** Planned  
**Primary Goal:** Integrate Payment into the Order/Inventory Saga and implement reliable compensation and recovery for payment failures and post-payment failures.

---

# 1. Phase Objective

At the end of Phase 10, the primary workflow should be:

```text
Order Created
      │
      ▼
Order Saga
      │
      ▼
Reserve Inventory
      │
      ▼
Inventory Reserved
      │
      ▼
Create Payment
      │
      ▼
Customer Payment
      │
      ▼
Stripe Webhook
      │
      ├───────────────┐
      │               │
      ▼               ▼
PaymentSucceeded   PaymentFailed
      │               │
      ▼               ▼
Confirm Order    Release Inventory
      │               │
      ▼               ▼
Saga Complete   Cancel Order
                      │
                      ▼
                 Saga Complete
```

The implementation must preserve:

- At-Least-Once Delivery.
- Idempotent processing.
- Saga state persistence and recovery.
- Module ownership boundaries.
- No cross-module `DbContext` access.
- No distributed transactions.
- Outbox + RabbitMQ.
- Correlation/Causation propagation.
- Compensation as a business operation, not a database rollback.

---

# 2. Phase Boundaries

## Included

- Payment outcomes inside the Order Saga.
- `PaymentSucceeded` handling.
- `PaymentFailed` handling.
- Payment-related Saga states and transitions.
- Inventory compensation after Payment failure.
- Order cancellation after compensation.
- Compensation idempotency.
- Payment-related Saga recovery.
- Handling duplicate/concurrent Payment events.
- Handling out-of-order events.
- Recovery after crash/restart.
- Payment compensation/refund workflow when required.
- End-to-End Order → Inventory → Payment workflow.
- Observability and distributed tracing across the workflow.
- Architecture, Unit, Integration, and E2E tests.
- Documentation synchronization.

## Explicitly Out of Scope

Do not introduce:

- Reservation Expiration orchestration → **Phase 11**
- Failure-injection campaign → **Phase 12**
- Search → Phase 13
- Promotions & Coupons → Phase 14
- Wishlist → Phase 15
- Reviews → Phase 16
- Notifications → Phase 17
- Shipping → Phase 18
- Analytics → Phase 19
- Final Production Hardening → Phase 20
- Microservices or distributed transactions

---

# 3. Execution Order

```text
10.1 Baseline & Saga/Payment Architecture Audit
             │
             ▼
10.2 Payment Saga State Model
             │
             ▼
10.3 Payment Initiation Flow Integration
             │
             ▼
10.4 PaymentSucceeded Flow
             │
             ▼
10.5 PaymentFailed Flow
             │
             ▼
10.6 Inventory Compensation
             │
             ▼
10.7 Order Cancellation & Finalization
             │
             ▼
10.8 Payment Compensation / Refund
             │
             ▼
10.9 Idempotency, Concurrency & Recovery
             │
             ▼
10.10 Failure & Recovery Scenarios
             │
             ▼
10.11 Observability & Distributed Tracing
             │
             ▼
10.12 Testing & Integration Verification
             │
             ▼
10.13 Architecture/Security/Performance Hardening
             │
             ▼
10.14 Documentation & Final Completion
```

---

# 10.1 — Baseline & Saga/Payment Architecture Audit

## Objective

Review the final state after Phase 8 and Phase 9 before modifying the Saga.

## Review

Inspect:

- Order Saga state model.
- Existing Saga consumers.
- Saga persistence.
- Saga commands/events.
- Inventory reservation flow.
- Inventory release flow.
- Payment module.
- Payment integration events.
- Payment Outbox.
- RabbitMQ topology.
- Idempotent consumer infrastructure.
- Correlation/Causation implementation.
- Existing architecture tests.

Target evolution:

```text
Phase 8

Order
  ↕
Inventory
  ↕
Saga
```

```text
Phase 9

Payment
  ↓
PaymentSucceeded / PaymentFailed
  ↓
Outbox
  ↓
RabbitMQ
```

Target for Phase 10:

```text
Order
  ↕
Saga
  ↕
Inventory
  ↕
Payment
```

## Exit Criteria

- Current Saga flow is understood.
- Payment contracts are verified.
- Existing compensation flow is understood.
- No duplicate infrastructure is introduced.
- Any required ADR is documented before implementation.

---

# 10.2 — Payment Saga State Model

## Objective

Extend the Saga state machine to include Payment.

Example success path:

```text
OrderCreated
    ↓
WaitingForInventory
    ↓
InventoryReserved
    ↓
WaitingForPayment
    ↓
PaymentSucceeded
    ↓
Completed
```

Failure path:

```text
WaitingForPayment
    ↓
PaymentFailed
    ↓
ReleasingInventory
    ↓
InventoryReleased
    ↓
OrderCancelled
    ↓
Completed
```

The exact states must be aligned with the existing Phase 8 model rather than introducing a separate state model.

## Define

- Valid transitions.
- Invalid transitions.
- Terminal states.
- Duplicate event behavior.
- Out-of-order event behavior.
- Compensation states.
- Recovery states.

## Exit Criteria

Every event must have a deterministic outcome:

```text
Current State + Event
        ↓
Valid Transition / Ignore / Reject
```

No implicit transitions.

---

# 10.3 — Payment Initiation Flow Integration

## Objective

Connect successful Inventory reservation to Payment creation.

```text
InventoryReserved
       ↓
Order Saga
       ↓
CreatePayment
       ↓
RabbitMQ
       ↓
Payments Module
       ↓
Payment Intent
```

Maintain the boundary:

```text
Orders
   X → Payments.Infrastructure
```

Communication must use the contracts/messages established in Phase 9.

The Saga must not call Stripe directly.

```text
Saga
  ↓
Payment Contract
  ↓
Payments
  ↓
Stripe
```

## Exit Criteria

- Payment initiation is triggered exactly once logically.
- Duplicate Saga events do not create duplicate Payment business effects.
- Payment remains owned by the Payments module.

---

# 10.4 — PaymentSucceeded Flow

## Objective

Implement the happy path after successful payment.

```text
PaymentSucceeded
       ↓
RabbitMQ
       ↓
Order Saga Consumer
       ↓
Validate Saga State
       ↓
Confirm Order
       ↓
Saga Completed
```

## Requirements

- Consume `PaymentSucceeded`.
- Validate correlation with the expected Order/Saga.
- Validate the current Saga state.
- Apply the valid Order transition.
- Persist Saga + Order changes atomically within the Orders boundary.
- Handle duplicate delivery safely.
- Publish resulting integration events through the existing Outbox infrastructure where required.

## Exit Criteria

```text
Order Created
→ Inventory Reserved
→ Payment Succeeded
→ Order Confirmed
→ Saga Completed
```

The complete success workflow must execute end-to-end without manual intervention.

---

# 10.5 — PaymentFailed Flow

## Objective

Define and implement the business workflow when Payment fails.

Expected flow:

```text
PaymentFailed
      ↓
Order Saga
      ↓
Determine Compensation
      ↓
Release Inventory
      ↓
InventoryReleased
      ↓
Cancel Order
```

The Payment module must not directly update Order or Inventory state.

```text
PaymentFailed
    ↓
Order Saga
    ↓
Compensation decision
```

## Exit Criteria

- Payment failure moves the Saga into the compensation flow.
- Inventory release is requested.
- Duplicate `PaymentFailed` is safe.
- Invalid/out-of-order `PaymentFailed` does not corrupt Saga state.

---

# 10.6 — Inventory Compensation

## Objective

Restore Inventory correctly after Payment failure.

```text
Payment Failed
      ↓
ReleaseInventory
      ↓
RabbitMQ
      ↓
Inventory
      ↓
Release Reservation
      ↓
InventoryReleased
      ↓
RabbitMQ
      ↓
Saga
```

Reuse the Inventory release capability established in Phase 8.

Do not redesign Inventory.

## Requirements

- Explicit release contract.
- Idempotent release.
- Duplicate release is safe.
- Concurrent release is safe.
- Correct correlation/causation propagation.
- Saga waits for the required compensation result.

## Exit Criteria

```text
Reserved = 1
Payment Failed
      ↓
Release
      ↓
Reserved = 0
```

No double release may occur.

---

# 10.7 — Order Cancellation & Saga Finalization

## Objective

Finalize the Saga after successful compensation.

```text
InventoryReleased
       ↓
Saga
       ↓
Cancel Order
       ↓
Saga Completed
```

Define explicitly:

- Whether cancellation occurs only after successful release.
- What happens while release is retrying.
- What happens if release permanently fails.
- Whether the Saga enters a recovery state.
- Whether manual intervention is required.

These are business workflow decisions, not merely implementation details.

## Exit Criteria

The Saga must not reach `Completed` before the required compensation conditions are satisfied.

---

# 10.8 — Payment Compensation / Refund

## Objective

Handle cases where Payment succeeded but a later workflow step cannot complete.

Example:

```text
PaymentSucceeded
       ↓
Downstream Failure
       ↓
Compensation
       ↓
Refund Payment
       ↓
Release Inventory
       ↓
Cancel Order
```

A Refund must not be implemented as only:

```text
Payment.Status = Refunded
```

Instead:

```text
Saga
   ↓
RefundPayment
   ↓
Payments
   ↓
Stripe
   ↓
Refund Result
```

## Requirements

- Refund idempotency.
- Duplicate refund command handling.
- Stripe timeout handling.
- Stripe transient failure handling.
- Stripe permanent failure handling.
- Retry policy.
- Already-refunded handling.
- Crash before/after Stripe call.
- Local state consistency.

If the Phase 9 implementation does not support sufficient refund semantics, document the dependency/decision in an ADR before implementing orchestration.

## Exit Criteria

No more than one Refund business effect may be created for the same Payment/Saga compensation operation.

---

# 10.9 — Idempotency, Concurrency & Recovery

This is a critical hardening stage.

## Duplicate PaymentSucceeded

```text
PaymentSucceeded
PaymentSucceeded
PaymentSucceeded
       ↓
One Order Confirmation
```

## Duplicate PaymentFailed

```text
PaymentFailed
PaymentFailed
       ↓
One Compensation Decision
```

## Concurrent Events

Example:

```text
PaymentSucceeded
PaymentFailed
```

arriving concurrently must not produce an invalid state.

## Crash Recovery

Example:

```text
PaymentFailed
     ↓
Saga decides ReleaseInventory
     ↓
CRASH
```

After restart:

```text
Load Saga
     ↓
Detect incomplete compensation
     ↓
Resume safely
```

The Saga must not restart from the beginning.

## Required Guarantees

- Optimistic concurrency where required.
- Unique constraints where required.
- Idempotent commands.
- Idempotent consumers.
- Recoverable Saga state.
- Safe restart.
- No duplicate compensation side effects.

---

# 10.10 — Failure & Recovery Scenarios

Test at minimum:

## Scenario A — Payment Failure

```text
Order
→ Inventory Reserved
→ Payment Failed
→ Release Inventory
→ Cancel Order
```

## Scenario B — Duplicate Payment Failure

```text
PaymentFailed
PaymentFailed
→ One Compensation
```

## Scenario C — Payment Success

```text
PaymentSucceeded
→ Confirm Order
→ Completed
```

## Scenario D — Duplicate Payment Success

```text
PaymentSucceeded
PaymentSucceeded
→ One Confirmation
```

## Scenario E — Crash During Compensation

```text
PaymentFailed
→ ReleaseInventory
→ Crash
→ Restart
→ Recover
```

## Scenario F — Inventory Release Failure

```text
PaymentFailed
→ ReleaseInventory
→ Failure
→ Retry
→ Eventually Released
→ Cancel Order
```

## Scenario G — Payment Success + Downstream Failure

```text
PaymentSucceeded
→ Order confirmation failure
→ Compensation
→ Refund
→ Release Inventory
→ Cancel Order
```

## Scenario H — Concurrent Saga Events

Test event races and optimistic concurrency.

---

# 10.11 — Observability & Distributed Tracing

The complete workflow must be traceable:

```text
Checkout
  ↓
OrderCreated
  ↓
Saga
  ↓
ReserveInventory
  ↓
InventoryReserved
  ↓
Saga
  ↓
CreatePayment
  ↓
Stripe
  ↓
PaymentSucceeded / PaymentFailed
  ↓
Saga
  ↓
Compensation
```

At minimum, trace/log the relevant:

```text
OrderId
SagaId
PaymentId
ReservationId
EventId
MessageId
CorrelationId
CausationId
TraceId
SpanId
SagaState
PreviousState
NewState
```

Logs must make it possible to understand why the Saga transitioned between states.

---

# 10.12 — Testing & Integration Verification

Testing must cover three levels.

## Unit Tests

Test:

- Saga state machine.
- Payment transitions.
- Compensation decisions.
- Order transitions.
- Duplicate handling.
- Invalid transitions.
- Refund decisions.
- Recovery logic.

## Integration Tests

Use the real infrastructure required by the project:

```text
SQL Server
RabbitMQ
Redis where applicable
```

Test:

- Payment consumers.
- Saga persistence.
- Outbox.
- Payment events.
- Inventory release.
- Order transitions.
- Idempotency.

## End-to-End Tests

Success workflow:

```text
Order
 ↓
Saga
 ↓
Inventory
 ↓
Saga
 ↓
Payment
 ↓
Stripe/Test Gateway
 ↓
Payment Event
 ↓
Saga
 ↓
Order
```

Failure workflow:

```text
Order
 ↓
Inventory Reserved
 ↓
Payment Failed
 ↓
Saga
 ↓
Release Inventory
 ↓
Inventory Released
 ↓
Saga
 ↓
Cancel Order
```

---

# 10.13 — Architecture / Security / Performance Hardening

## Architecture

Verify that no forbidden dependency appears:

```text
Orders → Payments.Infrastructure
Orders → Inventory.Infrastructure
Payments → Orders.Infrastructure
Saga → DbContext outside Orders
```

## Security

Review:

- Payment data is not leaked into logs.
- Stripe secrets are never logged.
- Sensitive payment information is never logged.
- Existing Phase 9 webhook security remains intact.
- Refund operations have appropriate authorization and ownership controls.

## Performance

Review:

- RabbitMQ consumer concurrency.
- Saga database queries.
- Unnecessary retries.
- Duplicate message processing.
- N+1 queries.
- Excessive polling.
- Unnecessary Stripe requests.

---

# 10.14 — Documentation & Final Completion

Update:

```text
PROJECT_CONTEXT.md
ARCHITECTURE.md
completed/INDEX.md
ADR(s)
completed/Phase 10 Completion Report
```

Document:

### Main Workflow

```text
Order
→ Inventory
→ Payment
→ Confirmation
```

### Payment Failure Workflow

```text
Payment Failure
→ Inventory Release
→ Order Cancellation
```

### Post-Payment Compensation

```text
Post-Payment Failure
→ Refund
→ Inventory Compensation
→ Order Cancellation
```

### Reliability Guarantees

```text
At-Least-Once
+
Idempotency
+
Concurrency Safety
+
Recovery
+
Compensation
```

---

# 4. Definition of Done

Phase 10 is complete only when:

1. Payment outcomes are integrated into the Order Saga.
2. `PaymentSucceeded` is handled idempotently.
3. `PaymentFailed` is handled idempotently.
4. Payment success leads to Order confirmation according to valid state transitions.
5. Payment failure triggers Inventory compensation.
6. Inventory release does not execute twice for duplicate messages.
7. Order cancellation occurs through the Orders boundary.
8. Saga state is persistent and recoverable.
9. Duplicate events do not create duplicate business effects.
10. Concurrent events cannot corrupt Saga state.
11. Out-of-order events do not corrupt the workflow.
12. Consumer crash/redelivery is safe.
13. Compensation is retryable.
14. Refund, when required, is idempotent.
15. Stripe is not treated as the source of truth for Order state.
16. No cross-module `DbContext` access exists.
17. No distributed transaction exists.
18. Outbox is used for reliable event publication.
19. Correlation/Causation is preserved across the workflow.
20. Distributed tracing works across RabbitMQ boundaries.
21. SQL Server integration tests pass.
22. RabbitMQ integration tests pass.
23. End-to-End success workflow passes.
24. End-to-End Payment failure compensation workflow passes.
25. Crash/recovery scenarios pass.
26. Architecture tests pass.
27. Unit tests pass.
28. Integration tests pass.
29. No Phase 11 functionality is introduced.
30. Documentation is synchronized with the implementation.

---

# 5. Final Target Architecture

```text
                         ┌──────────────┐
                         │    Order     │
                         └──────┬───────┘
                                │
                         OrderCreated
                                │
                                ▼
                       ┌────────────────┐
                       │   Order Saga   │
                       └───────┬────────┘
                               │
                     ReserveInventory
                               │
                               ▼
                        ┌────────────┐
                        │ Inventory  │
                        └─────┬──────┘
                              │
                     InventoryReserved
                              │
                              ▼
                       ┌──────────────┐
                       │ Order Saga   │
                       └──────┬───────┘
                              │
                        CreatePayment
                              │
                              ▼
                        ┌────────────┐
                        │  Payments  │
                        └─────┬──────┘
                              │
                              ▼
                           Stripe
                              │
                     ┌────────┴────────┐
                     │                 │
                     ▼                 ▼
             PaymentSucceeded    PaymentFailed
                     │                 │
                     ▼                 ▼
              Confirm Order      Compensation
                     │                 │
                     ▼          Release Inventory
                  Complete             │
                                       ▼
                               InventoryReleased
                                       │
                                       ▼
                                 Cancel Order
                                       │
                                       ▼
                                    Complete
```

---

# 6. Phase 10 Success Criteria

The final objective is not simply:

> "Payment works."

The objective is:

> **Order + Inventory + Payment form one reliable business workflow that reaches a correct business state even under failure, duplication, concurrency, message redelivery, consumer crashes, and restarts.**

The phase should therefore demonstrate:

```text
Distributed Systems
        +
Saga Pattern
        +
Event-Driven Architecture
        +
Idempotency
        +
Compensation
        +
External System Reliability
        +
Recovery
```
