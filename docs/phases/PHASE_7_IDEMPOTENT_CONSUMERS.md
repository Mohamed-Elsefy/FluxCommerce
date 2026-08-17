# Phase 7 --- Idempotent Consumers

> **Status:** Implemented & Verified (Phases 7.1–7.14)\
> **Phase:** 7\
> **Primary Goal:** Build reliable RabbitMQ consumers with idempotent
> message processing, Inbox / ProcessedMessage persistence,
> retry/failure handling, and production-oriented observability.
> **Completion:** See `docs/completed/PHASE_7_COMPLETION_REPORT.md` for the
> final verification results.

------------------------------------------------------------------------

## 1. Phase Overview

Phase 7 completes the messaging reliability foundation started in Phase
6.

``` text
Phase 6
Reliable Publication
        │
        ▼
     RabbitMQ
        │
        ▼
Phase 7
Reliable Consumption
        │
        ▼
Phase 8
Saga Orchestration
```

Phase 6 established:

``` text
Business State
+
Outbox Message
        ↓
Same Transaction
        ↓
Outbox Dispatcher
        ↓
RabbitMQ
```

Phase 7 must establish:

``` text
RabbitMQ
    ↓
Consumer
    ↓
Idempotency Check
    ↓
Application Handler
    ↓
Business State
+
ProcessedMessage
    ↓
Same Transaction
    ↓
Commit
    ↓
ACK
```

The central reliability property is:

> **At-Least-Once Delivery + Idempotent Processing = Reliable
> Consumption**

------------------------------------------------------------------------

# 2. Objectives

Phase 7 must provide:

1.  RabbitMQ consumer infrastructure.
2.  Consumer/application processing contracts.
3.  Inbox / ProcessedMessage persistence.
4.  Concurrency-safe idempotency.
5.  Atomic business processing + processed-message recording where they
    share a persistence boundary.
6.  Correct ACK semantics.
7.  Consumer retry and failure handling.
8.  Dead-letter handling where required.
9.  Correlation/Causation propagation.
10. Consumer observability.
11. Real RabbitMQ integration tests.
12. Duplicate, concurrency, crash, retry, and recovery verification.
13. Documentation synchronization.

------------------------------------------------------------------------

# 3. Architectural Boundaries

## 3.1 Explicitly In Scope

  Capability                      Phase
  ----------------------------- -------
  RabbitMQ Consumer               **7**
  Consumer Idempotency            **7**
  Inbox / ProcessedMessage        **7**
  Consumer Retry                  **7**
  Consumer Failure Handling       **7**
  Consumer Observability          **7**
  Duplicate Delivery Handling     **7**
  Consumer Concurrency Safety     **7**

## 3.2 Explicitly Out of Scope

The following must remain deferred:

``` text
Saga Orchestration
Order/Inventory Saga
Payment
Stripe
Refunds
Compensation Workflows
Distributed Transactions
Microservices
Elasticsearch
Unrelated Business Features
```

The Phase 8 boundary must remain intact.

------------------------------------------------------------------------

# 4. Architectural Principles

## 4.1 Modular Monolith

The system remains a Modular Monolith.

Do not introduce Microservices.

------------------------------------------------------------------------

## 4.2 Module Ownership

Each module owns its own business state.

Correct:

``` text
RabbitMQ
    ↓
Inventory Consumer
    ↓
Inventory Application
    ↓
Inventory Domain
    ↓
InventoryDbContext
```

Incorrect:

``` text
Consumer
    ↓
InventoryDbContext directly
```

Consumers must not bypass Application/Domain boundaries.

------------------------------------------------------------------------

## 4.3 No Cross-Module Database Access

A consumer must never directly modify another module's database.

``` text
Module A
    X→ Module B DbContext
```

Communication must occur through:

-   Application contracts
-   Integration Events
-   Messaging abstractions

------------------------------------------------------------------------

## 4.4 At-Least-Once Delivery

The system must assume:

``` text
At-Least-Once Delivery
```

Never depend on Exactly-Once delivery semantics.

A message may be delivered multiple times.

------------------------------------------------------------------------

## 4.5 Idempotency

The same message must not produce multiple business side effects.

``` text
Message X
Message X
Message X
    ↓
One Business Effect
```

Idempotency must remain safe under concurrent delivery.

------------------------------------------------------------------------

## 4.6 ACK After Successful Processing

The basic rule is:

``` text
Receive
  ↓
Process
  ↓
Persist
  ↓
Commit
  ↓
ACK
```

Never:

``` text
Receive
  ↓
ACK
  ↓
Process
```

------------------------------------------------------------------------

# 5. Processing Model

The target consumer pipeline is:

``` text
RabbitMQ
    │
    ▼
Consumer
    │
    ▼
Deserialize / Validate
    │
    ▼
Idempotency Check
    │
 ┌──┴──────────────┐
 │                 │
Duplicate          New
 │                 │
ACK                 ▼
             Application Handler
                     │
                     ▼
                Domain Logic
                     │
                     ▼
              Business State
                     +
              ProcessedMessage
                     │
                     ▼
                   Commit
                     │
                     ▼
                    ACK
```

------------------------------------------------------------------------

# 6. Sub-Phase 7.1 --- Baseline & Consumer Architecture Audit

## Objective

Understand and verify the Phase 6 messaging infrastructure before
implementing consumers.

## Audit

Review:

-   `BuildingBlocks.Messaging`
-   `BuildingBlocks.Idempotency`
-   `BuildingBlocks.Infrastructure`
-   RabbitMQ connection/provider infrastructure
-   Integration Event contracts
-   Event metadata
-   Existing event mappings
-   Existing DI registration
-   Existing architecture tests
-   Existing integration tests
-   Phase 6 publisher topology
-   Existing `ProcessedMessage`
-   `IIdempotencyStore`
-   `IDuplicateDetector`
-   `IdempotencyResult`

## Rules

Do not:

-   Rebuild the Outbox.
-   Redesign the RabbitMQ publisher.
-   Duplicate existing abstractions.
-   Introduce Saga.
-   Introduce Payments.
-   Change unrelated business behavior.

## Exit Criteria

-   Existing messaging architecture is understood.
-   Existing idempotency contracts are understood.
-   Consumer event candidates are identified.
-   No duplicate abstractions are introduced.

------------------------------------------------------------------------

# 7. Sub-Phase 7.2 --- Consumer Contract & Processing Model

## Objective

Define a small and explicit consumer processing abstraction.

The consumer infrastructure should separate transport concerns from
business processing.

Conceptually:

``` text
Integration Event
       ↓
Consumer
       ↓
Application Handler
```

## Consumer Infrastructure Responsibilities

-   Receive message.
-   Deserialize message.
-   Validate envelope/metadata.
-   Resolve event type.
-   Resolve appropriate handler.
-   Coordinate idempotency.
-   Manage ACK/NACK behavior.
-   Coordinate retry behavior.
-   Propagate correlation metadata.
-   Produce structured logs.

## Consumer Must Not

-   Implement business rules.
-   Modify DbContext directly.
-   Implement Saga.
-   Implement compensation.
-   Access another module's Infrastructure.
-   Own business state.

## Exit Criteria

-   Consumer contract is defined.
-   Handler contract is defined.
-   Transport and application responsibilities are separated.
-   Abstractions remain minimal and justified.

------------------------------------------------------------------------

# 8. Sub-Phase 7.3 --- Inbox / ProcessedMessage Persistence

## Objective

Turn the existing `ProcessedMessage` foundation into a real consumer
idempotency mechanism.

## Required Concept

A processed-message record must identify at minimum:

``` text
MessageId
Consumer / Handler Identity
ProcessedAtUtc
```

The exact model must follow the existing project abstractions and
architectural decisions.

## Persistence Ownership

Do not introduce an unrelated global business database.

Where the consumer changes a module's business state, the
processed-message record should participate in that module's persistence
boundary where appropriate.

Example:

``` text
InventoryDbContext
    ├── Inventory State
    └── ProcessedMessage
```

or:

``` text
OrdersDbContext
    ├── Order State
    └── ProcessedMessage
```

## Database Protection

The persistence model must prevent:

``` text
Same Message
+
Same Consumer
```

from being processed twice under concurrent delivery.

Use appropriate database uniqueness and transaction semantics.

## Exit Criteria

-   ProcessedMessage persistence is implemented.
-   Appropriate unique protection exists.
-   Lookup is efficient.
-   Persistence ownership is architecturally correct.
-   Existing idempotency abstractions are reused where appropriate.

------------------------------------------------------------------------

# 9. Sub-Phase 7.4 --- Atomic Idempotent Processing

## Objective

Ensure that business state changes and processed-message recording are
atomic whenever they share the same persistence boundary.

## Required Model

``` text
Message
   │
   ▼
Business Operation
   +
ProcessedMessage
   │
   ▼
Same Transaction
   │
   ▼
Commit
   │
   ▼
ACK
```

## Failure Scenario

The implementation must prevent:

``` text
Business State committed
        ↓
Application crashes
        ↓
ProcessedMessage not saved
        ↓
Message redelivered
        ↓
Duplicate Business Effect
```

## Duplicate Scenario

``` text
Message A
    ↓
Already Processed
    ↓
Do Not Execute Business Operation
    ↓
ACK
```

## Concurrent Scenario

``` text
Consumer A → Message X
Consumer B → Message X
```

Expected:

``` text
Business Effect = 1
ProcessedMessage = 1
```

Do not rely only on an application-level:

``` text
if (!exists)
```

Check. Database-level uniqueness and transaction semantics must provide
the final concurrency protection.

## Exit Criteria

-   Business state + ProcessedMessage are atomic where applicable.
-   Duplicate processing is prevented.
-   Concurrent duplicate delivery is safe.
-   Crash/recovery behavior is deterministic.

------------------------------------------------------------------------

# 10. Sub-Phase 7.5 --- RabbitMQ Consumer Infrastructure

## Objective

Implement the actual RabbitMQ consumer infrastructure compatible with
the Phase 6 topology.

## Flow

``` text
RabbitMQ
    ↓
Queue
    ↓
Consumer
    ↓
Deserialize
    ↓
Resolve Integration Event
    ↓
Invoke Handler
```

## Responsibilities

Define and verify:

-   Exchange binding.
-   Queue naming.
-   Routing key handling.
-   Manual ACK.
-   NACK behavior.
-   Prefetch.
-   Consumer concurrency.
-   Connection lifecycle.
-   Channel lifecycle.
-   Graceful shutdown.
-   Message cancellation behavior.

## Compatibility

The implementation must consume from the existing Phase 6 messaging
topology rather than creating a parallel messaging architecture.

## Exit Criteria

-   Consumer connects successfully.
-   Queues and bindings are correct.
-   Messages are received.
-   Handlers are invoked.
-   Graceful shutdown works.
-   ACK behavior is deterministic.

------------------------------------------------------------------------

# 11. Sub-Phase 7.6 --- ACK / Retry / Failure Handling

## Objective

Define deterministic behavior for every processing outcome.

## Successful Processing

``` text
Receive
 ↓
Process
 ↓
Commit
 ↓
ACK
```

## Duplicate Message

``` text
Receive
 ↓
Already Processed
 ↓
No Business Operation
 ↓
ACK
```

## Transient Failure

``` text
Receive
 ↓
Processing Failure
 ↓
No Successful Commit
 ↓
No ACK
 ↓
Retry / Redelivery
```

## Permanent Failure

Examples:

``` text
Invalid Message
Unsupported Event Version
Corrupt Payload
Permanent Business Failure
```

The final behavior must be explicitly defined and must not result in
infinite processing.

## Exit Criteria

-   ACK behavior is documented.
-   NACK behavior is documented.
-   Retryable failures are identified.
-   Permanent failures are identified.
-   No message is silently lost.

------------------------------------------------------------------------

# 12. Sub-Phase 7.7 --- Retry & Dead-Letter Strategy

## Objective

Implement consumer retry independently from Phase 6 Outbox publication
retry.

These are different mechanisms:

``` text
Phase 6
Outbox → RabbitMQ publication retry
```

versus:

``` text
Phase 7
RabbitMQ → Consumer processing retry
```

Do not merge their semantics unnecessarily.

## Retry Policy

Define:

-   Retryable errors.
-   Non-retryable errors.
-   Maximum attempts.
-   Backoff.
-   Redelivery behavior.
-   Terminal failure behavior.
-   Error metadata.

## Dead Letter

Expected conceptual flow:

``` text
Message
   ↓
Retry
   ↓
Retry
   ↓
Retry
   ↓
Maximum Attempts
   ↓
Dead Letter
```

Dead-lettered messages must remain diagnosable.

Relevant metadata should include:

``` text
MessageId
EventId
EventType
EventVersion
CorrelationId
CausationId
Consumer
Attempt
Error
OccurredAt
```

Avoid unnecessary sensitive payload logging.

## Exit Criteria

-   Retry policy is deterministic.
-   Infinite retry loops are impossible.
-   Permanent failures can reach a terminal state.
-   Dead-letter behavior is testable.
-   Failure metadata is sufficient for diagnosis.

------------------------------------------------------------------------

# 13. Sub-Phase 7.8 --- Correlation / Causation & Observability

## Objective

Continue the correlation/causation propagation established in Phase 6
into consumer processing.

Target flow:

``` text
HTTP
 ↓
CorrelationId
 ↓
Domain Event
 ↓
Outbox
 ↓
RabbitMQ
 ↓
Consumer
 ↓
Application
```

## Observable Fields

Consumer logs should support:

``` text
MessageId
EventId
EventType
Consumer
CorrelationId
CausationId
Attempt
Result
Duration
Failure
```

## Requirements

The system should allow operators/developers to answer:

-   Which message was processed?
-   Which consumer processed it?
-   Which request/workflow caused it?
-   Which event caused the current event?
-   How many attempts occurred?
-   Why did processing fail?
-   Was the message a duplicate?

Do not log sensitive payloads unnecessarily.

## Exit Criteria

-   CorrelationId is propagated.
-   CausationId is propagated.
-   Message processing is traceable.
-   Failures are observable.
-   Duplicate detection is observable.

------------------------------------------------------------------------

# 14. Sub-Phase 7.9 --- First Real Integration Consumer

## Objective

Implement the first real consumer using an existing integration event.

Do not invent a large business workflow for this phase.

The event must be selected after reviewing the existing Integration
Event contracts.

## Required Structure

``` text
Integration Event
       ↓
Consumer
       ↓
Application Contract
       ↓
Application Handler
       ↓
Domain
       ↓
Persistence
```

Not:

``` text
RabbitMQ
   ↓
DbContext
   ↓
Entity
```

## Scope Rule

The consumer must demonstrate:

-   Real RabbitMQ delivery.
-   Real application handling.
-   Real persistence.
-   Idempotency.
-   Duplicate handling.
-   Observability.

It must not introduce Saga orchestration.

## Exit Criteria

-   At least one real integration consumer is fully operational.
-   It uses the established architecture.
-   Duplicate delivery is safe.
-   Real SQL + RabbitMQ integration tests pass.

------------------------------------------------------------------------

# 15. Sub-Phase 7.10 --- Duplicate / Concurrency / Failure Tests

This sub-phase is mandatory.

## Test 1 --- Normal Processing

``` text
Message
 ↓
Consumer
 ↓
Business Effect
 ↓
ProcessedMessage
 ↓
ACK
```

Expected:

``` text
Success
```

------------------------------------------------------------------------

## Test 2 --- Duplicate Delivery

``` text
Message X
Message X
```

Expected:

``` text
Business Effect = 1
```

------------------------------------------------------------------------

## Test 3 --- Concurrent Duplicate Delivery

``` text
Consumer A → Message X
Consumer B → Message X
```

Expected:

``` text
Business Effect = 1
ProcessedMessage = 1
```

------------------------------------------------------------------------

## Test 4 --- Failure Before Commit

``` text
Process
 ↓
Failure
 ↓
Rollback
 ↓
Redelivery
 ↓
Successful Processing
```

Expected:

``` text
Final Business Effect = 1
```

------------------------------------------------------------------------

## Test 5 --- Commit Before ACK / Redelivery

Critical reliability test:

``` text
Business State
+
ProcessedMessage
 ↓
Commit
 ↓
Consumer fails before ACK
 ↓
RabbitMQ redelivers
 ↓
Duplicate detected
 ↓
ACK
```

Expected:

``` text
Business Effect = 1
```

------------------------------------------------------------------------

## Test 6 --- Retryable Failure

``` text
Message
 ↓
Transient Failure
 ↓
Retry
 ↓
Success
```

Expected:

``` text
Message eventually processed
```

------------------------------------------------------------------------

## Test 7 --- Permanent Failure

``` text
Message
 ↓
Permanent Failure
 ↓
Retry Limit
 ↓
DLQ
```

Expected:

``` text
No infinite processing loop
```

------------------------------------------------------------------------

## Test 8 --- Message Isolation

``` text
Message A → Fails
Message B → Succeeds
```

Expected:

``` text
B is not permanently blocked by A
```

------------------------------------------------------------------------

# 16. Sub-Phase 7.11 --- Architecture & Security Hardening

## Architecture Verification

Verify:

``` text
Domain
    X→ RabbitMQ

Domain
    X→ RabbitMQ.Client

Application
    X→ RabbitMQ.Client

Consumer Infrastructure
    ✓→ Application contracts

Module A
    X→ Module B Infrastructure

Consumer
    X→ Another Module DbContext
```

## Security Review

Review:

-   RabbitMQ credentials.
-   Secret configuration.
-   Environment configuration.
-   TLS readiness.
-   Deserialization safety.
-   Message size limits.
-   Malformed message handling.
-   Sensitive log data.
-   Queue permissions.

Do not expand the phase into unrelated infrastructure work unless an
issue is directly required for consumer reliability.

## Exit Criteria

-   Architecture tests pass.
-   No forbidden dependencies exist.
-   Security-sensitive configuration is not hardcoded.
-   Malformed messages fail safely.

------------------------------------------------------------------------

# 17. Sub-Phase 7.12 --- Performance & Operational Hardening

## RabbitMQ

Review:

-   Prefetch.
-   Consumer concurrency.
-   Connection reuse.
-   Channel lifecycle.
-   Backpressure.
-   Graceful shutdown.

## Database

Review:

-   ProcessedMessage indexes.
-   Unique constraints.
-   Lookup performance.
-   Transaction duration.
-   Cleanup/retention strategy.

## Consumer

Review:

-   Memory usage.
-   Deserialization overhead.
-   Handler duration.
-   Retry storms.
-   Shutdown behavior.

Avoid premature optimization.

The goal is to eliminate clear performance and operational defects
without introducing unnecessary complexity.

## Exit Criteria

-   No obvious N+1 behavior.
-   Idempotency lookup is indexed.
-   Consumer concurrency is bounded.
-   Shutdown is graceful.
-   Retry storms are controlled.

------------------------------------------------------------------------

# 18. Sub-Phase 7.13 --- Documentation Synchronization

After implementation and verification, synchronize:

``` text
docs/ARCHITECTURE.md
docs/PROJECT_CONTEXT.md
docs/adr/*
LOCAL_DEVELOPMENT.md
```

Only update documents where the implementation changes or confirms
documented architecture.

## Document

-   Consumer architecture.
-   Inbox / ProcessedMessage semantics.
-   Idempotency key/identity.
-   Transaction boundary.
-   ACK semantics.
-   Retry policy.
-   Dead-letter behavior.
-   Correlation/Causation propagation.
-   Consumer observability.
-   Operational requirements.
-   Phase 8 boundary.

## ADR Rule

Create/update an ADR only when a durable architectural decision is
introduced or changed.

Do not create ADRs for ordinary implementation details.

------------------------------------------------------------------------

# 19. Sub-Phase 7.14 --- Final Verification & Phase Completion

## Build

``` bash
dotnet build FluxCommerce.slnx
```

Required:

``` text
0 warnings
0 errors
```

## Test Suites

Run:

``` text
Unit Tests
Architecture Tests
Integration Tests
RabbitMQ Consumer Tests
Idempotency Tests
Concurrency Tests
Failure / Recovery Tests
Retry Tests
Dead-Letter Tests
```

Messaging-specific scenarios must use the real infrastructure where the
test is intended to prove infrastructure behavior:

``` text
Real SQL Server
+
Real RabbitMQ
```

## Final Verification

Verify:

``` text
Normal Processing
Duplicate Delivery
Concurrent Duplicate Delivery
Crash / Recovery
Retry
Permanent Failure
DLQ
ACK Semantics
Correlation / Causation
Architecture Boundaries
Security
Graceful Shutdown
```

## Completion Report

Create:

``` text
PHASE_7_COMPLETION_REPORT.md
```

The report must contain:

-   Implemented sub-phases.
-   Architectural decisions.
-   Files/components changed.
-   Tests executed.
-   Test counts/results.
-   Failure scenarios verified.
-   Known limitations.
-   Deferred work.
-   Confirmation that Phase 8 remains the next phase.

------------------------------------------------------------------------

# 20. Definition of Done

## Consumer Infrastructure

-   [ ] RabbitMQ consumer infrastructure implemented.
-   [ ] Consumer lifecycle is production-oriented.
-   [ ] Manual ACK behavior implemented.
-   [ ] Graceful shutdown implemented.
-   [ ] Queue/exchange bindings verified.
-   [ ] Event deserialization validated.
-   [ ] Invalid messages handled safely.

## Idempotency

-   [ ] Existing `ProcessedMessage` foundation reused appropriately.
-   [ ] Consumer idempotency persistence implemented.
-   [ ] Unique message/consumer protection exists.
-   [ ] Duplicate delivery does not repeat business effects.
-   [ ] Concurrent duplicate delivery is safe.
-   [ ] Idempotency lookup is efficient.

## Atomic Processing

-   [ ] Business state + ProcessedMessage are atomic where applicable.
-   [ ] Crash before commit is recoverable.
-   [ ] Crash after commit before ACK is safe.
-   [ ] ACK occurs only after successful processing/commit.
-   [ ] Duplicate redelivery is safely acknowledged.

## Retry / Failure

-   [ ] Retryable failures are defined.
-   [ ] Non-retryable failures are defined.
-   [ ] Retry policy is implemented.
-   [ ] Backoff is defined.
-   [ ] Maximum retry behavior is defined.
-   [ ] DLQ behavior is defined/implemented where required.
-   [ ] Failed messages remain diagnosable.

## Observability

-   [ ] MessageId is traceable.
-   [ ] EventId is traceable.
-   [ ] EventType is traceable.
-   [ ] CorrelationId is propagated.
-   [ ] CausationId is propagated.
-   [ ] Consumer attempts are observable.
-   [ ] Processing failures are observable.
-   [ ] Sensitive payloads are not unnecessarily logged.

## Architecture

-   [ ] Domain does not depend on RabbitMQ.
-   [ ] Application does not depend on RabbitMQ client.
-   [ ] Consumer does not bypass Application/Domain.
-   [ ] No cross-module DbContext access.
-   [ ] No distributed database transaction.
-   [ ] BuildingBlocks contain technical concepts only.
-   [ ] Module ownership remains intact.
-   [ ] No Saga implementation introduced.

## Testing

-   [ ] Unit tests pass.
-   [ ] Architecture tests pass.
-   [ ] Integration tests pass.
-   [ ] RabbitMQ consumer E2E tests pass.
-   [ ] Duplicate delivery test passes.
-   [ ] Concurrent duplicate test passes.
-   [ ] Crash/recovery tests pass.
-   [ ] Retry tests pass.
-   [ ] DLQ/permanent failure test passes.
-   [ ] Message isolation test passes.

## Documentation

-   [ ] `ARCHITECTURE.md` synchronized.
-   [ ] `PROJECT_CONTEXT.md` synchronized if required.
-   [ ] Relevant ADRs synchronized if architectural decisions changed.
-   [ ] `LOCAL_DEVELOPMENT.md` updated if operational configuration
    changed.
-   [ ] `PHASE_7_COMPLETION_REPORT.md` created.
-   [ ] Phase 8 remains explicitly deferred.

------------------------------------------------------------------------

# 21. Execution Order

``` text
7.1  Baseline & Consumer Architecture Audit
        ↓
7.2  Consumer Contract & Processing Model
        ↓
7.3  Inbox / ProcessedMessage Persistence
        ↓
7.4  Atomic Idempotent Processing
        ↓
7.5  RabbitMQ Consumer Infrastructure
        ↓
7.6  ACK / Retry / Failure Handling
        ↓
7.7  Retry & Dead-Letter Strategy
        ↓
7.8  Correlation / Causation & Observability
        ↓
7.9  First Real Integration Consumer
        ↓
7.10 Duplicate / Concurrency / Failure Tests
        ↓
7.11 Architecture & Security Hardening
        ↓
7.12 Performance & Operational Hardening
        ↓
7.13 Documentation Synchronization
        ↓
7.14 Final Verification & Completion
```

------------------------------------------------------------------------

# 22. Final Target Architecture

After Phase 7:

``` text
                    MODULE A
                       │
                       ▼
                 Domain Event
                       │
                       ▼
                    Outbox
                       │
                  SAME TX
                       │
                       ▼
                    Commit
                       │
                       ▼
                 Dispatcher
                       │
                       ▼
                   RabbitMQ
                       │
                       ▼
                ┌──────────────┐
                │   Consumer   │
                └──────┬───────┘
                       │
                       ▼
                Idempotency Check
                       │
             ┌─────────┴─────────┐
             │                   │
         Duplicate              New
             │                   │
            ACK                   ▼
                       Application Handler
                               │
                               ▼
                           Domain Logic
                               │
                     ┌─────────┴─────────┐
                     │                   │
              Business State      ProcessedMessage
                     │                   │
                     └─────────┬─────────┘
                               │
                               ▼
                             COMMIT
                               │
                               ▼
                              ACK
                               │
                               ▼
                         Phase 8 Saga
```

------------------------------------------------------------------------

# 23. Phase 7 Success Criteria

Phase 7 is considered complete only when the system can demonstrate:

``` text
Reliable Publication
        +
At-Least-Once Delivery
        +
Idempotent Consumption
        +
Concurrency Safety
        +
Crash Recovery
        +
Retry Handling
        +
Dead-Letter Handling
        +
Observability
        ↓
Reliable Messaging Foundation
        ↓
Phase 8 — Saga Orchestration
```

The primary proof is not merely that a consumer can receive a RabbitMQ
message.

The primary proof is:

> **The same message can be delivered multiple times, including
> concurrent delivery and redelivery after a consumer failure, while
> producing exactly one intended business side effect.**
