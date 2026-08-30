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

### 9.1 Payments Bounded Context (Phase 9)

Payments owns `Payment` state exclusively (`PaymentsDbContext` owns `Payments`, `ProcessedWebhookEvents`, `OutboxMessages`). No cross-module `DbContext` access.

**Domain model (`Payments.Domain`):**
```
Payment : AggregateRoot<Guid>
├── Id/PaymentId (PK → PaymentId column)
├── OrderId (unique, UX_Payments_OrderId)
├── CustomerId (owner, for [Authorize] enforcement)
├── Amount (decimal 18,6) + Currency (ISO3, 3)
├── Status (int → PaymentStatus: Pending=0, Processing, Succeeded, Failed, Cancelled) via field `_status`
├── ProviderPaymentIntentId (256, unique filtered [IS NOT NULL])
├── Provider (128)
├── Version (bigint, IsConcurrencyToken) + CreatedAtUtc/UpdatedAtUtc
```
Invariants (`PaymentErrors`): amount>0, currency required, `Succeed/Fail/Cancel/Process` enforce terminal-state guards (`InvalidOperationException` → non-fatal webhook handling).

**Persistence (`Payments.Infrastructure`):**
* `PaymentConfiguration` maps `Status` via `_status` field, `Version` concurrency, filtered unique `ProviderPaymentIntentId`.
* `ProcessedWebhookEventConfiguration` → `ProcessedWebhookEvents` (`Id`, `StripeEventId` 256 unique `UX_ProcessedWebhookEvents_StripeEventId`, `EventType` 128, `ProcessedAtUtc`).
* `OutboxMessageConfiguration` (Payments copy) → `OutboxMessages` (same shape as Orders/Inventory, pending/inProgress/published with retry).
* `PaymentsDbContext : BaseDbContext` owns `DbSet<Payment>`, `DbSet<ProcessedWebhookEvent>`, `DbSet<OutboxMessage>`; `ApplyConfigurationsFromAssembly`.
* Migrations (5): `20260824131713_InitialPayments`, `20260825090000_FixPaymentStatusAndId` (add `Status`, drop duplicate `Id`), `20260825100000_AddProcessedWebhookEvents`, `20260825110000_AddPaymentsOutbox`, `20260825120000_AddPaymentCustomerId`. No pending model changes.

**Stripe isolation (`ADR-011`):**
```
Payments.Domain ──X─► Stripe
Payments.Application ──X─► Stripe
Payments.Contracts ──X─► Stripe
Payments.Infrastructure ──► Stripe.net (only)
```
`IStripePaymentGateway` (`Application`) → `StripePaymentGateway` (`Infrastructure`, `Stripe.net`, `PaymentIntentService`, deterministic `intent_{OrderId}_{PaymentId}_{Version}` idempotency via `RequestOptions.IdempotencyKey`). `IStripeWebhookService` (`Application`, `StripeWebhookVerificationResult` with `PaymentIntentId`) → `StripeWebhookService` (`Infrastructure`, `EventUtility.ConstructEvent(tolerance:300, throwOnApiVersionMismatch:false)` + `JsonDocument` `data.object.id` extraction). `StripeOptions` (`SecretKey/WebhookSecret`) bound via `IOptions` from `Configuration["Stripe"]` (externalized, no secrets in source).

**Webhook flow (`9.8→9.11`, `PaymentsController`):**
```
Stripe → POST /api/payments/webhook [AllowAnonymous] (EnableBuffering → raw UTF-8)
  → Stripe-Signature header + payload → IStripeWebhookService.VerifyAndParse
  → IWebhookEventProcessor.TryProcessAsync(eventId,type,piId) — single PaymentsDbContext transaction:
        ├─ idempotency AnyAsync check + unique UX_ProcessedWebhookEvents_StripeEventId (race → AlreadyProcessed)
        ├─ lookup Payment by ProviderPaymentIntentId → ApplyEventToPayment (Supported: succeeded→Succeed, payment_failed→Fail, canceled→Cancel, processing→Process; unsupported→no-op; InvalidOperationException→ terminal protection, logged, success)
        ├─ add ProcessedWebhookEvent
        └─ if terminal Succeeded/Failed → IntegrationEventOutboxMessageFactory.Create("payments.payment_succeeded/failed", new PaymentSucceeded/FailedIntegrationEvent{CorrelationId=OrderId, CausationId=ProcessedEvent.Id}) → OutboxMessages
  → 400 on missing/invalid sig, 200 on valid/duplicate/unsupported/out-of-order (Stripe stops retrying)
```
`GET /api/payments/{paymentId}` `[Authorize]` enforces ownership (`CustomerId == ICurrentUser.UserId` else 404) — `[AllowAnonymous]` strictly limited to webhook.

**Observability (`9.12`):** `TraceContext.Source` (`FluxCommerce`) activities `Payments.CreatePaymentIntent`, `Payments.Stripe.CreatePaymentIntent`, `Payments.Webhook.Process` + tags `order.id/payment.id/stripe.*`; structured logs carry `OrderId/PaymentId/StripePaymentIntentId/StripeEventId/EventType/CorrelationId/CausationId`, no `SecretKey`/card payload.

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

Distributed business workflows are implemented using Saga Orchestration (Phase 8 — the Order/Inventory
Saga; see ADR-009, `docs/phases/PHASE_8_ORDER_INVENTORY_SAGA_IMPLEMENTATION_PLAN.md`, and
`docs/completed/PHASE_8_COMPLETION_REPORT.md`).

The Saga Orchestrator coordinates the workflow. It is an Orders-owned orchestration component (in
`Orders.Infrastructure`, beside the module DbContext, idempotency store, and consumers) whose state is
persisted in the Orders database in a dedicated Saga state table holding only workflow progress (Id,
OrderId, CorrelationId, State, timestamps, concurrency Version).

The Saga does NOT own:

- Order state
- Inventory state
- Payment state

Each module owns its own state. The Saga never reads another module's database and never uses
distributed transactions; coordination flows only through explicit Integration Event contracts over
RabbitMQ, published reliably through each module's Outbox and consumed by the idempotent consumer
infrastructure (Phase 6/7).

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

Phase 8 implements the Order + Inventory segment: on `orders.order_created` the Saga starts
(`WaitingForInventory`), publishes `inventory.reservation_requested`, and consumes `inventory.reserved`
/ `inventory.reservation_failed`. A success completes the Saga (`Completed`) while the Order stays
Pending; a failure transitions the Saga to `Failed`, cancels the Order, and publishes
`orders.order_cancelled`. A cancellation with an active reservation publishes
`inventory.release_requested`, whose release is compensated by the Inventory module. Saga transitions
are guarded by the Saga row's optimistic concurrency token (duplicate or conflicting results apply at
most once), and every consumer runs inside its module's idempotent-processing boundary. Payment
(Phase 9) and refund compensation (Phase 10) extend this pattern in later phases.

### 15.1 Order Processing Saga with Payment Compensation (Phase 10)

The Phase 10 Order Processing Saga extends the Phase 8 Order/Inventory Saga to incorporate
Payment orchestration and refund compensation. The workflow spans three modules:

- **Orders** owns Order state (`Pending → Confirmed → Cancelled`) and the Saga orchestration state
- **Inventory** owns Inventory item state and reservation/expiration lifecycle
- **Payments** owns Payment state (`Pending → Processing → Succeeded/Failed/Cancelled`) and Stripe integration

**Happy Path (10.2–10.4):**

1. Orders Saga starts at `WaitingForInventory`, publishes `inventory.reservation_requested`
2. Inventory consumes, reserves stock, publishes `inventory.reserved`
3. Saga transitions to `WaitingForPayment`, publishes `payments.payment_requested`
4. Payments consumes, creates PaymentIntent via Stripe, publishes `payments.payment_succeeded`
5. Saga consumes `payments.payment_succeeded`, transitions Order to `Confirmed`, Saga to `Completed`

**Payment Failure (10.5–10.6):**

1. Payments publishes `payments.payment_failed`
2. Saga consumes, transitions to `ReleasingInventory`
3. Inventory consumes, releases reservation, Saga to `Completed` (Order stays Pending/Cancelled)

**Post-Payment Compensation (10.8):**

1. Payments publishes `payments.refund_requested` (Stripe refund)
2. Saga consumes, transitions Order to `Refunding`
3. Inventory releases any remaining reservation, Saga to `Cancelled`
4. Order cancelled, `orders.order_cancelled` published

**Isolation guarantee:** The Saga never reads another module's DbContext. Each module's consumer
runs inside its idempotent-processing boundary (EventId + HandlerName). Saga state transitions are
guarded by the Saga row's optimistic concurrency token (`Version` field). Payment integration is
confined to `Payments.Infrastructure` → `Stripe.net` only; `Payments.Contracts` carries the
integration events (`PaymentSucceededIntegrationEvent`, `PaymentFailedIntegrationEvent`,
`RefundPaymentRequestedIntegrationEvent`, `PaymentRefundedIntegrationEvent`) that other modules
consume without accessing Payments' DbContext.

### 15.2 Reservation Expiry Orchestration (Phase 11)

Phase 11 closes the reservation-timeout path of the Order Processing Saga. When an Inventory
reservation reaches its expiry, the Inventory expiration worker (`ExpiredReservationProcessor`,
`ReservationExpirationBackgroundService`) expires the reservation **in Inventory only**, then the
system raises an **integration** event so the Orders Saga can react statefully.

The **domain** event `InventoryReservationExpired` (in `Inventory.Domain.Events`) is raised by
`InventoryReservation.Expire`; the **integration** contract `InventoryReservationExpiredIntegrationEvent`
lives in `Inventory.Contracts.IntegrationEvents`. These are distinct types and namespaces — the domain
event never crosses the module boundary; only the integration contract is published over RabbitMQ.

**Message flow:**

```text
Expiration Worker
      ↓
ExpiredReservationProcessor (batch-load, expire, release exactly once, per-reservation save)
      ↓
InventoryReservationExpired (domain event) → InventoryReservationExpiredIntegrationEvent (contract)
      ↓
Outbox (InventoryDbContext, transactional with the reservation state change)
      ↓
Outbox Dispatcher → RabbitMQ
      ↓
InventoryReservationExpiredConsumer (Orders.Infrastructure; stable handler Orders.ApplyReservationExpired)
      ↓
OrderSagaOrchestrator.ApplyReservationExpiredAsync
      ↓
OrderSaga.ApplyReservationExpired → WaitingForPayment → Cancelled
```

**Final `OrderSaga` transition matrix for `ReservationExpired` (from `OrderSaga.ApplyReservationExpired`):**

| Current State | Outcome |
|---------------|---------|
| `WaitingForPayment` | Only valid transition: `WaitingForPayment → Cancelled` |
| `WaitingForInventory` | Benign no-op (Success) |
| `ReleasingInventory` | Benign no-op (Success) — in-flight compensation, ignore safely |
| `Refunding` | Benign no-op (Success) — in-flight compensation, ignore safely |
| `Completed` | Benign no-op (Success) — terminal protected |
| `Failed` | Benign no-op (Success) — terminal protected |
| `Cancelled` | Benign no-op (Success) — terminal protected |
| any other/unspecified | `InvalidStateTransition` failure |

A late or duplicate `ReservationExpired` never moves a terminal state or an in-flight-compensation
state; only an active `WaitingForPayment` saga is cancelled. On the `WaitingForPayment → Cancelled`
expiry path the Saga does **not** stage `inventory.release_requested` — release is performed exclusively
by the Inventory expiration processor, so Inventory is released exactly once (no double release) and no
refund is duplicated.

**Guarantees (Phase 11):** at-least-once delivery with idempotent consumers; transactional Outbox
publication; optimistic-concurrency-guarded saga transitions; concurrency / out-of-order / duplicate
safety; terminal-state protection; crash-recovery safe via idempotency; correlation/causation preserved
across the async boundary via the `IntegrationEventEnvelope` (W3C trace context propagates through the
`Expiration` inventory activity source to the `Orders.Saga.ApplyReservationExpired` consumer).

---

## 6. Module Dependency Rules (expanded)

Modules are isolated. The following additions apply specifically to Phase 10 Payment Saga:

**Forbidden (expanded):**

    Inventory
       |
       X
       +--> Payments.Infrastructure

    Orders
       |
       X
       +--> PaymentsDbContext

    Payments
       |
       X
       +--> OrdersDbContext / InventoryDbContext

A module must not directly modify another module's state.

**Approved cross-module references (Phase 10):**

- **Orders.Infrastructure MAY reference `FluxCommerce.Modules.Payments.Contracts`** (the `Integration`
  and `IntegrationEvents` namespaces only) so it can consume `PaymentSucceeded`, `PaymentFailed`,
  `RefundPaymentRequested`, and `PaymentRefunded` integration events. This mirrors the existing
  Phase 4.5 carve-out that lets Orders.Application reference the Cart/Catalog Contracts, and the
  Phase 8 carve-out that lets Orders.Infrastructure reference `Inventory.Contracts`.

- **Orders.Infrastructure MAY reference `FluxCommerce.Modules.Inventory.Contracts`** (the `Integration`
  and `IntegrationEvents` namespaces only) as established in Phase 8. This same carve-out (Phase 11) lets
  `Orders.Infrastructure` consume `InventoryReservationExpiredIntegrationEvent` via
  `InventoryReservationExpiredConsumer` without ever touching the Inventory DbContext.

The architecture tests `Orders_ShouldNot_ReferenceOtherModules` and `Orders_ShouldNot_ReferenceInventory`
are updated to encode the narrow Payments carve-out. Payments never references Orders or Inventory internal
layers. Inventory never references Orders or Payments internal layers. Phase 11 adds dedicated rules
(`Inventory_ReservationExpirationTypes_ShouldBeOwnedByInventory` and
`Orders_Saga_ShouldNot_ReferenceInventoryReservationPersistence`) verifying the expiration processor,
background service, and both reservation-expired event types are confined to their owning modules and
that the Orders Saga never references Inventory reservation persistence.

**Concurrency & Idempotency (expanded from §16):**

- **Optimistic Concurrency:** Each module's aggregate root uses a `Version` field (bigint, `IsConcurrencyToken`)
  for EF Core concurrency protection. Saga state transitions also guard on the Saga row's `Version` token,
  so duplicate or conflicting results (Reserved vs Failed, Payment Succeeded vs Failed) apply at most once.
- **Idempotent Consumers:** Every asynchronous consumer that changes state must be idempotent (Rule 7). 
  Consumers use the `IIdempotentProcessor` boundary with `ProcessedMessage.Create(EventId, HandlerName)`
  as the idempotency key. The composite `(Key, Handler)` primary key in each module's `ProcessedMessages`
  table is the final concurrency protection: `(E1, H1)` can be claimed once, duplicate `(E1, H1)` races
  are resolved by the database and reported as `AlreadyProcessed`.
- **No distributed transactions:** Cross-module workflows use Integration Events + Saga Orchestration +
  Compensation rather than distributed transactions (2PC).
- **Outbox Pattern:** Each module's Outbox ensures reliable event publication atomically with the
  business state change. The Outbox Message contains sufficient metadata (CorrelationId, CausationId,
  OccurredAtUtc, RetryCount, Error information) for reliable processing and observability.

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

**Reservation expiry (Phase 11):** the `InventoryReservationExpiredIntegrationEvent` is handled by the
`InventoryReservationExpiredConsumer` (`Orders.Infrastructure`) inside the standard idempotent-processing
boundary with stable handler `Orders.ApplyReservationExpired` and `ProcessedMessage.Create(EventId,
HandlerName)` keyed on `(EventId, HandlerName)` and the `(Key, Handler)` composite PK. Duplicate or
redelivered expiry events therefore produce at most one saga effect; a duplicate on a terminal/completed
state is an idempotent no-op.

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