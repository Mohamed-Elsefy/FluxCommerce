# Phase 11 — Reservation Expiry Orchestration

## 1. Phase Overview

**Phase:** 11  
**Name:** Reservation Expiry Orchestration  
**Status:** Planned  
**Prerequisites:** Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10

### Primary Goal

Reliably connect `Inventory Reservation` expiration to the `Order Saga`, so reservation expiration produces the correct workflow outcome without:

- Duplicate compensation
- Invalid state transitions
- Double inventory release
- Lost events
- Unsafe retry behavior
- Incorrect handling of out-of-order events
- Recovery inconsistencies after crashes

> Phase 11 does not rebuild the existing Inventory Reservation Expiration mechanism. The goal is to connect it to the Saga using the existing Outbox + RabbitMQ + Idempotent Consumer infrastructure.

---

# 2. Current Architecture Context

Inventory already contains the expiration mechanism:

```text
Inventory
    │
    ▼
Reservation Expiration Worker
    │
    ▼
Reservation Released / Expired
```

Phase 11 adds the orchestration integration:

```text
Reservation
    │
    ▼
Expiration Worker
    │
    ▼
Inventory State Transition
    │
    ▼
ReservationExpired
    │
    ▼
Inventory Outbox
    │
    ▼
RabbitMQ
    │
    ▼
Orders Saga Consumer
    │
    ▼
Evaluate Saga State
    │
    ├── Valid timeout
    │      ↓
    │   Cancel / finalize workflow
    │
    └── Already completed
           ↓
        Ignore safely
```

---

# 3. Architectural Ownership

## Inventory owns

```text
Inventory
├── Reservation
├── Reservation expiration
├── Reservation release
└── ReservationExpired event
```

## Orders owns

```text
Orders
├── Order
├── Order lifecycle
└── Order Saga
```

## Payments owns

```text
Payments
├── Payment
├── Payment Intent
└── Payment lifecycle
```

### Mandatory Boundary

Inventory must never directly modify Order state.

```text
Inventory
    ❌ OrdersDbContext
    ❌ OrderRepository
    ❌ Direct Order mutation
```

Correct communication:

```text
Inventory
    │
    ▼
Integration Event
    │
    ▼
RabbitMQ
    │
    ▼
Orders Saga
```

---

# 4. Scope

## Included

- `ReservationExpired` Integration Event
- Inventory → Outbox integration
- RabbitMQ publication
- Orders consumer registration
- Saga handling of reservation expiry
- Saga state transitions
- Idempotency
- Concurrency safety
- Out-of-order event handling
- Terminal-state handling
- Crash/restart recovery
- Compensation correctness
- Correlation/Causation propagation
- Distributed tracing
- Integration tests
- E2E tests
- Architecture verification
- Security review
- Performance review
- Documentation synchronization

## Explicitly Out of Scope

The following remain outside Phase 11:

- Failure-injection campaign → Phase 12
- Search → Phase 13
- Promotions & Coupons → Phase 14
- Wishlist → Phase 15
- Reviews → Phase 16
- Notifications → Phase 17
- Shipping → Phase 18
- Analytics → Phase 19
- Final production hardening → Phase 20
- Microservices extraction
- Distributed transactions
- Rebuilding the existing Inventory expiration worker

---

# 5. Execution Order

```text
11.1 Baseline & Phase 10 Audit
            │
            ▼
11.2 Reservation Expiry Event Contract
            │
            ▼
11.3 Inventory Expiry → Outbox Integration
            │
            ▼
11.4 RabbitMQ Publication & Consumer Registration
            │
            ▼
11.5 Saga Expiry State Model
            │
            ▼
11.6 ReservationExpired Saga Consumer
            │
            ▼
11.7 Compensation & Terminal-State Rules
            │
            ▼
11.8 Idempotency / Concurrency / Recovery
            │
            ▼
11.9 Observability & Distributed Tracing
            │
            ▼
11.10 Integration & E2E Testing
            │
            ▼
11.11 Architecture / Security / Performance Hardening
            │
            ▼
11.12 Final Verification & Documentation
```

---

# 6. Sub-Phase 11.1 — Baseline & Phase 10 Audit

## Objective

Understand the current implementation before adding new behavior.

## Tasks

Review:

- `InventoryReservation`
- `ExpiredReservationProcessor`
- `ReservationExpirationBackgroundService`
- Existing `ReservationExpired` domain event
- Inventory Outbox mapping
- Inventory integration-event conventions
- RabbitMQ topology
- Consumer registry
- Orders Saga state machine
- Saga persistence/versioning
- Payment compensation states
- Inventory release workflow
- Existing idempotency boundary
- Existing terminal-state rules
- Existing tracing/correlation implementation

## Rules

- Do not rewrite the existing expiration mechanism.
- Do not add new infrastructure when an existing capability already satisfies the requirement.
- Document any prerequisite blocking issue before modifying it.

## Deliverables

- Current-state audit
- Dependency map
- Required changes list
- Architectural decisions requiring clarification

## Exit Criteria

```text
No architectural ambiguity remains.
```

---

# 7. Sub-Phase 11.2 — Reservation Expiry Integration Event Contract

## Objective

Define the public integration contract representing reservation expiration.

Conceptually:

```text
ReservationExpiredIntegrationEvent
```

The contract should contain the metadata required by the existing messaging conventions, such as:

```text
EventId
EventType
EventVersion
OccurredAtUtc
CorrelationId
CausationId

ReservationId
OrderId
SKU
Quantity
ExpiredAtUtc
```

> Exact property names and metadata must follow the existing project conventions. Do not introduce a different metadata model without a justified architectural reason.

## Location

```text
Inventory.Contracts
```

The integration contract must not be placed inside:

```text
Inventory.Domain
Orders.Infrastructure
```

because Inventory owns the event.

## Tasks

- Define event contract.
- Define event type/version.
- Reuse existing messaging metadata conventions.
- Add serialization coverage.
- Verify contract purity.

## Tests

- Serialization test
- Required property test
- Event type/version test
- Architecture boundary test

## Exit Criteria

Inventory can publish the event without exposing internal implementation details.

---

# 8. Sub-Phase 11.3 — Inventory Expiration → Outbox Integration

## Objective

Connect the existing expiration flow to the transactional Outbox.

Target flow:

```text
Expire Reservation
      │
      ├── Reservation State Change
      │
      └── Outbox Message
              │
              ▼
           Commit
```

Then:

```text
Outbox
   │
   ▼
RabbitMQ
```

## Critical Transaction Rule

The following must be persisted in the same Inventory transaction:

```text
Reservation State Change
+
Outbox Insert
```

Incorrect:

```text
Commit Reservation
      ↓
Publish RabbitMQ
```

Correct:

```text
Transaction
├── Update Reservation
└── Insert Outbox Message
      ↓
Commit
      ↓
Existing Outbox Dispatcher
      ↓
RabbitMQ
```

## Tasks

- Connect reservation expiration to the Outbox.
- Reuse existing Outbox infrastructure.
- Ensure atomic persistence.
- Verify retry behavior.
- Ensure duplicate expiration processing does not create duplicate business effects.

## Exit Criteria

The system cannot reach this inconsistent state:

```text
Reservation expired
+
Business state committed
+
Event lost
```

---

# 9. Sub-Phase 11.4 — RabbitMQ Publication & Consumer Registration

## Inventory Side

Ensure:

```text
ReservationExpiredIntegrationEvent
        ↓
Outbox
        ↓
Existing Dispatcher
        ↓
RabbitMQ
```

## Orders Side

Add:

```text
IIntegrationEventHandler<ReservationExpiredIntegrationEvent>
```

and register it explicitly in the existing consumer registry.

## ACK Rule

Correct:

```text
Receive
  ↓
Process
  ↓
Saga transaction commit
  ↓
ACK
```

Failure:

```text
Processing Failure
  ↓
No ACK
  ↓
Retry / DLQ
```

## Tasks

- Add consumer.
- Register consumer explicitly.
- Configure routing using existing RabbitMQ conventions.
- Verify ACK behavior.
- Verify retry/DLQ behavior.

## Exit Criteria

`ReservationExpired` can travel end-to-end from Inventory to Orders.

---

# 10. Sub-Phase 11.5 — Saga Expiry State Model

## Objective

Define the behavior of `ReservationExpired` for every relevant Saga state.

> Do not create a second state machine. Extend the existing Phase 10 Saga state model.

Conceptual matrix:

| Saga State | ReservationExpired |
|---|---|
| `WaitingForInventory` | Defined recovery / unexpected-state handling |
| `WaitingForPayment` | Cancel workflow |
| `ReleasingInventory` | Evaluate current transition safely |
| `Refunding` | Do not blindly restart or duplicate compensation |
| `Completed` | Ignore safely |
| `Failed` | Ignore safely |
| `Cancelled` | Ignore safely |

> The exact states must be matched against the actual Phase 10 implementation before coding.

## Critical Rule

An old event must never move a Saga backward from a terminal state:

```text
Completed
    +
ReservationExpired
    ↓
Cancelled ❌
```

## Deliverables

- State transition matrix
- Valid transitions
- Invalid transitions
- Terminal-state behavior
- Out-of-order behavior definition

## Exit Criteria

Every relevant Saga state has a defined behavior for `ReservationExpired`.

---

# 11. Sub-Phase 11.6 — ReservationExpired Saga Consumer

## Main Flow

```text
WaitingForPayment
       │
       ▼
ReservationExpired
       │
       ▼
Evaluate Saga State
       │
       ▼
Cancel / finalize Order workflow
       │
       ▼
Persist Saga
       │
       ▼
ACK
```

## Processing Pipeline

```text
RabbitMQ Delivery
       ↓
EventId + HandlerName
       ↓
Idempotency Check
       ↓
Load Saga
       ↓
Validate State / Version
       ↓
Apply Transition
       ↓
Persist
       ↓
ACK
```

## Rules

- Use the existing idempotency infrastructure.
- Use the existing Saga persistence.
- Do not directly modify Inventory.
- Do not create a second Saga.
- Do not duplicate compensation logic already owned by Inventory.

## Exit Criteria

Reservation expiration changes Order/Saga state correctly according to the defined state machine.

---

# 12. Sub-Phase 11.7 — Compensation & Terminal-State Rules

## Core Principle

Inventory expiration already performs Inventory-side compensation.

Therefore:

```text
ReservationExpired
        ↓
Inventory has already released reservation
        ↓
Orders Saga handles Order/Saga consequence
```

Do not do:

```text
ReservationExpired
        ↓
Saga
        ↓
Release Inventory again ❌
```

## Main Scenario

```text
Reservation
     ↓
Expires
     ↓
Inventory releases reservation
     ↓
ReservationExpired
     ↓
Saga
     ↓
Cancel Order
```

## Payment Success Race

Example:

```text
PaymentSucceeded
ReservationExpired
```

or:

```text
ReservationExpired
PaymentSucceeded
```

The final behavior must be determined by the persisted Saga state and explicit transition rules, not simply by message arrival order.

## Exit Criteria

- No double release.
- No duplicate refund.
- No invalid order transition.
- No resurrection of a terminal workflow.

---

# 13. Sub-Phase 11.8 — Idempotency / Concurrency / Recovery

## Duplicate Event

Input:

```text
ReservationExpired(E1)
ReservationExpired(E1)
ReservationExpired(E1)
```

Expected:

```text
One valid Saga transition
One business outcome
```

## Concurrent Delivery

```text
Consumer A ─┐
            ├── ReservationExpired
Consumer B ─┘
```

Expected:

```text
One successful transition
Remaining processing safely rejected/ignored
```

## Out-of-Order Delivery

Test:

```text
PaymentSucceeded
ReservationExpired
```

and:

```text
ReservationExpired
PaymentSucceeded
```

Expected behavior must follow the Saga state machine.

## Crash Recovery

Scenario:

```text
ReservationExpired
      ↓
Saga transaction
      ↓
Process crashes
```

After restart:

```text
RabbitMQ Redelivery
      ↓
Idempotency / persisted Saga state
      ↓
Safe recovery
```

## Exit Criteria

The workflow remains correct under:

- Duplicate delivery
- Concurrent delivery
- Out-of-order events
- Consumer crash
- Application restart
- Retry

---

# 14. Sub-Phase 11.9 — Observability & Distributed Tracing

## Required Flow

```text
Reservation Expiration
        ↓
Inventory
        ↓
Outbox
        ↓
RabbitMQ
        ↓
Orders Consumer
        ↓
Saga
        ↓
Order Cancellation
```

## Metadata

Preserve the existing:

```text
CorrelationId
CausationId
EventId
OrderId
ReservationId
SagaId
TraceId
SpanId
```

## Tasks

- Verify W3C trace propagation.
- Verify correlation propagation.
- Add structured logs where required.
- Ensure logs identify Order/Saga/Reservation.
- Verify failure/retry visibility.

## Exit Criteria

Reservation expiration can be traced end-to-end across Inventory → RabbitMQ → Orders Saga.

---

# 15. Sub-Phase 11.10 — Integration & E2E Testing

## Unit Tests

### Inventory

- Expiration produces the correct domain event.
- Event is produced only for actual expiration.
- Already released reservation does not emit a duplicate event.
- Already committed reservation does not expire.
- Repeated expiration processing is safe.

### Orders

- Valid `ReservationExpired` transition.
- Invalid transition.
- Terminal state.
- Duplicate event.
- Out-of-order event.
- Concurrent transition.

---

## Integration Tests

Use real infrastructure where applicable:

```text
SQL Server
RabbitMQ
```

Test:

```text
Inventory
   ↓
Reservation Expiration
   ↓
Outbox
   ↓
RabbitMQ
   ↓
Orders Consumer
   ↓
Saga
   ↓
Order State
```

---

## Mandatory E2E Scenarios

### Scenario 1 — Normal Expiration

```text
Reservation expires
→ Event published
→ Saga receives
→ Order cancelled
```

### Scenario 2 — Duplicate Event

```text
Event delivered twice
→ One business effect
```

### Scenario 3 — Concurrent Event

```text
Concurrent consumers
→ One valid Saga transition
```

### Scenario 4 — Expiration After Payment Success

```text
PaymentSucceeded
→ Order completed
→ ReservationExpired
→ Safe ignore
```

### Scenario 5 — Expiration Before Payment Success

```text
ReservationExpired
→ Order cancellation
→ Later Payment event
→ No invalid resurrection
```

### Scenario 6 — Consumer Failure

```text
Consumer failure
→ Retry
→ Event eventually processed
```

### Scenario 7 — Retry Exhaustion

```text
Permanent failure
→ DLQ
```

### Scenario 8 — Crash / Restart

```text
Saga processing
→ Process restart
→ Redelivery / recovery
→ Correct final state
```

### Scenario 9 — Correlation / Causation

Verify the complete metadata chain.

### Scenario 10 — No Double Inventory Release

```text
Expired reservation
→ Released exactly once
```

---

# 16. Sub-Phase 11.11 — Architecture / Security / Performance Hardening

## Architecture Verification

Verify:

```text
Inventory.Domain
    ❌ Orders

Inventory.Infrastructure
    ❌ Orders.Infrastructure

Orders.Domain
    ❌ Inventory implementation

Orders.Infrastructure
    ✅ Inventory.Contracts
```

Verify:

- No cross-module DbContext access.
- No cross-module EF relationships.
- No direct cross-module state mutation.
- Contracts remain pure.
- Saga remains Orders-owned.
- Reservation remains Inventory-owned.

---

## Security

Review:

- Event payload contains only required information.
- No secrets in messages.
- No sensitive information in logs.
- Correlation metadata does not expose sensitive data.
- Consumer cannot bypass business authorization boundaries.

---

## Performance

Review:

- Expiration query efficiency.
- Batch processing.
- Outbox insertion cost.
- Consumer concurrency.
- Saga lookup.
- Saga update.
- Relevant indexes.
- Duplicate-event fast path.

Avoid:

```text
N expired reservations
→ N unnecessary database round trips
```

where the existing design can safely batch or optimize the operation.

---

# 17. Sub-Phase 11.12 — Final Verification & Documentation

## Build

```bash
dotnet build FluxCommerce.slnx
```

Expected:

```text
0 errors
0 warnings
```

## Tests

Run:

```text
Unit Tests
Architecture Tests
Integration Tests
E2E Tests
```

All must pass.

## Documentation

Synchronize:

```text
PROJECT_CONTEXT.md
ARCHITECTURE.md
PHASES.md
```

If a new architectural decision is introduced:

```text
docs/adr/
```

Create:

```text
PHASE_11_COMPLETION_REPORT.md
```

The completion report must document:

- Implemented behavior
- Architectural decisions
- State transition rules
- Tests
- Failure/recovery behavior
- Known limitations
- Documentation updates

---

# 18. Definition of Done

Phase 11 is complete only when all of the following are true:

- [ ] Existing Inventory expiration mechanism works without regression.
- [ ] Actual reservation expiration produces `ReservationExpired` integration event.
- [ ] Contract lives in `Inventory.Contracts`.
- [ ] Reservation state + Outbox message are persisted transactionally.
- [ ] Event is published through the existing Outbox Dispatcher.
- [ ] Event reaches RabbitMQ.
- [ ] Orders registers the consumer explicitly.
- [ ] Saga handles expiration using the existing state machine.
- [ ] Duplicate events produce no duplicate business effect.
- [ ] Concurrent events are safe.
- [ ] Out-of-order events are safe.
- [ ] Terminal Saga states are protected.
- [ ] Inventory is not released twice.
- [ ] Refund/compensation is not duplicated.
- [ ] Consumer restart/recovery is safe.
- [ ] Correlation/Causation are preserved.
- [ ] Distributed tracing works across the asynchronous boundary.
- [ ] Integration tests pass.
- [ ] E2E tests pass.
- [ ] Architecture tests pass.
- [ ] No cross-module database dependencies are introduced.
- [ ] No distributed transaction is introduced.
- [ ] Documentation is synchronized.
- [ ] `PHASE_11_COMPLETION_REPORT.md` is created.
- [ ] Project is ready for Phase 12 — Failure Testing.

---

# 19. Target Architecture After Phase 11

```text
                    Reservation
                         │
                         ▼
               Expiration Worker
                         │
                         ▼
                  Inventory State
                         │
                         ├──────────────┐
                         │              │
                         ▼              ▼
                  Outbox Message   Reservation State
                         │
                         ▼
                     RabbitMQ
                         │
                         ▼
                  Orders Consumer
                         │
                         ▼
                    Order Saga
                         │
              ┌──────────┴──────────┐
              ▼                     ▼
       Valid Timeout          Already Terminal
              │                     │
              ▼                     ▼
       Cancel / Finalize           Ignore
```

## Final Architectural Outcome

Before Phase 11:

```text
Inventory Reservation Expiration
            │
            ▼
     Inventory released

            ╳

       Orders Saga
```

After Phase 11:

```text
Inventory Reservation Expiration
            │
            ▼
       ReservationExpired
            │
            ▼
          Outbox
            │
            ▼
        RabbitMQ
            │
            ▼
       Orders Saga
            │
            ▼
     State-aware handling
            │
      ┌─────┴─────┐
      ▼           ▼
   Timeout      Terminal
      │           │
      ▼           ▼
Cancel/Finalize  Ignore
```

This closes the reservation-timeout path in the Order Processing Saga and leaves the system ready for **Phase 12 — Failure Testing**.
