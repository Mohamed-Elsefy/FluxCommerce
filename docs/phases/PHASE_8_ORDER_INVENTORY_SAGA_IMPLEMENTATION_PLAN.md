# Phase 8 — Order / Inventory Saga
## Implementation Plan

**Phase:** 8  
**Name:** Order / Inventory Saga  
**Status:** Planned  
**Prerequisites:** Phase 6 — Outbox + RabbitMQ, Phase 7 — Idempotent Consumers  
**Next Major Phase:** Phase 9 — Stripe Payments

---

# 1. Phase Objective

Phase 8 introduces the first reliable cross-module business workflow in FluxCommerce.

The objective is to implement an **Order / Inventory Saga** that coordinates the Order and Inventory modules through explicit asynchronous contracts while preserving strict module ownership.

The Saga must:

- Coordinate the Order / Inventory workflow.
- Preserve module-owned business state.
- Use RabbitMQ for asynchronous communication.
- Use the existing Outbox infrastructure for reliable publication.
- Use the existing idempotent consumer infrastructure.
- Handle duplicate and concurrent message delivery safely.
- Support failure handling and Inventory compensation where required.
- Preserve CorrelationId, CausationId, EventId, and tracing information.
- Recover correctly after retries or process restarts.
- Avoid distributed transactions and cross-module database access.

The Saga coordinates the workflow; it does not own Order or Inventory business state.

---

# 2. Architectural Position

The workflow introduced by this phase is:

```text
Order
  |
  | OrderCreated
  v
Order Saga
  |
  | ReserveInventory
  v
RabbitMQ
  |
  v
Inventory Consumer
  |
  v
Inventory Application
  |
  v
Inventory Domain
  |
  +----------------------+
  |                      |
  v                      v
InventoryReserved   InventoryReservationFailed
  |                      |
  +----------+-----------+
             |
             v
          RabbitMQ
             |
             v
        Order Saga
         /       \
        /         \
   Success       Failure
      |              |
      v              v
Saga advances    Fail/Cancel Order
```

The following boundary is mandatory:

```text
Orders
  |
  X --> InventoryDbContext
```

Instead:

```text
Orders
  |
  v
Explicit Message Contract
  |
  v
RabbitMQ
  |
  v
Inventory Module
  |
  v
Inventory State
```

Inventory must never directly modify Order state.

Orders must never directly modify Inventory state.

---

# 3. Scope

Phase 8 consists of the following sub-phases:

1. **8.1 — Saga Architecture & Baseline Audit**
2. **8.2 — Saga State Model & Persistence**
3. **8.3 — Order Saga Orchestration**
4. **8.4 — Inventory Reservation Command Flow**
5. **8.5 — Inventory Result Events**
6. **8.6 — Order State Transitions**
7. **8.7 — Compensation & Release Inventory**
8. **8.8 — Idempotency, Concurrency & Recovery Hardening**
9. **8.9 — Observability & Distributed Trace Verification**
10. **8.10 — Final Verification, Hardening & Completion**

Sub-phases must be implemented sequentially.

Do not implement later sub-phases prematurely unless required by a dependency that is explicitly documented.

---

# 4. Phase Boundaries

## Included

- Order / Inventory Saga orchestration.
- Saga state persistence.
- Reserve Inventory command flow.
- Inventory reservation success/failure events.
- Order state transitions driven by Saga results.
- Inventory release compensation where required.
- Saga idempotency and concurrency hardening.
- Recovery after message redelivery and process restart.
- Correlation and tracing across asynchronous boundaries.
- Real SQL Server and RabbitMQ integration testing.
- Architecture and documentation updates required by the implementation.

## Explicitly Out of Scope

The following must NOT be implemented as part of Phase 8:

- Stripe integration.
- Payment Intent creation.
- Payment webhooks.
- Payment refunds.
- Payment Saga.
- Payment compensation.
- Reservation expiration worker.
- Shipping workflows.
- Notifications.
- Elasticsearch/search.
- Promotions/coupons.
- Microservice extraction.
- Distributed database transactions.

Payment-related compensation belongs to later phases.

Reservation expiration belongs to the dedicated later phase.

---

# 5. Architectural Rules

The implementation agent MUST preserve the following rules.

## 5.1 Module Ownership

Orders owns:

- Order state.
- Order lifecycle.
- Saga/orchestration state.

Inventory owns:

- Inventory item state.
- Reservation state.
- Reservation lifecycle.

The Saga does not become the owner of Inventory state.

---

## 5.2 No Cross-Module Database Access

Forbidden:

```text
Orders
  |
  +--> InventoryDbContext
```

Forbidden:

```text
Inventory
  |
  +--> OrdersDbContext
```

Required:

```text
Module A
  |
  +--> Explicit Contract
          |
          v
      Module B
```

---

## 5.3 No Distributed Transactions

A transaction must remain local to one module.

For example:

```text
Inventory Transaction
    |
    +--> Update Inventory / Reservation
    |
    +--> Create Outbox Message
    |
    +--> Commit
```

Cross-module coordination must use messaging.

---

## 5.4 At-Least-Once Delivery

Exactly-once delivery must never be assumed.

Every consumer and Saga transition must tolerate duplicate delivery.

---

## 5.5 Idempotency

There are two related requirements:

```text
Message Idempotency
        +
Saga Transition Idempotency
```

A duplicate message must not cause duplicate business effects or repeated invalid state transitions.

---

## 5.6 Correlation and Causation

The workflow must preserve:

- EventId / MessageId
- CorrelationId
- CausationId
- TraceId where available

Example:

```text
OrderCreated
    CorrelationId = C1

ReserveInventory
    CorrelationId = C1
    CausationId  = OrderCreated.EventId

InventoryReserved
    CorrelationId = C1
    CausationId  = ReserveInventory.MessageId
```

---

# 6. 8.1 — Saga Architecture & Baseline Audit

## Objective

Establish the final Saga design before implementing the workflow.

## Tasks

- Re-audit the Orders module after Phase 7.
- Re-audit the Inventory module after Phase 7.
- Inspect the existing Order lifecycle.
- Inspect the existing Inventory reservation lifecycle.
- Inspect current Integration Event contracts.
- Inspect Outbox implementation.
- Inspect RabbitMQ topology and consumer infrastructure.
- Inspect idempotency implementation.
- Inspect current projections, including any Order summary projection affected by new events.
- Define the Saga ownership boundary.
- Define Saga states.
- Define allowed state transitions.
- Define commands/messages.
- Define result events.
- Define compensation commands.
- Define CorrelationId/CausationId propagation.
- Identify any required ADR updates.

## Deliverables

- Approved Saga state model.
- Approved message/event flow.
- Approved ownership boundaries.
- Identified persistence changes.
- Identified contract changes.
- ADR updates where architectural decisions are introduced.

## Verification

- Architecture boundaries remain valid.
- No cross-module dependency is introduced.
- No implementation begins before ambiguous architectural decisions are resolved.

---

# 7. 8.2 — Saga State Model & Persistence

## Objective

Introduce persistent state representing the progress of the Saga.

## Proposed Model

The exact model must follow the implementation discovered during 8.1.

Conceptually:

```text
OrderSaga
├── Id
├── OrderId
├── CorrelationId
├── State
├── CreatedAtUtc
├── UpdatedAtUtc
└── ConcurrencyToken / Version
```

The model must not duplicate Order or Inventory business state.

## Tasks

- Create Saga state entity/model.
- Define Saga state enumeration/value representation.
- Define valid transitions.
- Add EF Core configuration.
- Add database migration.
- Add appropriate indexes.
- Add concurrency protection.
- Define uniqueness constraints where required.
- Implement persistence abstraction only where there is a concrete need.
- Integrate with Orders persistence.

## Required Properties

The design must support:

- Reliable state persistence.
- Recovery after process restart.
- Duplicate-event handling.
- Concurrent event handling.
- Querying by OrderId / CorrelationId where required.

## Verification

- Mapping tests.
- Persistence integration tests.
- Unique constraint tests.
- Concurrency tests.
- Migration verification.

---

# 8. 8.3 — Order Saga Orchestration

## Objective

Implement the Order-owned Saga orchestrator.

## Initial Flow

```text
OrderCreated
     |
     v
Create / Load Saga
     |
     v
WaitingForInventory
     |
     v
ReserveInventory
```

## Responsibilities

The Saga must:

1. Start or resume the workflow.
2. Persist its state.
3. Issue the Inventory reservation command.
4. Handle Inventory results.
5. Validate state transitions.
6. Advance or fail the workflow.
7. Remain safe under duplicate delivery.
8. Remain recoverable after process restart.

## Important Constraint

The Saga must not contain Inventory business rules.

It only coordinates the workflow.

## Verification

Unit tests for:

- Saga creation.
- Saga resume.
- Duplicate `OrderCreated`.
- Valid transition.
- Invalid transition.
- Already-completed Saga.
- Repeated command generation.

---

# 9. 8.4 — Inventory Reservation Command Flow

## Objective

Connect the Saga to the existing Inventory reservation capability through messaging.

## Flow

```text
Order Saga
    |
    | ReserveInventory
    v
Outbox
    |
    v
RabbitMQ
    |
    v
Inventory Consumer
    |
    v
Inventory Application
    |
    v
Inventory Domain
```

## Tasks

- Define the command/message contract.
- Include the required OrderId/reservation data.
- Preserve message metadata.
- Implement or extend Inventory consumer handling.
- Resolve the appropriate Inventory application use case.
- Execute reservation through the Inventory domain.
- Ensure the operation remains atomic with its Outbox record.
- ACK only after successful processing/idempotency completion according to Phase 7 consumer semantics.

## Verification

Real integration test:

```text
Saga
  -> Outbox
  -> RabbitMQ
  -> Inventory Consumer
  -> Inventory Reservation
```

---

# 10. 8.5 — Inventory Result Events

## Objective

Publish the outcome of the reservation operation back to the Saga.

## Success Flow

```text
ReserveInventory
      |
      v
Inventory
      |
      v
InventoryReserved
```

## Failure Flow

```text
ReserveInventory
      |
      v
Inventory
      |
      v
InventoryReservationFailed
```

## Tasks

- Define stable Integration Event contracts.
- Define event metadata.
- Map Inventory operation results to events.
- Persist result events through Outbox.
- Publish through existing RabbitMQ infrastructure.
- Preserve CorrelationId and CausationId.
- Add consumers/handlers in Orders for the result events.
- Update any affected projection infrastructure.

## Verification

- Contract tests.
- Unit tests.
- Outbox tests.
- Real RabbitMQ integration tests.
- Duplicate result-event tests.

---

# 11. 8.6 — Order State Transitions

## Objective

Apply Inventory results to the Order lifecycle through the Saga.

## Success

```text
InventoryReserved
       |
       v
Order Saga
       |
       v
Valid Order transition
```

## Failure

```text
InventoryReservationFailed
       |
       v
Order Saga
       |
       v
Fail / Cancel Order
```

The exact Order states must be based on the existing Order domain model.

Do not introduce a second independent Order lifecycle.

## Tasks

- Implement success transition.
- Implement failure transition.
- Validate transitions in the Order domain.
- Persist Order state atomically.
- Persist Saga state consistently with the transition.
- Handle duplicate result events.
- Prevent invalid transitions.

## Verification

Test:

- Reservation success.
- Reservation failure.
- Duplicate success.
- Duplicate failure.
- Conflicting results.
- Invalid transitions.
- Concurrent result handling.

---

# 12. 8.7 — Compensation & Release Inventory

## Objective

Implement Inventory release where the Order/Inventory workflow requires compensation.

## Flow

```text
Order Saga
    |
    | ReleaseInventory
    v
RabbitMQ
    |
    v
Inventory Consumer
    |
    v
Release Reservation
    |
    v
InventoryReleased
```

## Rules

- Inventory owns the release operation.
- Orders requests release through an explicit contract.
- Inventory does not modify Order state.
- Release must be idempotent.
- Repeated release commands must not corrupt reservation state.

## Scope Limitation

Do not implement:

- Payment refund.
- Stripe compensation.
- Payment Saga.

Those belong to later phases.

## Verification

- Release success.
- Duplicate release.
- Release after already released reservation.
- Release retry.
- Release message redelivery.
- Compensation recovery after consumer restart.

---

# 13. 8.8 — Idempotency, Concurrency & Recovery Hardening

## Objective

Prove that the Saga remains correct under realistic distributed-system failure modes.

## Required Scenarios

### Duplicate Result

```text
InventoryReserved
InventoryReserved
```

Expected:

```text
One business transition
No duplicate effect
```

### Concurrent Result

```text
Consumer A -> InventoryReserved
Consumer B -> InventoryReserved
```

Expected:

```text
Single valid transition
```

### Conflicting Result

```text
InventoryReserved
InventoryReservationFailed
```

Expected:

```text
Invalid transition is rejected safely.
```

The implementation must not blindly apply both.

### Consumer Crash

```text
Message
  |
  v
Business Processing
  |
  v
Crash before ACK
  |
  v
Redelivery
  |
  v
Idempotent Processing
```

Expected:

```text
No duplicate business effect
```

### Saga Restart

```text
Saga State Persisted
        |
        v
Process Restart
        |
        v
Saga Resumes
```

Expected:

```text
Workflow remains recoverable.
```

## Tasks

- Add optimistic concurrency where required.
- Add uniqueness constraints where required.
- Harden state transitions.
- Verify consumer idempotency integration.
- Verify message redelivery behavior.
- Verify Saga recovery.
- Verify retry behavior.
- Verify DLQ behavior where applicable.
- Ensure failures do not silently lose workflow state.

---

# 14. 8.9 — Observability & Distributed Trace Verification

## Objective

Make the complete Saga traceable across asynchronous boundaries.

## Required Trace

```text
Checkout
   |
   v
OrderCreated
   |
   v
Order Saga
   |
   v
ReserveInventory
   |
   v
RabbitMQ
   |
   v
Inventory Consumer
   |
   v
InventoryReserved
   |
   v
RabbitMQ
   |
   v
Order Saga
   |
   v
Order Transition
```

## Required Context

Where applicable, logs and telemetry must expose:

- OrderId.
- SagaId.
- CorrelationId.
- Message/Event Id.
- CausationId.
- Current Saga state.
- Previous state.
- New state.
- TraceId / SpanId.

## Tasks

- Verify correlation propagation.
- Verify causation propagation.
- Add Saga-specific structured logging.
- Add appropriate Activity instrumentation.
- Verify asynchronous trace continuity.
- Ensure sensitive information is not logged.

## Verification

Run a real end-to-end workflow and confirm that the complete operation can be reconstructed from logs/traces.

---

# 15. 8.10 — Final Verification, Hardening & Completion

## Objective

Perform final validation against real infrastructure and close Phase 8.

## Build

```bash
dotnet build FluxCommerce.slnx
```

Expected:

```text
0 warnings
0 errors
```

## Architecture Tests

All architecture tests must pass.

Verify:

- Domain boundaries.
- Application boundaries.
- Infrastructure boundaries.
- Module isolation.
- BuildingBlocks restrictions.
- No cross-module DbContext references.

## Unit Tests

All existing unit tests plus new Phase 8 tests must pass.

## Integration Tests

Use real infrastructure where required:

```text
SQL Server
RabbitMQ
Redis
```

## Mandatory Saga Scenarios

At minimum:

1. Order Created.
2. Saga starts.
3. Inventory reservation succeeds.
4. Inventory reservation fails.
5. Duplicate reservation result.
6. Concurrent reservation result.
7. Inventory release.
8. Duplicate release.
9. Invalid Saga transition.
10. Consumer retry.
11. Consumer redelivery.
12. Saga recovery after restart.
13. Correlation/Causation propagation.
14. End-to-end success workflow.
15. End-to-end failure workflow.

## Documentation

Update:

- `ARCHITECTURE.md` where required.
- Relevant ADRs.
- Phase documentation.
- Completion report.

Do not create unrelated documentation.

---

# 16. Testing Strategy

Phase 8 testing should be layered.

## Unit Tests

Test:

- Saga state machine.
- State transitions.
- Order transition rules.
- Command generation.
- Event handling.
- Compensation decisions.
- Invalid transitions.
- Duplicate handling.

## Integration Tests

Test:

- SQL persistence.
- Saga persistence.
- Outbox.
- RabbitMQ.
- Consumers.
- Inventory reservation.
- Inventory release.
- Event publication.
- Event consumption.

## End-to-End Tests

Test the actual workflow:

```text
Order
  ↓
Saga
  ↓
Outbox
  ↓
RabbitMQ
  ↓
Inventory
  ↓
Outbox
  ↓
RabbitMQ
  ↓
Saga
  ↓
Order
```

The critical end-to-end tests must use the real infrastructure already established by previous phases.

---

# 17. Definition of Done

Phase 8 is complete only when all of the following are true:

1. Saga ownership is explicitly defined.
2. Saga state is persistent.
3. Saga state transitions are explicit and validated.
4. Orders does not access Inventory internals.
5. Inventory does not access Orders internals.
6. Inventory reservation is triggered through an explicit message contract.
7. Inventory publishes reservation results through the Outbox.
8. Order Saga consumes reservation results.
9. Order state changes only through valid domain/application boundaries.
10. Inventory release compensation is implemented where required.
11. Message delivery is treated as At-Least-Once.
12. Duplicate messages are safely handled.
13. Concurrent messages are safely handled.
14. Saga state is recoverable after process restart.
15. CorrelationId is preserved.
16. CausationId is preserved.
17. Distributed tracing remains usable across RabbitMQ boundaries.
18. Real SQL Server integration tests pass.
19. Real RabbitMQ integration tests pass.
20. Architecture tests pass.
21. Unit tests pass.
22. No Payment functionality is introduced.
23. No Reservation Expiration functionality is introduced.
24. No distributed transaction is introduced.
25. Documentation is synchronized with the implementation.

---

# 18. Execution Order

The implementation must follow:

```text
8.1 Architecture & Baseline Audit
          |
          v
8.2 Saga State Model & Persistence
          |
          v
8.3 Order Saga Orchestration
          |
          v
8.4 Inventory Reservation Command Flow
          |
          v
8.5 Inventory Result Events
          |
          v
8.6 Order State Transitions
          |
          v
8.7 Compensation & Release Inventory
          |
          v
8.8 Idempotency / Concurrency / Recovery
          |
          v
8.9 Observability & Distributed Tracing
          |
          v
8.10 Final Verification & Completion
```

Each sub-phase must end with appropriate verification before proceeding.

---

# 19. Final Architectural Principle

The central rule of Phase 8 is:

> **The Saga coordinates; modules own state.**

Therefore:

```text
Orders
  └── Owns Order + Saga state

Inventory
  └── Owns Inventory + Reservation state

RabbitMQ
  └── Transports asynchronous messages

Outbox
  └── Guarantees reliable publication

Consumers
  └── Perform idempotent processing

Saga
  └── Coordinates the business workflow
```

The desired result is a reliable cross-module workflow without violating the Modular Monolith boundaries:

```text
Order
  |
  v
Order Saga
  |
  | ReserveInventory
  v
RabbitMQ
  |
  v
Inventory
  |
  | InventoryReserved / InventoryReservationFailed
  v
RabbitMQ
  |
  v
Order Saga
  |
  +--> Advance Order
  |
  +--> Fail / Cancel Order
  |
  +--> Request Inventory Release when compensation is required
```

Phase 8 must establish this workflow without prematurely introducing Payment, Payment Compensation, Reservation Expiration, or distributed deployment complexity.
