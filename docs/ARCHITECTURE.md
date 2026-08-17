# Enterprise Commerce & Order Management Platform
# Architecture Specification

# 1. Architectural Style

The system follows a Modular Monolith architecture based on:

- Clean Architecture principles
- Domain-Driven Design principles
- Explicit module boundaries
- Module-owned business state
- Module-owned persistence boundaries
- Asynchronous messaging where required
- Outbox Pattern for reliable event publication
- Idempotent Consumers
- Saga Orchestration for distributed business workflows
- Strong observability

The system starts as a Modular Monolith.

It is intentionally NOT implemented as Microservices from the beginning.

The architecture must allow future extraction of individual modules into independently deployable services if the scale or organizational requirements justify it.

The migration path is conceptually:

    Modular Monolith
          |
          v
    Strong Module Boundaries
          |
          v
    Independent Persistence Boundaries
          |
          v
    Asynchronous Integration Contracts
          |
          v
    Optional Service Extraction

The architecture must not introduce distributed-system complexity before it provides real value.

---

# 2. High-Level Architecture

The system is organized into:

    API
      |
      v
    Modules
      |
      +-------------------------------+
      |                               |
      v                               v
    BuildingBlocks              Infrastructure
                                      |
                                      +--> SQL Server
                                      +--> Redis
                                      +--> RabbitMQ
                                      +--> External Services

Business modules:

    Catalog
    Cart
    Orders
    Inventory
    Payments

Platform capabilities:

    Identity
    Observability
    Cross-Cutting Infrastructure

Identity is a platform capability, not a core commerce bounded context.

---

# 3. Module Structure

Each business module should follow this conceptual structure:

    Module
    |
    +-- Domain
    |
    +-- Application
    |
    +-- Infrastructure
    |
    +-- Contracts

## Domain

Contains:

- Entities
- Aggregate Roots
- Value Objects
- Domain Events
- Domain Services where justified
- Domain Rules
- Business Invariants

Domain must contain business rules.

Domain must not depend on:

- ASP.NET Core
- EF Core implementation details
- RabbitMQ
- Redis
- External APIs
- Infrastructure concerns

---

## Application

Contains:

- Commands
- Queries
- Handlers
- Use Cases
- Application Services
- DTOs
- Validation
- Ports / Interfaces required by application logic

Application orchestrates use cases.

Application should depend on Domain.

Application must not directly contain infrastructure implementations.

---

## Infrastructure

Contains implementations for:

- Persistence
- EF Core
- External integrations
- Messaging implementations
- Caching implementations
- File storage
- Other infrastructure concerns

Infrastructure implements abstractions defined by Domain/Application where appropriate.

Infrastructure must not become the location for business rules.

---

## Contracts

Contains public contracts exposed by the module.

Examples:

- Integration Event contracts
- Public DTO contracts
- Inter-module contracts
- Public interfaces

Contracts must not expose internal implementation details.

Integration Event contracts derive from the canonical contract-only `BuildingBlocks.Messaging` metadata
base (`IIntegrationEvent` / `IntegrationEvent` / `EventVersion`) and carry a stable `EventType`
identifier. This is the only Messaging reference a module is allowed: the RabbitMQ client and the
Outbox abstraction stay out of Contracts, and Domain, Application and Infrastructure stay free of
`BuildingBlocks.Messaging` and `RabbitMQ.Client` (see `docs/adr/007-integration-event-contract-placement.md`).

---

# 4. BuildingBlocks

BuildingBlocks contains genuinely cross-cutting technical primitives.

Possible areas:

    BuildingBlocks
    |
    +-- Domain
    +-- Application
    +-- Infrastructure
    +-- Observability
    +-- Messaging
    +-- Persistence
    +-- Web
    +-- Localization

Only concepts that are truly shared across multiple modules should be placed here.

Do not place business-specific logic in BuildingBlocks.

Do not use BuildingBlocks as a dumping ground.

Do not create shared abstractions merely because two modules currently have similar code.

Duplication is sometimes preferable to incorrect coupling.

---

# 5. Dependency Rules

The dependency direction is:

    API
      |
      v
    Application
      |
      v
    Domain

Infrastructure implements abstractions required by Application and Domain.

Conceptually:

    API
      |
      v
    Application
      |
      v
    Domain
      ^
      |
    Infrastructure

Infrastructure must not force Domain to depend on infrastructure technology.

---

# 6. Module Dependency Rules

Modules are isolated.

Forbidden:

    Catalog
       |
       X
       +--> Orders.Infrastructure

    Orders
       |
       X
       +--> Inventory.Infrastructure

    Inventory
       |
       X
       +--> Payments.Infrastructure

A module must never access another module's:

- DbContext
- Repository
- Internal Entity
- Internal Application Service
- Internal Infrastructure implementation

A module must not directly modify another module's state.

---

# 7. Cross-Module Communication

Cross-module communication must use explicit contracts.

Preferred communication mechanisms:

## Synchronous Communication

Use explicit application-level contracts when immediate consistency or request-response behavior is required.

## Asynchronous Communication

Use integration events through the messaging infrastructure when:

- Loose coupling is beneficial
- Eventual consistency is acceptable
- A workflow spans multiple modules
- Reliability requires asynchronous processing

The architecture uses RabbitMQ as the asynchronous messaging infrastructure.

---

# 8. Domain Events vs Integration Events

Domain Events represent internal business events within a module.

Example:

    OrderPlaced

Integration Events represent events published for other modules or external consumers.

Example:

    OrderPlacedIntegrationEvent

Do not automatically treat every Domain Event as an Integration Event.

The module should explicitly decide which events cross its boundary.

Conceptually:

    Domain Operation
          |
          v
    Domain Event
          |
          v
    Application / Outbox Decision
          |
          v
    Integration Event
          |
          v
    Outbox
          |
          v
    RabbitMQ

---

# 9. Persistence Architecture

The persistence model follows Module-per-DbContext.

Conceptually:

    SQL Server
        |
        +-- CatalogDbContext
        |
        +-- OrdersDbContext
        |
        +-- InventoryDbContext
        |
        +-- PaymentsDbContext
        |
        +-- IdentityDbContext

Redis-backed modules (currently Cart) own no SQL DbContext; their state lives in Redis.

Each DbContext belongs to one module.

No module may access another module's DbContext.

No cross-module EF Core relationships.

No cross-module foreign keys.

No shared business entity tables across module boundaries.

If modules need information from another module, they use:

- Explicit contracts
- Queries
- Cached projections
- Integration events
- Other approved communication mechanisms

The exact mechanism depends on consistency requirements.

---

# 10. Database Strategy

The initial deployment may use one SQL Server environment.

However, the logical ownership boundaries must remain explicit.

The architecture must allow future evolution toward:

    One SQL Server
          |
          v
    Multiple Module Databases
          |
          v
    Independent Service Databases

Business logic must never rely on cross-module database access.

This is critical for future service extraction.

---

# 11. Transaction Boundaries

A transaction belongs to one module's business operation.

A transaction should normally contain:

    Business State Change
          +
    Outbox Record

The system must avoid distributed database transactions across modules.

Cross-module workflows should use:

- Integration Events
- Saga Orchestration
- Compensation

rather than distributed transactions.

---

# 12. Outbox Pattern

The Outbox Pattern guarantees reliable publication of integration events.

Conceptually:

    Module Transaction
       |
       +--> Update Business State
       |
       +--> Insert Outbox Message
       |
       +--> Commit
               |
               v
           Outbox Dispatcher
               |
               v
            RabbitMQ

The Outbox Message must contain enough metadata for reliable processing and observability.

Recommended metadata includes:

- Message ID
- Event ID
- Event Type
- Payload
- OccurredAtUtc
- CorrelationId
- CausationId
- CreatedAtUtc
- ProcessedAtUtc
- Retry information
- Error information where required

The dispatcher must support retry behavior.

Failed messages must not silently disappear.

---

# 13. Messaging Semantics

The messaging system follows At-Least-Once Delivery semantics.

Exactly-Once Delivery must not be assumed.

Therefore:

    Consumer
       |
       v
    Idempotent Processing

Every consumer must safely handle duplicate delivery.

Messages must have stable unique identifiers.

Consumers must track processed messages or use another reliable idempotency strategy.

---

# 14. RabbitMQ

RabbitMQ is the intended asynchronous messaging infrastructure.

RabbitMQ is used for:

- Integration Events
- Asynchronous communication
- Background processing
- Event-driven workflows
- Saga coordination where appropriate

RabbitMQ must not become a replacement for direct in-process method calls inside a single module.

Use asynchronous messaging where it provides architectural value.

---

## 14.1 Consumer Contract & Processing Model

Integration event handlers are transport-agnostic contracts owned by the consuming module. A handler
implements `IIntegrationEventHandler<TEvent>` (in `BuildingBlocks.Messaging`) and receives the
deserialized contract plus an `IntegrationEventEnvelope` carrying the canonical processing context
(EventId, EventType, EventVersion, CorrelationId, CausationId, OccurredAtUtc) — never a broker message.

The handler **implementation** lives in the consuming module's `Infrastructure` layer (beside the module
DbContext and `IIdempotentProcessor` it shares), so consumer transport stays in Infrastructure and
Domain/Application never reference `IIntegrationEventHandler` or the Messaging consumer contracts.

Handlers are resolved through an explicit consumer registry that maps a wire `EventType` to a contract
type and a DI-registered handler type (no reflection-based activation), mirroring the Phase 6
publisher's switch-on-Type deserialization. An unknown event type resolves to `null` and is skipped.

### 14.1.1 ProcessedMessage Persistence (Inbox)

The idempotency identity is `EventId + HandlerName`, composed as
`ProcessedMessage.Create(eventId, handler.HandlerName)` (see ADR-008). Processed-message persistence is
**module-local**: the canonical `ProcessedMessage` record (a shared `BuildingBlocks.Idempotency`
contract, following the `OutboxMessage` precedent) is mapped into each *consuming* module's DbContext —
there is no global/shared idempotency database or DbContext, and one module never touches another
module's `ProcessedMessages`. The **Inventory module** owns the first `ProcessedMessages` boundary
(established in Phase 7.3 and exercised by the Phase 7.5–7.10 consumer integration tests); the
**Orders module** maps the same canonical record into its own `OrdersDbContext` for the Phase 7.9 Order
Summary projection consumer — the first real production consumer. Each module's table is strictly
module-local:

```text
InventoryDbContext
    ├── Inventory State
    ├── OutboxMessages
    └── ProcessedMessages     ← UNIQUE(Key, Handler)
```

The composite `(Key, Handler)` is the table's primary key, so the SQL unique constraint is the final
concurrency protection: `(E1, H1)` can be claimed once, `(E1, H2)` is an independent consumer, and a
duplicate `(E1, H1)` race is resolved by the database and reported as already-processed (never an
application failure). The record stays lightweight: `Key` (EventId, 36 chars), `Handler` (128 chars),
`ProcessedAtUtc`.

The SQL-backed implementations of the canonical idempotency contracts live in the consuming module's
Infrastructure (`FluxCommerce.Modules.Inventory.Infrastructure.Idempotency`): `IIdempotencyStore`
(`GetAsync`/`AddAsync`, the check-then-act surface) and `IDuplicateDetector` (`TryClaimAsync`
atomic claim + `IsProcessedAsync`). `GetAsync` is for querying only and is not sufficient concurrency
protection.

**Retention.** Processed-message records are currently retained indefinitely and there is deliberately
NO automatic cleanup/retention job. Under at-least-once delivery the broker may redeliver a message at
any time with no expiry bound, so deleting the idempotency record after a time window would reopen the
duplicate window: a late redelivery would no longer be recognized as a duplicate and would re-execute
the business operation (e.g., a duplicate inventory reservation or a duplicate projection). A
controlled purge is only safe when the workflow has a confirmed end-of-life — a point after which no
further deliveries for the event can occur (for example a completed saga in Phase 8+). Until such a
bound exists, indefinite retention is the safe default and any future cleanup must be gated on it. The
composite `(Key, Handler)` primary key is the only index needed by the current access paths (single-row
PK seeks); no additional indexes are justified.

### 14.1.2 Atomic Idempotent Processing

Business state changes and processed-message recording are **atomic** whenever they share the same
persistence boundary. The `IIdempotentProcessor` contract (in `BuildingBlocks.Idempotency`) is the
transport-agnostic processing boundary: it claims a `ProcessedMessage` and runs the business operation
(`Func<Task>`) inside ONE module-local transaction, then commits once. A successful processing commits
Business State + ProcessedMessage together; a business failure rolls both back (the message stays
retryable); a known duplicate is detected up front and acknowledged WITHOUT re-executing the business
operation; a concurrent duplicate race is resolved by the composite `(Key, Handler)` primary key — the
losing attempt rolls back, re-reads the winner and reports `AlreadyProcessed` (exactly one business
effect, exactly one processed message). The Inventory implementation
(`InventoryIdempotentProcessor`) runs the transaction through the retrying execution strategy
(`InventoryStore.TryReserveAsync` precedent) and reuses `IDuplicateDetector.IsProcessedAsync` and
`IIdempotencyStore.GetAsync` for the fast-path check and winner re-read. The consumer infrastructure
(`IntegrationEventMessageProcessor`) invokes any application handler through this boundary — always
resolving each module's own processor declared on the consumer's descriptor (§14.1.4) — so no second
idempotency abstraction exists.

```text
IIdempotentProcessor.TryProcessAsync(message, businessOperation)
    │
    ├─ already processed? ──► AlreadyProcessed (business logic NOT executed)
    │
    ▼
begin transaction (execution strategy)
    │
    ▼
Business Operation  +  ProcessedMessage  ──►  COMMIT (atomic)
```

### 14.1.3 Background Identity

Consumer/background scopes resolve a non-request `ICurrentUser` (unauthenticated), matching the test
`AnonymousCurrentUser` precedent — no request identity is available on the broker path (ADR-008).

### 14.1.4 RabbitMQ Consumer Infrastructure

The consumer infrastructure lives in `BuildingBlocks.Infrastructure.Consuming` and consumes from the
SAME Phase 6 topology (the durable topic exchange) — it does not create a parallel messaging
architecture. `RabbitMQConsumerBackgroundService` (an `IHostedService`) declares the exchange and the
configured queue, binds the queue to each registered event type's routing key, applies per-channel
prefetch, and starts `RabbitMQConsumerOptions.Concurrency` consumers against the queue (the broker
round-robins deliveries across them, giving bounded in-flight concurrency). Every delivery is processed
in its own DI scope:

```text
delivery ─► resolve EventType (header, fallback AMQP Type)
              ├─ unregistered EventType ──► ACK + skip (ADR-008, never poison)
              ├─ invalid metadata / corrupt payload / metadata mismatch ──► REJECT ──► DLQ (terminal)
              ▼
         build envelope from transport headers (authoritative metadata)
              ▼
         deserialize contract (switch-on-Type registry, no reflection activation)
              ▼
         retry loop (fresh DI scope per attempt; attempt = 1 + broker x-death count)
              ▼
          IIdempotentProcessor.TryProcessAsync(ProcessedMessage.Create(EventId, HandlerName),
                                               () => handler.HandleAsync(...))
              ├─ fresh ──► ACK
              ├─ duplicate ──► ACK (business logic NOT re-executed)
              └─ failure ──► classify (IIntegrationEventFailureClassifier)
                                ├─ permanent (NonRetryableExceptionTypes) ──► enrich + publish DLX ──► ACK
                                ├─ retryable, attempts < MaxAttempts ──► in-process backoff, retry
                                └─ retryable, attempts exhausted ──► enrich + publish DLX ──► ACK
                                      (if the enriched DLX publish is not confirmed ──► DEAD-LETTER ──► Nack(requeue:false) ──► broker DLQ verbatim)
```

Phase 7.6 retry/failure model. Retry is IN-PROCESS and bounded — it does not requeue to the broker, and
it is deliberately separate from the Phase 6 outbox publication retry (`OutboxDispatchOptions`): the
consumer retry handles processing a delivery; the outbox retry handles publishing an event. The policy
lives in `RabbitMQConsumerOptions.Retry` (`ConsumerRetryOptions`): `MaxAttempts` total processing
attempts (default 3; 1 disables retries), `BackoffType` (Fixed / Linear / Exponential),
`InitialDelay` (default 1s), `DelayMultiplier` (default 2.0) and `MaxDelay` (default 30s) via the pure
`ConsumerRetryPolicy` math. The attempt counter is seeded from the broker's `x-death` redelivery count —
the MAXIMUM count across `x-death` entries (the message lives in exactly one queue at a time, so summing
across hops for different dead-letter reasons/exchanges would over-count a single message) — so attempts
stay bounded even across crashes/requeues/dead-letter hops. Every attempt runs in a FRESH DI scope — the
scoped `InventoryDbContext`/`IIdempotentProcessor` is only safe to use once, because after a business
failure rolls the transaction back EF Core considers the context poisoned. Failures are classified by
the singleton `IIntegrationEventFailureClassifier`: an exception whose type (or any inner exception) is
listed in `ConsumerRetryOptions.NonRetryableExceptionTypes` is permanent and dead-lettered immediately;
everything else is retryable. A failed attempt never creates a `ProcessedMessage` (the transaction
rolls back), and a message is only ACKed after the business operation AND the idempotency record have
committed.

Phase 7.7 terminal dead-lettering. Terminal failures are never dropped and never retried forever, and
the dead-letter queue is a recoverable, diagnosable record. The main queue is declared with an
`x-dead-letter-exchange` pointing at a dedicated dead-letter topology — a durable topic exchange
(`{QueueName}.dlx` by default, configurable via `DeadLetterExchangeName`) bound with `#` to a durable
dead-letter queue (`{QueueName}.dlq` by default, configurable via `DeadLetterQueueName`). Two distinct
terminal paths keep the original delivery (body + headers) in the DLQ in every case:

- **Reject (malformed/invalid deliveries)** is applied verbatim: Nack(requeue:false) and the broker
  dead-letters the ORIGINAL message unchanged (only adding the standard `x-death`/`x-first-death-*`
  headers). No metadata is injected — the poison message is preserved byte-for-byte for diagnosis.
- **Permanent failure / exhausted retries** are ENRICHED: the processor publishes a copy of the
  original body and headers plus failure metadata — `x-dead-letter-reason`
  (`processing-failure-permanent` | `processing-failure-exhausted`), `x-dead-lettered-at`,
  `x-consumer` (stable handler name), `x-attempt`, `x-error-type`, `x-error-message` — to the DLX via
  the `IIntegrationEventDeadLetterer` (a singleton that mirrors the publisher: lazy confirm-enabled
  channel, `ThrottlingRateLimiter(1)`, declares the durable topic DLX, returns success only on broker
  confirm). When the enriched copy is confirmed the ORIGINAL is ACKed (exactly one DLQ record); when the
  publish cannot be confirmed the processor returns DeadLetter and the host Nacks the original
  (requeue:false) so the broker still dead-letters it verbatim. A terminal failure therefore always
  ends in the DLQ exactly once, never silently, never back in the processing loop.

Phase 7.8 consumer observability. Every consumer log is structured with a CONSISTENT field set —
`MessageId`, `EventId`, `EventType`, `EventVersion`, `CorrelationId`, `CausationId`, `Consumer`,
`Attempt`, `Result`, `DurationMs`, `FailureClassification`, `DeadLetterReason` — written as explicit
template fields on each log call (no `BeginScope`, so the fields are directly assertable and survive
any sink). `Result` is one of the stable values `processed` | `duplicate` | `rejected` |
`dead-lettered` | `retrying` | `skipped`; a per-attempt `Stopwatch` logs `DurationMs` for every
outcome, and the consumer is logged via the handler's stable `HandlerName` on every path (no more
`HandlerType.Name` in catch logs). Business payloads are never logged. Correlation/causation ride the
envelope (`IntegrationEventEnvelope`) unchanged from the delivery: the incoming `CorrelationId` is
preserved and the consumed `EventId` becomes the causation. A consumer that PRODUCES an integration
event must stamp the produced event's metadata from `envelope.ProducedEventCorrelationId` (the incoming
`CorrelationId`) and `envelope.ProducedEventCausationId` (the consumed `EventId`) — no new metadata
model, no consumer context service. Missing metadata is safe: an absent/invalid `CorrelationId` header
yields `Guid.Empty` and an absent `CausationId` yields `null` in the envelope and logs; malformed
deliveries that never build an envelope log only the transport identifiers (`EventType`, `MessageId`).

`RabbitMQConsumerOptions.Validate()` runs on host startup and fails fast on misconfiguration: queue and
exchange required, PrefetchCount/Concurrency ≥ 1, `ConsumerRetryOptions.Validate()` (MaxAttempts ≥ 1,
defined BackoffType, InitialDelay ≥ 0, DelayMultiplier ≥ 1.0, MaxDelay ≥ 0 and ≥ InitialDelay,
NonRetryableExceptionTypes non-null), and the resolved dead-letter topology must not alias the main
topology (a DLX equal to the main exchange or a DLQ equal to the main queue would route terminal
failures straight back into processing — an endless retry). Cancellation (shutdown) propagates through
the retry loop and leaves the delivery unacknowledged so the broker redelivers it once the channel
closes (at-least-once; the redelivery is then counted via `x-death`).

Consumer scopes invoke handlers through the `IIdempotentProcessor` boundary (§14.1.2) — no second
idempotency abstraction. Shutdown cancels in-flight processing (leaving uncommitted deliveries
unacknowledged so the broker redelivers them, at-least-once), cancels the consumers, awaits in-flight
work, and then disposes the channels.

Wiring is explicit DI: `AddRabbitMQConsumer(exchangeName, queueName, configure)` registers the consumer
host, the singleton `IIntegrationEventFailureClassifier`, the singleton `IIntegrationEventDeadLetterer`
(factory reading `IOptions<RabbitMQConsumerOptions>` for the resolved DLX name), plus
`RabbitMQConsumerOptions` (QueueName, ExchangeName, PrefetchCount, Concurrency, Durable, AutoDelete,
Retry, DeadLetterExchangeName, DeadLetterQueueName — with `ResolvedDeadLetterExchangeName`/
`ResolvedDeadLetterQueueName` deriving `{QueueName}.dlx`/`{QueueName}.dlq` when not configured), and
`AddIntegrationEventConsumer<TEvent, THandler, TProcessor>()` registers each handler (scoped), its
module-owned `IIdempotentProcessor` implementation (scoped), and an
`IntegrationEventConsumerDescriptor` carrying `EventType` (via the contract's stable `public const string
EventType`), the contract type, the handler type, and the processor type. The message processor resolves
BOTH the handler and the processor by the concrete types declared on the descriptor — never by the
container-wide `IIdempotentProcessor` interface, which every module registers and which would resolve
last-registration-wins across modules. Each module registers its concrete
`IIdempotencyStore`/`IDuplicateDetector`/`IIdempotentProcessor` and forwards the shared interfaces to
them for single-module internal use; a consumer therefore always executes through its owning module's
processor and DbContext (Orders consumers through `OrdersIdempotentProcessor`/`OrdersDbContext`,
Inventory consumers through `InventoryIdempotentProcessor`/`InventoryDbContext`). Note that
`BasicDeliverEventArgs.CancellationToken` is never
cancelled by RabbitMQ.Client 7.x, so the consumer drives cancellation from its own shutdown token only.

---

# 15. Saga Architecture

The system will eventually implement distributed business workflows using Saga Orchestration.

The Saga Orchestrator coordinates the workflow.

The Saga does NOT own:

- Order state
- Inventory state
- Payment state

Each module owns its own state.

Conceptually:

    Order
      |
      v
    Saga Orchestrator
      |
      +--> Inventory Reservation
      |
      +--> Payment
      |
      +--> Order Confirmation

If a later step fails:

    Failure
      |
      v
    Compensation
      |
      +--> Release Inventory
      +--> Refund Payment
      +--> Cancel / Compensate Order

Saga orchestration belongs to later implementation phases.

Phase 0 only establishes the architectural foundation required to support it.

---

# 16. Idempotency

Idempotency is required at multiple levels.

Potential areas include:

- API requests
- Message consumers
- Outbox dispatch
- Payment webhooks
- Saga commands

The exact strategy depends on the operation.

Do not assume one universal idempotency mechanism for every workflow.

Idempotency keys and event/message IDs must be explicit where required.

---

# 17. Redis

Redis is an infrastructure capability.

Potential future uses include:

- Cart state
- Caching
- Distributed locks where justified
- Rate limiting
- Temporary state

Redis must not become a source of truth for business state unless explicitly designed for a specific bounded context.

Phase 0 establishes connectivity and infrastructure readiness only.

---

# 18. API Layer

The API layer is responsible for:

- HTTP transport
- Authentication integration
- Authorization integration
- Request validation
- Mapping HTTP requests to application use cases
- Mapping application results to HTTP responses
- ProblemDetails
- API versioning
- Correlation IDs

Controllers / Endpoints must remain thin.

Business rules must not live inside controllers.

---

# 19. Error Handling

The system distinguishes between:

## Expected Business Errors

Examples:

- Validation failure
- Business rule violation
- Resource not found
- Conflict

These should be represented consistently using Result / Error abstractions where appropriate.

`Error.Code` is the stable machine-readable key of an error; the human-readable display text is localized at the presentation boundary (see §26). Status codes, `type` URIs and the ProblemDetails shape do not change with the language.

## Unexpected Technical Failures

Examples:

- Database failure
- Network failure
- Infrastructure outage
- Unexpected exceptions

These should be handled centrally through global exception handling and mapped to appropriate ProblemDetails responses.

Do not use exceptions as the normal control flow for expected business failures.

Do not use Result<T> as a replacement for every exception.

---

# 20. Observability

The system uses:

- Structured Logging
- Correlation IDs
- Trace IDs
- OpenTelemetry
- Metrics
- Health Checks

Every request should be traceable.

Every asynchronous message should preserve correlation information where possible.

Consumers (Phase 7.8) preserve the business-operation trace: the incoming `CorrelationId` is carried
into the processing envelope unchanged and the consumed event's `EventId` becomes the `CausationId`;
an event produced by a consumer must reuse `envelope.ProducedEventCorrelationId` /
`envelope.ProducedEventCausationId` so publisher → consumer → next publisher stays linked. Consumer
outcomes are logged with a consistent structured field set (MessageId, EventId, EventType,
EventVersion, CorrelationId, CausationId, Consumer, Attempt, Result, DurationMs,
FailureClassification, DeadLetterReason); payloads are never logged.

A future distributed workflow should be traceable across:

    API
      |
    Module
      |
    Database
      |
    Outbox
      |
    RabbitMQ
      |
    Consumer
      |
    Another Module

---

# 21. Configuration

Configuration must use:

- ASP.NET Core Configuration
- Options Pattern
- Environment Variables
- Environment-specific configuration

Secrets must never be committed to source control.

Examples:

- Database credentials
- RabbitMQ credentials
- Redis credentials
- JWT secrets
- Stripe secrets

Development-only configuration may use local secret management mechanisms.

---

# 22. Testing Strategy

The architecture should support:

## Unit Tests

For:

- Domain rules
- Value Objects
- Aggregate behavior
- Application logic

## Integration Tests

For:

- EF Core
- SQL Server
- Redis
- RabbitMQ
- External integrations

## Architecture Tests

For:

- Dependency rules
- Module isolation
- Layer boundaries
- BuildingBlocks restrictions

## End-to-End Tests

For complete business workflows introduced in later phases.

Testing must be introduced progressively.

---

# 23. Architecture Enforcement

The architecture is considered successful only if it is enforced.

Documentation explains the rules.

Architecture tests enforce critical rules.

Code review protects the boundaries.

The combination of:

    Documentation
        +
    Architecture Tests
        +
    Automated Tests
        +
    Code Review

must prevent architectural erosion.

---

# 24. Current Implementation Priority

The implementation order is:

    Phase 0
    Architecture Foundation
          |
          v
    Identity / Platform Capabilities
          |
          v
    Catalog
          |
          v
    Cart
          |
          v
    Orders
          |
          v
    Inventory
          |
          v
    Messaging
          |
          v
    Saga
          |
          v
    Payments
          |
          v
    Compensation / Reliability Hardening

The exact phase sequencing must always follow `PROJECT_CONTEXT.md`.

---

# 25. Core Principle

The most important architectural principle is:

    Each module owns its business state.

    Each module protects its invariants.

    Each module exposes explicit contracts.

    Modules do not directly access each other's internals.

    Cross-module workflows use explicit communication.

    Reliable asynchronous communication uses Outbox + At-Least-Once Delivery + Idempotent Consumers.

    Distributed workflows use Saga Orchestration + Compensation.

    The system starts as a Modular Monolith and evolves toward distributed deployment only when justified.

The architecture must optimize for correctness, maintainability, reliability, and future evolution rather than premature distribution.

---

# 26. Localization (Internationalization) Architecture

Implemented in **Phase 1.6** (Internationalization Foundation; see `docs/completed/PHASE_1_COMPLETION_REPORT.md` §14 for the full subphase breakdown and verification results).

## 26.1 Scope

Localization covers **user-facing strings only**: validation messages, ProblemDetails (titles/details), Identity/auth error texts, transactional email copy, and API documentation (Swagger). Multilingual **business data** (Product/Category/Brand translations, SEO, translation management) is intentionally out of scope and remains single-language.

## 26.2 BuildingBlock

`FluxCommerce.BuildingBlocks.Localization` hosts the framework-independent localization abstractions and the shared resource repository:

- `ILocalizer` (indexer + `Get(key, args)`) and `ICurrentCulture` — the only localization types modules may depend on.
- `SharedLocalizer` / `CurrentCulture` — the ASP.NET Core-backed implementations (`IStringLocalizer<SharedResources>`, `CultureInfo.CurrentUICulture`). `Microsoft.Extensions.Localization` is referenced **only inside this block**; no module layer references ASP.NET Core localization types directly.
- `SharedResources` — the shared resource set (neutral `SharedResources.resx` + `fr`/`de`/`es`/`ar` satellites, 75 keys each, full satellite parity).

## 26.3 Culture Registry & Resolution

- Single compile-time culture registry: `LocalizationConfiguration.SupportedCultures` (`en`, `fr`, `de`, `es`, `ar`) and `DefaultCulture` (`en`) in the API host — the one source of truth used by both the middleware and Swagger.
- Request culture is resolved by `RequestLocalizationMiddleware` (registered immediately after the exception handler) using providers in order: **query string** (`?culture=`), **cookie**, then **`Accept-Language`**; the default culture applies when no provider yields a supported culture.
- `Content-Language` response header is emitted on every response (`ApplyCurrentCultureToResponseHeaders`).
- A storage-backed per-user override (`User.PreferredLanguage` as a higher-priority provider) is a documented future evolution, deliberately not yet implemented.

## 26.4 Resource Organization

- One shared resource tier serves cross-cutting keys (Validation, Errors, Auth, Common, Success, Email). Per-module resource sets are a documented convention for the future, not yet created.
- Resource sets resolve purely by type namespace (`FluxCommerce.BuildingBlocks.Localization.Resources.SharedResources`); `ResourcesPath` is not set (a `ResourcesPath` would double-prefix the resolved name and break lookups).
- Missing translations fall back to the neutral (English) resource; a missing key returns the key itself (visible in development).

## 26.5 Error Localization Boundary

- `Error.Code` is the stable machine-readable key and never changes with language; `Error.Description` remains the English fallback.
- `IErrorCodeLocalizer` (BuildingBlocks.Web abstraction) maps a code to a resource key at the **presentation boundary**; the API host implements it (`ErrorCodeLocalizer`) with the English description as fallback.
- `ApiControllerBase` and the global `ExceptionHandler` localize ProblemDetails `Title`/`Detail` via `ILocalizer`; status codes, `type` URIs and the ProblemDetails shape are unchanged.

## 26.6 Localized Surfaces

| Surface | Mechanism |
|---------|-----------|
| Validation messages | All Identity command validators and rules resolve messages through `ILocalizer` |
| ProblemDetails | `Title`/`Detail` via `ILocalizer` + `IErrorCodeLocalizer` |
| Identity/auth errors | `Authentication.*` / `Errors.*` keys via the code → key mapping |
| Transactional emails | Verification + Password Reset templates composed from `Email.*` keys; HTML `lang` = resolved UI culture |
| API documentation | Swagger `Accept-Language` header on every operation (enum of supported cultures, default `en`) |

## 26.7 Adding a Culture

1. Add the culture to `LocalizationConfiguration.SupportedCultures`.
2. Add the satellite resource file (`SharedResources.<culture>.resx`) with the full key set.
3. Update the supported-culture documentation.

The abstractions-only rule keeps this mechanical: module code never changes.