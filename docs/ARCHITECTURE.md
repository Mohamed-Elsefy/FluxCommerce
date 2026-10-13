# Enterprise Commerce & Order Management Platform
# Architecture Specification

# 1. Architectural Style

Modular Monolith on Clean Architecture + DDD: explicit module boundaries, module-owned
business state and persistence, async messaging where valuable, Outbox for reliable
publication, idempotent consumers, saga orchestration with compensation, strong
observability. Intentionally not microservices; extraction stays possible via
boundaries → independent persistence → async contracts → optional split. No
distributed-system complexity before it pays.

---

# 2. High-Level Architecture

```
API → Modules → (BuildingBlocks, Infrastructure → SQL Server / Redis / RabbitMQ / Stripe)
```

Business modules: Catalog, Cart, Orders, Inventory, Payments. Platform: Identity,
observability, cross-cutting infrastructure. Identity is a capability, not a commerce
bounded context.

---

# 3. Module Structure

```
Module/{Domain, Application, Infrastructure, Contracts}
```

- **Domain**: entities, aggregates, value objects, domain events/services, rules, invariants.
  No ASP.NET Core, EF, broker, cache, or external APIs.
- **Application**: commands/queries/handlers, use cases, DTOs, validation, ports. Depends on
  Domain; contains no infrastructure implementations.
- **Infrastructure**: persistence (EF), external integrations, messaging/caching implementations.
  Implements Domain/Application abstractions; holds no business rules.
- **Contracts**: public integration-event contracts (deriving from the contract-only
  `BuildingBlocks.Messaging` base with stable `EventType`), public DTOs, inter-module
  interfaces. No internals. Only Messaging reference a module allows; Domain/Application/
  Infrastructure stay free of it (ADR-007).

---

# 4. BuildingBlocks

Genuinely cross-cutting technical primitives only (10):

```
Domain          Entity, AggregateRoot, DomainEvent
Application     Result, Error, ICommand/IQuery (+handlers), ICurrentUser, PagedResult
Infrastructure  RabbitMQ publisher/consumer, Redis, TraceContext (name aliases Observability)
Messaging       IIntegrationEvent/IntegrationEvent/EventVersion/IIntegrationEventHandler (contract-only)
Outbox          OutboxMessage, IOutboxStore, IntegrationEventOutboxMessageFactory, OutboxMessageConfigurationBase
Idempotency     ProcessedMessage (deterministic Create timestamp), IIdempotentProcessor (the only
                business boundary), IIdempotencyStore, IDuplicateDetector, ProcessedMessageConfigurationBase
Persistence     BaseDbContext, design-time helpers
Observability   OTel resource + ActivitySources (single source of truth for the source name)
Localization    ILocalizer, ICurrentCulture, SharedResources
Web             ApiControllerBase, ProblemDetails (+Validation), ApiResponse, middleware,
                Services/WebCorrelationIdProvider, Extensions/HttpContextItems+HttpContextExtensions
```

Notes: per-module `OutboxMessages`/`ProcessedMessages` duplication is intentional (module-local
atomicity, no distributed transactions) with shared mapping-base helpers. Consumer retry and
outbox retry stay separate concepts. `Result` (Application) and `ApiResponse` (Web) are
different layers, not duplication. `EndpointTags` and error-code maps live in the API host,
not in Web.

---

# 5. Dependency Rules

API → Application → Domain; Infrastructure implements their abstractions, never the reverse.

---

# 6. Module Dependency Rules

Forbidden: any module reaching another module's DbContext, repository, entity, application
service, or infrastructure implementation; any direct cross-module state mutation.

Approved carve-outs (all contract-namespaces only, pinned by arch tests):
Orders.Application → Cart/Catalog Contracts (checkout); Orders.Infrastructure →
Inventory.Contracts + Payments.Contracts (`Integration`/`IntegrationEvents`); Catalog serves
Cart/Orders through its Contracts. Payments/Inventory never reach into Orders.

---

# 7. Cross-Module Communication

Synchronous application contracts where immediate consistency is required; integration events
over RabbitMQ where loose coupling and eventual consistency fit (multi-module workflows,
reliability needs). Never use the broker for in-module calls.

---

# 8. Domain Events vs Integration Events

`OrderPlaced` (internal) ≠ `OrderPlacedIntegrationEvent` (boundary). Modules explicitly decide
what crosses: Domain Operation → Domain Event → Application/Outbox decision →
Integration Event → Outbox → RabbitMQ.

---

# 9. Persistence Architecture

Module-per-DbContext on one SQL Server instance (logical ownership first, multi-DB later):
Catalog, Orders, Inventory, Payments, Identity DbContexts (all `BaseDbContext`,
`decimal(18,6)`, `datetime2`). Cart is Redis-only. No cross-module DbContext access, relations,
or foreign keys — only contracts, queries, projections, and events.

Payments owns `Payments`, `ProcessedWebhookEvents` (`StripeEventId` unique),
`OutboxMessages`. Stripe SDK is confined to `Payments.Infrastructure` (arch-enforced);
options live in `Infrastructure/Options` (`PaymentGatewayOptions`, section `Stripe`).

---

# 10. Database Strategy

One instance today; boundaries must allow module databases → service databases later.
Business logic must never rely on cross-module database access.

---

# 11. Transaction Boundaries

One module per transaction: Business State + Outbox (publish side) or Business State +
ProcessedMessage (consume side). Cross-module workflows use events + sagas + compensation.

---

# 12. Outbox Pattern

Module transaction commits state + `OutboxMessage` (IDs, type/version, payload, timestamps,
Correlation/Causation, trace context, retry/error metadata) atomically. Dispatchers claim via
`UPDLOCK, READPAST`, back off exponentially, recover stale claims, park poison as terminal.
Failed messages never vanish silently.

---

# 13. Messaging Semantics

At-least-once, always. Consumers are idempotent; messages carry stable unique IDs; processed
records are the deduplication truth.

---

# 14. RabbitMQ & Consumer Model

Durable topic exchange (`fluxcommerce.events`, `Type` as routing key), publisher confirms,
manual ACK only after commit. Handlers are transport-agnostic (`IIntegrationEventHandler<T>`
+ envelope) implemented in the consuming module's Infrastructure and resolved through an
explicit registry (unknown types skipped, never poison).

- Identity: `EventId + HandlerName` → canonical `ProcessedMessage`, `(Key, Handler)` PK as the
  concurrency arbiter; `IIdempotentProcessor.TryProcessAsync` is the single atomic boundary
  (duplicate → ACK without re-execution; race → loser reports `AlreadyProcessed`).
- Retry is in-process and bounded (`MaxAttempts`, backoff, `x-death`-seeded attempts, fresh DI
  scope per attempt); terminal failures (permanent/exhausted/malformed) land in the DLX/DLQ
  topology enriched (`x-dead-letter-reason`, `x-attempt`, `x-error-type`, …), body preserved.
- Consistent structured logs (Message/Event/Type/Version/Correlation/Causation/Consumer/
  Attempt/Result/Duration/…), no business payloads; correlation preserved, consumed `EventId`
  becomes causation; producers stamp from the consumed envelope.
- Shutdown cancels in-flight work (unacked → redelivered); misconfiguration fails fast at startup.

---

# 15. Saga Architecture

Orders-owned orchestration in `Orders.Infrastructure` (4 partials: core/reservation/payment/
compensation; byte-faithful split), progress-only state table (`Version`-guarded), explicit
integration-event coordination only:

`WaitingForInventory → WaitingForPayment → Completed`, with `Failed`, `Cancelled`,
`ReleasingInventory`, `Refunding`. Payment leg: reservation → `payment_requested` →
webhook → `payment_succeeded` → `Confirmed/Completed`; failure → release → cancel;
post-payment failure → Stripe refund → release → cancel. Expiry leg: worker releases
exactly once in Inventory → `InventoryReservationExpired` via Outbox → consumer moves
`WaitingForPayment → Cancelled` (only valid expiry transition; no release staged on that
path, so no double release). Duplicate/conflicting/out-of-order results resolve
deterministically; terminal states are protected; recovery resumes via idempotency.

Ports: `IOrderAggregateStore` (tracked aggregates + unit of work) vs `IOrderQueryStore`
(projections); `IOrderStore` remains their composite. Counts breakdowns live in read-model
factories (`FromStatusGroups/FromParts`), stores stay thin adapters.

---

# 16. Idempotency

Required at API (`Idempotency-Key` + fingerprint), consumer (`EventId, HandlerName`),
outbox dispatch, webhook (`StripeEventId`), intent (deterministic key), and saga commands.
No single universal mechanism — explicit keys where required. `ProcessedMessage` records are
retained (redelivery has no expiry bound); purge only past a proven end-of-life.

---

# 17. Redis

Infrastructure capability: Cart state (primary), plus caching/locks/rate-limiting where
justified. Never the source of business truth unless designed as such (Cart only).

---

# 18. API Layer

Thin controllers mapping HTTP ↔ use cases ↔ responses. Host layout:

```
Controllers/ (16: Catalog×7, Cart, Orders×2, Inventory, Payments×2, Identity×2, Admin dashboard)
Services/     IDashboardComposer (adaptive snapshot; per-module parallelism, legacy failure precedence)
Mapping/      ContractEnumMappers, InventoryResponseMapper, (module mappers in Application)
Models/       AdminDashboardResponse, DailySeriesBuilder (in-memory bucketing: SQLite/SQL Server portable)
Constants/    EndpointTags        Localization/  ErrorCodeLocalizer (per-area partials)
Extensions/   DI (presentation/modules/messaging/health sections), auth, pipeline, Swagger, options
Authorization/ supremacy handlers (SuperAdmin passes everything; SuperAdminPolicy stays exact)
```

PaymentsController is a thin adapter: `GetPayment` delegates ownership to the `GetPayment`
query; the webhook action only reads the raw body and maps the endpoint-service outcome.
`POST /orders/checkout` maps the aggregate to `CheckoutResponse` via `OrderResponseMapper`.
Dashboard: any authenticated caller gets 200 with entitled sections only (`null`/`[]`
otherwise); validation errors surface before composition.

Auth: JWT Bearer (15 min) + rotating opaque refresh (7 days); 9 action-level permissions
(`orders.read/cancel/refund`, `payments.read/stats`, `inventory.read/manage`,
`catalog.manage`, `users.read`) granted per-admin by SuperAdmin; grants apply on next
login/refresh. Catalog reads stay anonymous; ownership mismatches read as 404.

---

# 19. Error Handling

Expected business errors → `Result`/`Error` (stable machine-readable `Code`, English
`Description` fallback). Presentation maps to localized ProblemDetails (`Title`/`Detail`
via `ILocalizer` + code→key map); status/type/shape never change with language.
Unexpected failures → centralized handler → generic localized 500. No exceptions for control
flow, no `Result` for the truly exceptional.

---

# 20. Observability

Structured logging, Correlation/Causation/Trace IDs, OpenTelemetry, metrics, health checks.
Every request and every async message is traceable producer → outbox → broker → consumer →
module, with zero untraced failures as the invariant.

---

# 21. Configuration

ASP.NET Core configuration + Options + environment overrides; `ValidateOnStart`.
Secrets (DB/RabbitMQ/Redis/JWT/Stripe/seeder) externalized via `.env` (gitignored),
user-secrets, or environment — never committed. `appsettings.json` ships closed defaults.

---

# 22. Testing Strategy

- **Unit** (2412): domain rules, aggregates, application logic, mappers, policies.
- **Integration** (561 suites incl. 92 reliability + 13 regression): EF/SQL Server, Redis,
  RabbitMQ, Stripe webhook flows, saga E2E, dashboard composition.
- **Architecture** (187): dependency/layer/module rules, Stripe isolation, enum-mirror
  drift guards, orchestrator placement, consumer boundaries.
- **E2E/failure**: full business workflows + Phase 12 failure matrix, all green except
  3 pre-existing email-link environment failures (reproduced on the clean tree).

Progressive, per-suite authoritative (parallel Redis EVAL can flake in combined runs).

---

# 23. Architecture Enforcement

Documentation states the rules; 187 arch tests enforce the critical ones; review protects
the rest. Erosion is prevented by the combination, not by any single gate.

---

# 24. Current Implementation Priority

Follow `IMPLEMENTATION_PLAN.md`: Core MVP (0–12) + 21/22 + R1/R2/R3 are complete; Phase 23
(dashboard data) is next; 13–20 remain planned commerce extensions.

---

# 25. Core Principle

Each module owns its state, protects its invariants, and exposes explicit contracts.
Modules never touch each other's internals. Reliable async communication is
Outbox + at-least-once + idempotent consumers. Distributed workflows are sagas with
compensation. Start a Modular Monolith; distribute only when justified. Optimize for
correctness, maintainability, reliability, and evolvability.

---

# 26. Localization Architecture

User-facing strings only (validation, ProblemDetails, auth errors, emails, API docs);
business data stays single-language. `BuildingBlocks.Localization` exposes only
`ILocalizer`/`ICurrentCulture` to modules; single culture registry (`en` default +
`fr/de/es/ar`); resolution query → cookie → `Accept-Language` (+ `Content-Language`
response header); neutral-English fallback; `Error.Code` stable, localized at the
presentation boundary. New module keys belong to the owning module's resources going
forward (shared set is frozen).
