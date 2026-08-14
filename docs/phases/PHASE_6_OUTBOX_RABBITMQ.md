# Phase 6 — Outbox + RabbitMQ
## Implementation Plan & Execution Specification

> **Status:** Planned  
> **Phase:** 6  
> **Primary Scope:** Transactional Outbox + Reliable RabbitMQ Publication  
> **Depends On:** Phase 5 — Inventory + Concurrency  
> **Next:** Phase 7 — Idempotent Consumers  
> **Authoritative References:** `docs/PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`, existing phase specifications and completion reports

---

# 1. Purpose

Phase 6 introduces the reliable asynchronous messaging foundation required by the platform.

The primary objective is to ensure that a module can persist:

```text
Business State Change
        +
Outbox Message
```

inside the **same database transaction**, and publish the resulting integration event to RabbitMQ asynchronously through an Outbox Dispatcher.

The target flow is:

```text
Application Use Case
        |
        v
Domain Operation
        |
        +--------------------+
        |                    |
        v                    v
Business State         Domain Event
        |                    |
        +---------+----------+
                  |
                  v
        Integration Event Mapping
                  |
                  v
             Outbox Record
                  |
                  v
              DB COMMIT
                  |
                  v
         Outbox Dispatcher
                  |
                  v
              RabbitMQ
```

Phase 6 establishes reliable **publication**.

It does **not** implement:

- Idempotent Consumers
- Inbox / ProcessedMessage consumer infrastructure
- Saga orchestration
- Payment workflows
- Stripe integration
- Distributed transactions
- Compensation workflows

Those belong to later phases.

---

# 2. Architectural Principles

## 2.1 Modular Monolith

The system remains a Modular Monolith.

Modules continue to own:

- Domain logic
- Application use cases
- Business state
- Persistence boundaries

Messaging must not bypass module boundaries.

## 2.2 Module-per-DbContext

Each module owns its DbContext and persistence boundary.

Do not introduce a global business DbContext for messaging.

The Outbox transaction must remain inside the module transaction boundary.

```text
Orders Transaction
    |
    +-- Order State
    |
    +-- Outbox Message

Inventory Transaction
    |
    +-- Inventory State
    |
    +-- Outbox Message
```

There must be no distributed transaction across these boundaries.

## 2.3 Domain Events vs Integration Events

A Domain Event is an internal event.

An Integration Event is an explicit contract that crosses a module boundary.

Therefore:

```text
Domain Event
    !=
Integration Event
```

Do not automatically publish every Domain Event.

## 2.4 Clean Architecture

The dependency direction remains:

```text
API
 |
 v
Application
 |
 v
Domain

Infrastructure
 |
 +--> implements required abstractions
```

Forbidden:

```text
Domain
    X--> RabbitMQ
    X--> EF Core implementation

Application
    X--> RabbitMQ client
```

RabbitMQ implementation belongs to Infrastructure.

## 2.5 At-Least-Once Delivery

RabbitMQ publication follows:

```text
At-Least-Once Delivery
```

Exactly-once delivery must not be assumed.

Consumer idempotency is Phase 7.

---

# 3. Phase Scope

## In Scope

- Outbox persistence
- Outbox transaction integration
- Integration Event contracts
- Domain Event → Integration Event mapping
- Outbox store
- Outbox claiming semantics
- RabbitMQ publisher
- RabbitMQ infrastructure
- Outbox Dispatcher
- Retry behavior
- Failure recovery
- Correlation/Causation metadata propagation
- Messaging observability
- End-to-end publication tests
- Architecture verification
- Security and operational hardening
- Documentation synchronization
- Phase completion report

## Out of Scope

- Consumer business logic
- Consumer idempotency
- Inbox pattern
- Saga orchestration
- Payment
- Stripe
- Compensation
- Distributed transactions
- Cross-module database transactions
- Elasticsearch
- New unrelated business capabilities

---

# 4. Execution Rules

The implementing agent MUST:

1. Read the current project context and architecture before modifying code.
2. Inspect existing messaging/outbox abstractions before creating new ones.
3. Reuse existing abstractions when appropriate.
4. Avoid duplicate infrastructure abstractions.
5. Preserve existing behavior outside Phase 6.
6. Avoid premature implementation of Phase 7/8/9 capabilities.
7. Keep module boundaries intact.
8. Preserve existing error-code conventions.
9. Add tests for reliability-sensitive behavior.
10. Update documentation after implementation is verified.
11. Not silently change architectural decisions.
12. If an architectural decision must change, document the decision before implementation.

---

# 5. 6.1 — Baseline & Messaging Architecture Audit

## Objective

Understand and verify the existing messaging foundation before implementation.

## Inspect

At minimum:

- `BuildingBlocks.Messaging`
- `BuildingBlocks.Outbox`
- `BuildingBlocks.Persistence`
- `BuildingBlocks.Infrastructure`
- RabbitMQ-related infrastructure
- `BaseDbContext`
- Domain Event infrastructure
- `DomainEventInterceptor`
- `IOutboxStore`
- `IOutboxDispatcher`
- `OutboxMessage`
- `OutboxStatus`
- Correlation ID infrastructure
- Trace/observability infrastructure
- Orders Domain Events
- Inventory Domain Events

## Questions

```text
How are Domain Events currently raised?

How are Domain Events currently collected?

Where does BaseDbContext participate?

Where should Outbox persistence occur?

Which module owns the Outbox transaction?

How are correlation IDs represented?

What RabbitMQ abstractions already exist?

Which parts are foundation-only and which are production-ready?
```

## Constraints

Do not modify business behavior during this audit.

## Exit Criteria

- Existing messaging architecture documented.
- Existing abstractions identified.
- Gaps explicitly identified.
- No duplicate abstractions introduced.

---

# 6. 6.2 — Outbox Persistence Model

## Objective

Implement the persistent Outbox record required for transactional publication.

The following must be persisted atomically:

```text
Business State
+
Outbox Message
```

inside the same module transaction.

## Required Data

The final model should support, as applicable:

```text
MessageId
EventId
EventType
Payload
OccurredAtUtc
CorrelationId
CausationId
CreatedAtUtc
ProcessedAtUtc
RetryCount
Status
Error
```

Use existing project abstractions and conventions where possible.

## Tasks

- Finalize Outbox entity/model.
- Configure EF Core mapping.
- Define required fields.
- Define indexes.
- Define status representation.
- Define retry metadata.
- Define error storage.
- Add migration where required.
- Verify migration against the correct module database.
- Verify no cross-module persistence dependency is introduced.

## Tests

- EF configuration.
- Required constraints.
- Required indexes.
- Migration application.
- Serialization persistence.
- Status persistence.

## Exit Criteria

Outbox messages can be persisted reliably inside the correct module persistence boundary.

---

# 7. 6.3 — Domain Event → Outbox Integration

## Objective

Connect selected Domain Events to Integration Event publication through the Outbox.

## Required Flow

```text
Domain Operation
       |
       v
Domain Event
       |
       v
Explicit Integration Event Mapping
       |
       v
Outbox Message
       |
       v
Same DB Transaction
```

Do not publish every Domain Event automatically.

## Candidate Events

Review existing events, including:

```text
Inventory
    ReservationExpired

Orders
    OrderCreated
    OrderCancelled
```

These are candidates for review, not automatic publication requirements.

## Transaction Requirements

If the business operation rolls back:

```text
Business State = Rolled Back
Outbox = Rolled Back
```

If the transaction commits:

```text
Business State = Persisted
Outbox = Persisted
```

## Tests

### Successful transaction

```text
Business operation
    ↓
Domain event
    ↓
Outbox created
    ↓
Commit
```

### Failed transaction

```text
Business operation
    ↓
Failure
    ↓
Rollback
```

Verify that the Outbox record is not independently committed.

## Exit Criteria

Selected integration events are created transactionally with their originating business state.

---

# 8. 6.4 — Integration Event Contracts & Versioning

## Objective

Define stable integration contracts that can cross module boundaries and be transported through RabbitMQ.

## Requirements

Integration Event contracts must not expose:

- EF entities
- Internal domain entities
- Internal repositories
- Infrastructure implementation details

## Contract Metadata

Support:

```text
EventId
EventType
EventVersion
OccurredAtUtc
CorrelationId
CausationId
```

and other metadata required by the architecture.

## Tasks

- Define/complete Integration Event base contract.
- Define event metadata.
- Define event type identifiers.
- Define versioning strategy.
- Define serialization contract.
- Define naming conventions.
- Define payload boundaries.

## Tests

- Serialization/deserialization.
- Required metadata.
- Stable event type.
- Version handling.
- Contract does not expose internal implementation types.

## Exit Criteria

Integration Events are stable transport contracts independent of module internals.

---

# 9. 6.5 — Outbox Store & Claiming Semantics

## Objective

Implement safe retrieval and processing semantics for background publication.

A naive pending-message query is insufficient.

The store must account for:

- Concurrent dispatcher execution
- Claiming
- Retries
- Crashes
- Recovery

## Required Operations

Conceptually:

```text
GetPending
Claim
MarkPublished
MarkRetryableFailure
RecoverExpiredClaims
```

The exact API must follow existing abstractions and architecture.

## State Flow

```text
Pending
   |
   v
Claimed / InProgress
   |
   +---- Success ----> Published
   |
   +---- Failure ----> Retryable / Pending
```

If the process crashes after claiming, the message must remain recoverable.

## Tests

- Concurrent dispatcher attempts.
- Claim exclusivity.
- Claim expiration/recovery.
- Published messages are not treated as pending.
- Retryable failures remain recoverable.

## Exit Criteria

Outbox records can be safely processed by background workers under concurrent execution and failure.

---

# 10. 6.6 — RabbitMQ Publishing Infrastructure

## Objective

Implement actual RabbitMQ publishing infrastructure.

Phase 0 connectivity/readiness is not sufficient for Phase 6.

## Tasks

Implement as required:

- Publisher abstraction.
- RabbitMQ connection handling.
- Exchange configuration.
- Routing strategy.
- Message serialization.
- Message metadata propagation.
- Publisher confirmation strategy where required.
- Connection/channel lifecycle.
- Failure handling.
- Recovery behavior.

## Conceptual Flow

```text
Integration Event
       |
       v
RabbitMQ Publisher
       |
       v
Exchange
       |
       v
RabbitMQ
```

## Rules

The publisher must not contain business rules.

It must not:

- Create Orders.
- Reserve Inventory.
- Release Inventory.
- Execute Saga logic.
- Make business decisions.

## Exit Criteria

A valid Integration Event can be published to RabbitMQ using production-oriented infrastructure semantics.

---

# 11. 6.7 — Outbox Dispatcher Background Service

## Objective

Implement the background worker connecting the Outbox to RabbitMQ.

## Required Flow

```text
Background Service
       |
       v
Claim Outbox Messages
       |
       v
Publish to RabbitMQ
       |
    +--+--+
    |     |
 Success Failure
    |     |
    v     v
Published Retry
```

## Responsibilities

The dispatcher may:

- Poll the Outbox.
- Claim messages.
- Publish messages.
- Mark successful publication.
- Record failures.
- Schedule retries.
- Recover interrupted claims.
- Emit structured logs.
- Respect cancellation.
- Shut down gracefully.

## The Dispatcher Must Not

- Execute business workflows.
- Implement Saga logic.
- Implement consumer idempotency.
- Modify business entities directly.
- Call other module application services to simulate consumers.

## Operational Parameters

Define explicitly:

- Batch size.
- Polling interval.
- Retry delay/backoff.
- Claim timeout.
- Maximum retry behavior if applicable.
- Shutdown behavior.

## Exit Criteria

The dispatcher can continuously publish pending Outbox messages and recover from transient failures.

---

# 12. 6.8 — Retry & Failure Recovery

## Objective

Prove that failures do not silently lose events.

## Required Scenarios

### RabbitMQ unavailable

```text
Business Transaction
      |
      v
Commit
      |
      v
RabbitMQ unavailable
      |
      v
Publish fails
      |
      v
Outbox remains retryable
```

Business state remains persisted.

### RabbitMQ recovers

```text
Retry
  ↓
RabbitMQ available
  ↓
Publish succeeds
  ↓
Outbox marked Published
```

### Dispatcher crash

```text
Claim
  ↓
Crash
  ↓
Restart
  ↓
Recover message
```

## Retry Policy

Define:

- Retryable errors.
- Non-retryable errors if applicable.
- Backoff.
- Maximum attempts if applicable.
- Failure metadata.
- Recovery behavior.

Do not confuse Outbox publication retries with consumer retries.

## Exit Criteria

Transient RabbitMQ or dispatcher failures do not permanently lose committed events.

---

# 13. 6.9 — Correlation, Causation & Observability

## Objective

Provide traceability across the asynchronous publication boundary.

## Required Metadata

At minimum, where applicable:

```text
MessageId
EventId
CorrelationId
CausationId
OccurredAtUtc
EventType
EventVersion
```

## Propagation

```text
HTTP Request
    |
    v
Correlation ID
    |
    v
Application
    |
    v
Domain Event
    |
    v
Outbox
    |
    v
Dispatcher
    |
    v
RabbitMQ Message
```

## Logging

Dispatcher logs should allow identification of:

- Message ID
- Event ID
- Event type
- Correlation ID
- Attempt
- Result
- Failure information

Do not log sensitive business payloads unnecessarily.

## Exit Criteria

A published event can be correlated back to the originating business operation.

---

# 14. 6.10 — End-to-End Integration Tests

## Objective

Verify the complete Phase 6 path using real infrastructure where required.

## Scenario A — Successful Publication

```text
Business Operation
    ↓
Business State + Outbox
    ↓
Commit
    ↓
Dispatcher
    ↓
RabbitMQ
```

Verify:

- Business state persisted.
- Outbox persisted.
- Message published.
- Outbox eventually marked Published.

## Scenario B — RabbitMQ Failure

Verify:

```text
Business State = persisted
Outbox = retryable
```

## Scenario C — Transaction Rollback

Verify:

```text
Business State = absent
Outbox = absent
```

## Scenario D — Dispatcher Recovery

```text
Pending Outbox
    ↓
Dispatcher starts
    ↓
RabbitMQ available
    ↓
Message published
```

## Scenario E — Multiple Messages

Verify:

- No message loss.
- One failed message does not permanently block unrelated messages.
- Retry behavior works.
- Batch processing works.
- Ordering is relied upon only if explicitly required.

## Scenario F — Duplicate Publication Boundary

Simulate:

```text
RabbitMQ publish succeeds
        ↓
Application crashes
        ↓
Outbox not marked Published
        ↓
Retry
```

Verify behavior is consistent with At-Least-Once Delivery.

Do not add consumer idempotency here.

## Exit Criteria

End-to-end tests prove the transactional Outbox → RabbitMQ publication path.

---

# 15. 6.11 — Architecture & Security Hardening

## Architecture Tests

Verify:

```text
Domain
    X--> RabbitMQ
    X--> EF Core implementation

Application
    X--> RabbitMQ client

Module A
    X--> Module B Infrastructure

Contracts
    X--> Internal implementation details
```

Verify:

- Module boundaries remain intact.
- Outbox does not create cross-module persistence coupling.
- Infrastructure remains behind abstractions.
- Controllers remain thin.
- Business rules remain outside messaging infrastructure.

## Security

Review:

- RabbitMQ credentials.
- Secret configuration.
- Environment variables.
- Connection strings.
- Sensitive message payload logging.
- Production configuration.
- TLS readiness where applicable.

## Exit Criteria

Messaging introduces no architecture or security regression.

---

# 16. 6.12 — Performance & Operational Hardening

## Database

Review:

- Pending-message indexes.
- Claiming query performance.
- Batch retrieval.
- Transaction duration.
- Cleanup strategy where applicable.

## Dispatcher

Review:

- Batch size.
- Polling interval.
- Connection reuse.
- Channel lifecycle.
- Memory usage.
- Backpressure.
- Graceful shutdown.

## RabbitMQ

Review:

- Exchange topology.
- Routing.
- Publisher throughput.
- Connection recovery.
- Failure behavior.

Do not prematurely optimize.

## Exit Criteria

No obvious performance or operational design flaw remains in the Phase 6 messaging path.

---

# 17. 6.13 — Documentation Synchronization

Update documentation after implementation and tests are stable.

## Review

- `docs/ARCHITECTURE.md`
- `docs/PROJECT_CONTEXT.md`
- Relevant ADRs
- Existing phase documentation

## Document

- Outbox ownership.
- Transaction boundary.
- Integration Event strategy.
- RabbitMQ topology.
- Dispatcher behavior.
- Retry semantics.
- At-Least-Once semantics.
- Correlation/Causation propagation.
- Failure recovery.
- Explicit Phase 7/8 deferrals.

## ADR Rule

Do not create or modify an ADR merely because code was implemented.

Create/update an ADR only if an architectural decision changed or a new significant decision requires durable documentation.

## Exit Criteria

Documentation accurately describes the implementation that was verified.

---

# 18. 6.14 — Final Hardening & Phase Completion

## Verification

Run:

```text
dotnet build
```

Then:

```text
Unit Tests
Architecture Tests
Integration Tests
RabbitMQ E2E Tests
Failure/Recovery Tests
Migration Verification
```

Review:

- Build warnings.
- Test failures.
- Architecture violations.
- Database migrations.
- RabbitMQ connectivity.
- Retry behavior.
- Logging.
- Correlation.
- Security.
- Performance.
- Documentation consistency.

## Completion Report

Create:

```text
docs/completed/PHASE_6_COMPLETION_REPORT.md
```

The report must contain:

1. Phase objective.
2. Implemented sub-phases.
3. Outbox design.
4. Integration Event contracts.
5. Transaction boundary.
6. RabbitMQ implementation.
7. Dispatcher implementation.
8. Retry semantics.
9. Failure recovery.
10. Observability.
11. Tests and results.
12. Architecture verification.
13. Security verification.
14. Performance review.
15. Database/migration verification.
16. Documentation updates.
17. Known limitations.
18. Explicitly deferred work.
19. Final completion status.

---

# 19. Phase 6 Definition of Done

## Outbox

- [ ] Outbox persistence is implemented.
- [ ] Outbox belongs to the correct persistence boundary.
- [ ] Business state and Outbox record commit atomically.
- [ ] Outbox indexes are appropriate.
- [ ] Retry metadata is persisted.
- [ ] Failure metadata is persisted.
- [ ] Interrupted processing is recoverable.

## Integration Events

- [ ] Domain Event → Integration Event mapping is explicit.
- [ ] Integration Events do not expose internal implementation details.
- [ ] Event metadata is present.
- [ ] Event versioning strategy is defined.
- [ ] Serialization is tested.

## RabbitMQ

- [ ] RabbitMQ publisher is implemented.
- [ ] Exchange/routing strategy is defined.
- [ ] Publishing failures are handled.
- [ ] Connection/channel lifecycle is production-oriented.
- [ ] At-Least-Once semantics are documented.

## Dispatcher

- [ ] Background dispatcher is implemented.
- [ ] Messages are claimed safely.
- [ ] Successful messages become Published.
- [ ] Failed messages remain recoverable.
- [ ] Retry behavior is implemented.
- [ ] Graceful shutdown is implemented.
- [ ] Dispatcher does not contain business logic.

## Observability

- [ ] Correlation ID propagation works.
- [ ] Causation metadata is preserved where applicable.
- [ ] Message/event identifiers are logged.
- [ ] Failures are observable.
- [ ] Sensitive payloads are not unnecessarily logged.

## Testing

- [ ] Unit tests pass.
- [ ] Architecture tests pass.
- [ ] Integration tests pass.
- [ ] RabbitMQ E2E tests pass.
- [ ] Transaction rollback scenario passes.
- [ ] Broker failure scenario passes.
- [ ] Dispatcher recovery scenario passes.
- [ ] Duplicate publication boundary is understood and tested.
- [ ] No Phase 7 consumer idempotency was silently introduced.

## Architecture

- [ ] No cross-module DbContext access.
- [ ] No distributed database transaction.
- [ ] Domain remains infrastructure-independent.
- [ ] Application remains independent from RabbitMQ implementation.
- [ ] Module boundaries remain enforced.
- [ ] Existing behavior outside Phase 6 remains unchanged.

## Documentation

- [ ] Architecture documentation is synchronized.
- [ ] Project context is synchronized if required.
- [ ] ADRs are synchronized if architectural decisions changed.
- [ ] Phase 6 completion report exists.
- [ ] Deferred Phase 7/8/9 work is explicitly documented.

---

# 20. Dependency Order

The implementation MUST follow this order unless a documented architectural reason requires otherwise:

```text
6.1 Baseline & Audit
        |
        v
6.2 Outbox Persistence
        |
        v
6.3 Domain Event → Outbox
        |
        v
6.4 Integration Event Contracts
        |
        v
6.5 Outbox Store & Claiming
        |
        v
6.6 RabbitMQ Publisher
        |
        v
6.7 Outbox Dispatcher
        |
        v
6.8 Retry & Recovery
        |
        v
6.9 Observability
        |
        v
6.10 E2E Integration Tests
        |
        v
6.11 Architecture & Security Hardening
        |
        v
6.12 Performance & Operational Hardening
        |
        v
6.13 Documentation
        |
        v
6.14 Final Verification
```

---

# 21. Explicit Phase Boundaries

| Capability | Phase |
|---|---:|
| Domain Events | Existing foundation / previous phases |
| Transactional Outbox | **Phase 6** |
| Integration Event Contracts | **Phase 6** |
| RabbitMQ Publisher | **Phase 6** |
| Outbox Dispatcher | **Phase 6** |
| Publication Retry / Recovery | **Phase 6** |
| Correlation/Causation Publication Metadata | **Phase 6** |
| Consumer | Phase 7 |
| Consumer Idempotency | Phase 7 |
| Inbox / ProcessedMessage | Phase 7 |
| Saga Orchestration | Phase 8 |
| Inventory Saga Workflow | Phase 8 |
| Payments | Phase 9 |
| Stripe Integration | Phase 9 |
| Compensation Workflows | Later workflow phases |

Do not move functionality between these phases without an explicit architectural decision.

---

# 22. Final Phase Flow

The completed Phase 6 should establish:

```text
                 MODULE
                   |
                   v
           Application Use Case
                   |
                   v
             Domain Operation
                   |
          +--------+--------+
          |                 |
          v                 v
   Business State      Domain Event
          |                 |
          +--------+--------+
                   |
                   v
       Integration Event Mapping
                   |
                   v
              Outbox Record
                   |
                   | SAME TRANSACTION
                   v
                 COMMIT
                   |
                   v
          Outbox Dispatcher
                   |
                   v
              RabbitMQ
                   |
                   v
       ─────── Phase 7 ───────
             Consumer
                   |
                   v
       ─────── Phase 8 ───────
               Saga
```

---

# 23. Phase 6 Success Criteria

The final question for this phase is not:

> "Can we send a message to RabbitMQ?"

The actual success criterion is:

> **Can the system commit business state and the intent to publish an integration event atomically, survive RabbitMQ/application failures, retry publication safely, and eventually publish the event without violating module boundaries?**

If the answer is yes, Phase 6 has achieved its architectural objective.

If the implementation is only:

```text
Application
    ↓
RabbitMQ.Publish()
```

then Phase 6 is **NOT complete**.
