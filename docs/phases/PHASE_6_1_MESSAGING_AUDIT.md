# Phase 6.1 — Baseline & Messaging Architecture Audit

> **Document purpose.** This is the Phase 6.1 deliverable from
> `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md` §5. It records the **actual, observed** state of the
> messaging, Outbox, Domain Event, RabbitMQ, persistence/transaction, and observability foundation of
> the FluxCommerce codebase *before* Phase 6.2 implementation begins. It is the baseline snapshot and
> comparison point for the remaining Phase 6 sub-phases. Everything in this document reflects what was
> inspected and executed, not what the architecture is intended to become.

- **Phase:** 6.1 — Baseline & Messaging Architecture Audit
- **Date:** 2026-08-14
- **Scope:** Strictly Phase 6.1 (audit/inspection/documentation only). No production code was changed.
  No Phase 6.2+ functionality was implemented. No Phase 7+ functionality was introduced.
- **Authoritative references read:** `docs/PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`,
  `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md`, ADRs `001`–`005` (`docs/adr/`), completed phase reports
  `PHASE_0`–`PHASE_5` (`docs/completed/`).

## Classification Legend

- **COMPLIANT** — the current implementation satisfies the relevant Phase 6 / architecture requirement.
- **PARTIAL** — the foundation exists but is incomplete for Phase 6.
- **GAP** — the capability is required by Phase 6 but absent.
- **UNKNOWN** — the current state could not be determined from the codebase; requires a decision.

---

## 1. Repository State

| Item | Value |
|------|-------|
| Branch | `feature/outbox-rabbitmq` |
| HEAD commit | `25cca32` — "Merge pull request #11 from Mohamed-Elsefy/refactoring-1" |
| Solution | `FluxCommerce.slnx` (XML solution format) |
| Target framework | `net10.0` (SDK `10.0.400`, `global.json` requests `10.0.302` with `rollForward: latestFeature`) |
| Build settings | `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, nullable enabled |
| Infrastructure | `docker-compose.yml` runs SQL Server 2022, Redis 8, RabbitMQ 4 (management), smtp4dev |
| Working tree | Only untracked `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md` before this audit; **no tracked file changed** by this audit |

---

## 2. Current Messaging Architecture

**Classification: PARTIAL**

### 2.1 Integration Event contracts (`FluxCommerce.BuildingBlocks.Messaging`)

The messaging BuildingBlock is **contract-only and foundation-only**. It defines:

- `Events/IIntegrationEvent` — metadata surface: `EventId`, `CorrelationId`, `CausationId`,
  `EventVersion Version`, `OccurredAtUtc`.
- `Events/IntegrationEvent` — abstract base implementing `IIntegrationEvent`. Constructor takes
  `correlationId`, optional `causationId`, optional `version`; defaults `EventId`/`OccurredAtUtc` to
  `Guid.NewGuid()` / `DateTime.UtcNow`.
- `Metadata/EventMetadata` — an alternative standalone metadata record (also self-generating
  `EventId`, adds `EventType` and `CreatedAtUtc`).
- `Versioning/EventVersion` — `readonly record struct`, `major.minor`, `V1` helper, parse/try-parse,
  `IComparable`.

### 2.2 What exists and what does not

| Capability | State |
|---|---|
| Integration Event base contract + versioning | **COMPLIANT** (foundation) |
| Concrete Integration Events | **GAP** — no event derives from `IIntegrationEvent`/`IntegrationEvent` anywhere in `src/` |
| Publisher abstraction | **GAP** |
| Consumer / subscriber infrastructure | **GAP** (Phase 7) |
| Exchange/queue/routing topology | **GAP** (Phase 6.6) |
| Serialization contract for the wire | **GAP** (Phase 6.4/6.6) |
| DI registration of messaging components | **GAP** — `BuildingBlocks.Messaging` is referenced by nothing in `src/` |

`IIntegrationEvent`/`IntegrationEvent`/`EventMetadata`/`EventVersion` have **zero usages** outside
their own definitions.

**Relevance to Phase 6:** the versioned integration event contract is the base for Phase 6.4. Its
location in `BuildingBlocks.Messaging` collides with the existing architecture rule that module
*Contracts* projects must not reference `BuildingBlocks.Messaging` (see §11.3 and §12).

---

## 3. Domain Event Infrastructure

**Classification: COMPLIANT (collection) / PARTIAL (dispatch)**

### 3.1 Primitives (`FluxCommerce.BuildingBlocks.Domain`)

- `Events/IDomainEvent` — marker interface.
- `Events/DomainEvent` — abstract base adding `EventId` (`Guid.NewGuid()`) and `OccurredAtUtc`
  (`DateTime.UtcNow`).
- `Events/IDomainEventDispatcher` — `DispatchAsync(IDomainEvent, CancellationToken)`.
- `Entities/Entity<TKey>`, `Entities/IAggregateRoot` (`GetDomainEvents()`, `ClearDomainEvents()`),
  `Entities/AggregateRoot<TKey>` (private `_domainEvents` list; `AddDomainEvent`/`RemoveDomainEvent`
  protected).

### 3.2 How Domain Events are raised and collected

Raising is **Domain-owned**: aggregate methods call the protected `AddDomainEvent(...)`
(`AggregateRoot.cs:17`), e.g. `Order.Place` → `OrderCreated`, `Order.Cancel` → `OrderCancelled`.

Collection is **persistence-owned** via `DomainEventInterceptor` (`BuildingBlocks.Persistence`):

- Registered as `AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>()` in the API host
  (`src/FluxCommerce.Api/Extensions/DependencyInjection.cs:79`). Because each module DbContext is
  scoped, every module gets its own interceptor instance per scope.
- `SavingChangesAsync` clears the previous capture, then collects `GetDomainEvents()` from every
  `IAggregateRoot` currently in the change tracker.
- `SavedChangesAsync` invokes `OnTransactionCompleted(...)` **which currently returns
  `Task.CompletedTask`** (empty hook), then `ClearDomainEvents()` on tracked aggregates and clears the
  capture.
- `SaveChangesFailed` clears the capture so a failed transaction does not leak events.

**Critical design caution for Phase 6.3:** the outbox hook currently runs in `SavedChangesAsync`,
i.e. **after** the transaction commits. If Phase 6.3 inserts the Outbox record from that hook, the
record would be written in a **second, separate transaction**, breaking the Phase 6.3 requirement
that Business State and Outbox commit (or roll back) **atomically**. The outbox record must instead be
added to the change tracker so it is flushed by the **same** `SaveChangesAsync` as the business state
(either by appending tracked Outbox entities during `SavingChangesAsync`, or by the module store
adding them before the save). This is a design constraint, not a code change, recorded for Phase 6.3.

`BaseDbContext` participates by wiring the interceptor into `OnConfiguring` (`BaseDbContext.cs:27-31`)
for every derived module DbContext.

### 3.3 Findings

| Item | State |
|---|---|
| Domain Event raise/collect infrastructure | **COMPLIANT** |
| Domain events survive the commit boundary (captured after successful save) | **COMPLIANT** (by design of `SavedChangesAsync`) |
| Post-commit handling of collected events | **GAP** — `OnTransactionCompleted` is an empty hook; nothing consumes the captured events (no Outbox insert, no dispatch). Moreover the hook runs **after** commit; an Outbox insert there would be non-atomic with business state (see the design caution above) |
| `IDomainEventDispatcher` | **PARTIAL/UNKNOWN** — interface exists but has **no implementation and no DI registration**; no code dispatches domain events through it. Its role in Phase 6 (in-process dispatch vs. Outbox-only) is a Phase 6.3 decision |

---

## 4. Outbox Foundation

**Classification: PARTIAL**

All Outbox types live in `FluxCommerce.BuildingBlocks.Outbox` (no project references; pure contracts):

- `Messages/OutboxMessage` — `sealed record` with `Id`, `EventId`, `Type`, `Payload`,
  `CorrelationId`, `CausationId`, `OccurredAtUtc`, `CreatedAtUtc`, `ProcessedAtUtc`, `Status`,
  `RetryCount`, `Error`.
- `Messages/OutboxStatus` — enum `Pending = 0`, `InProgress = 1`, `Published = 2`, `Failed = 3`.
- `Stores/IOutboxStore` — `AddAsync`, `GetPendingAsync(batchSize)`, `MarkInProgressAsync`,
  `MarkPublishedAsync`, `MarkFailedAsync(id, error)`.
- `Dispatch/IOutboxDispatcher` — `DispatchPendingAsync(CancellationToken)`.

| Capability | State |
|---|---|
| Outbox data contract vs Phase 6.2 required data | **COMPLIANT** — `OutboxMessage` already carries every field listed in `PHASE_6_OUTBOX_RABBITMQ.md` §6 (`MessageId`, `EventId`, `EventType`, `Payload`, `OccurredAtUtc`, `CorrelationId`, `CausationId`, `CreatedAtUtc`, `ProcessedAtUtc`, `RetryCount`, `Status`, `Error`) |
| Status representation | **COMPLIANT** — `Pending/InProgress/Published/Failed` covers the Phase 6.5 state flow |
| Outbox store implementation (EF mapping, table, index, migration) | **GAP** |
| Outbox participation in a module transaction | **GAP** — no module DbContext maps an Outbox entity; `DomainEventInterceptor.OnTransactionCompleted` is empty |
| Claiming / recovery semantics | **GAP** — `GetPendingAsync` is a naive pending query; there is no `Claim`, no claim-expiry/recovery operation. `MarkInProgressAsync` exists as a primitive but no recovery of stale claims |
| Outbox dispatcher implementation / background worker | **GAP** |
| Retry/failure/error persistence behavior | **GAP** — fields exist on the contract only |
| DI registration | **GAP** — `IOutboxStore` / `IOutboxDispatcher` are registered nowhere and referenced nowhere in `src/` |

**Reuse note:** the existing `OutboxMessage` / `OutboxStatus` / `IOutboxStore` / `IOutboxDispatcher`
contracts should be reused by Phase 6.2/6.5/6.7 rather than redefined. Phase 6.5 will need to extend
the store surface (claim + recovery operations) per the spec — this is an extension of the existing
abstraction, not a new duplicate abstraction.

> **Phase 6.2 delta (not a historical rewrite).** The rows above describe the baseline captured *before*
> Phase 6.2. As a direct result of the Phase 6.2 implementation, the **"Outbox store implementation
> (EF mapping, table, index, migration)"** row is no longer a gap for the Orders and Inventory modules:
> both modules now map `OutboxMessage` to an `OutboxMessages` table (with the two status indexes) inside
> their own module DbContext and migration. The remaining rows (transaction participation, claiming,
> dispatcher, retry/error behavior, DI registration) are still gaps and belong to Phase 6.3+.
> Additionally, the architecture rule `Orders_/Inventory_ShouldNot_ReferenceOutbox` was scoped to the
> protected layers (Domain/Application/Contracts); module Infrastructure is now *required* to reference
> `BuildingBlocks.Outbox` to implement the module Outbox persistence. See
> `docs/phases/PHASE_6_2_OUTBOX_PERSISTENCE.md`.

---

## 5. RabbitMQ Foundation

**Classification: PARTIAL — runtime/connectivity ready, publishing infrastructure absent**

`RabbitMQ.Client` **7.2.1** (async `IChannel` API) is pinned in `Directory.Packages.props`.

- `BuildingBlocks.Infrastructure/Messaging/IRabbitMQConnectionProvider` — `GetConnection()`,
  `CreateChannelAsync(ct)`, `IAsyncDisposable`.
- `BuildingBlocks.Infrastructure/Messaging/RabbitMQConnectionProvider` — lazy
  `Lazy<Task<IConnection>>`, `AutomaticRecoveryEnabled = true`, `CreateConnectionAsync` from an
  `amqp://` URI.
- `BuildingBlocks.Infrastructure/DependencyInjection/RabbitMQServiceExtensions.AddRabbitMQ(...)` —
  registers `IRabbitMQConnectionProvider` as a **singleton** (instance is constructed eagerly).
- API host: `MessageBrokerOptions` (`Host`, `Port`, `Username`, `Password`, `VirtualHost`,
  `RetryCount`) bound from the `MessageBroker` config section (`DependencyInjection.cs:48-51`); the
  connection string is composed in `GetRabbitMQConnectionString` (`DependencyInjection.cs:112-128`).
- `RabbitMQHealthCheck` opens a channel and reports Healthy/Degraded (`HealthChecks/RabbitMQHealthCheck.cs`).
  Health checks are wired into `/health` (`PipelineConfiguration.cs`).

| Capability | State |
|---|---|
| Connection factory / channel creation / auto-recovery | **COMPLIANT** (foundation) |
| Connectivity health check | **COMPLIANT** |
| Publisher abstraction | **GAP** (Phase 6.6) |
| Exchange / queue declarations and routing | **GAP** (Phase 6.6) |
| Message serialization + metadata propagation | **GAP** (Phase 6.6/6.9) |
| Publisher confirms / connection & channel lifecycle hardening | **GAP** (Phase 6.6) |
| Failure handling / recovery of publication | **GAP** (Phase 6.8) |
| Dispatcher background worker consuming the provider | **GAP** (Phase 6.7) |

**Observations (not fixed during the audit):**
- `RabbitMQConnectionProvider` is registered as a singleton but is not registered for DI disposal; it
  is `IAsyncDisposable`. Minor operational gap; Phase 6.6 should address connection/channel lifecycle.
- `MessageBrokerOptions.RetryCount` is not consumed anywhere; it is a leftover candidate for the
  publisher/dispatcher retry policy (Phase 6.8) or removal.

---

## 6. Persistence and Transaction Boundaries

**Classification: COMPLIANT (module-per-DbContext) / PARTIAL (Outbox cannot yet share a module transaction)**

### 6.1 Shared persistence building block

- `BaseDbContext` (`BuildingBlocks.Persistence/DbContexts`) is module-agnostic: it adds the
  `ISaveChangesInterceptor` in `OnConfiguring`, applies `ApplyConfigurationsFromAssembly`, and applies
  shared conventions (`PersistenceConfigurationExtensions.AddPersistenceConventions`: `decimal(18,6)`,
  `datetime2`).
- Every module DbContext (`CatalogDbContext`, `OrdersDbContext`, `InventoryDbContext`,
  `PaymentsDbContext`, `IdentityDbContext`) derives from `BaseDbContext` (enforced by architecture
  test `AllModuleDbContexts_ShouldDeriveFromBaseDbContext`). `PaymentsDbContext` is an empty skeleton.
- `DesignTimeSaveChangesInterceptor` is the design-time stand-in for the runtime interceptor.

### 6.2 Module persistence boundaries and atomicity today

- **Orders** (`OrdersDbContext`): `CheckoutStore.PersistAsync` adds the `Order` aggregate + the
  `(UserId, IdempotencyKey)` record and persists with a single `SaveChangesAsync` — one atomic unit
  (`CheckoutStore.cs:50-86`). `OrderStore.SaveChangesAsync` flushes tracked mutations. No explicit
  `BeginTransaction` is used; the DbContext save is the transaction boundary.
- **Inventory** (`InventoryDbContext`): `InventoryStore.TryReserveAsync` runs inside an explicit
  transaction wrapped in the execution strategy: atomic conditional `UPDATE InventoryItems ... WHERE
  ReservedQuantity + q <= OnHandQuantity` gates availability, then inserts the reservation and commits
  (`InventoryStore.cs:94-171`). Lifecycle operations (Commit/Release/Expire) persist reservation +
  item together via a single `SaveChangesAsync` guarded by the optimistic-concurrency `Version` token
  (`SaveLifecycleChangesAsync`).
- **Identity**: uses `IUnitOfWork`/`UnitOfWork`; Orders/Inventory do **not** use a generic UoW — they
  use store abstractions (`ICheckoutStore`, `IOrderStore`, `IInventoryStore`).
- Cart is Redis-backed (no SQL DbContext) per Phase 3.

### 6.3 Can Business State + Outbox participate in the same module transaction today?

**No.** The outbox has no persistence model mapped to any module DbContext, and the
`DomainEventInterceptor` hook that would write the outbox record is empty. Once Phase 6.2 adds the
Outbox entity to the module persistence boundary and Phase 6.3 fills the interceptor hook, the
existing atomic save patterns (single `SaveChangesAsync`, or the explicit transaction in
`TryReserveAsync`) will naturally place Business State + Outbox in the same module transaction —
without cross-module or distributed transactions.

| Item | State |
|---|---|
| Module-per-DbContext ownership | **COMPLIANT** |
| Atomic module persistence (Order+idempotency; reservation+item) | **COMPLIANT** |
| No cross-module DbContext / FK / shared tables | **COMPLIANT** (enforced) |
| Outbox in the same module transaction | **GAP** — requires Phase 6.2/6.3 |

---

## 7. Correlation, Trace, and Observability

**Classification: COMPLIANT (request-level) / PARTIAL (messaging/async propagation absent)**

- `CorrelationIdMiddleware` (`BuildingBlocks.Web`) assigns or propagates `X-Correlation-ID`, stores it
  in `HttpContext.Items["CorrelationId"]`, echoes it on the response, and sets the
  `correlation_id` activity tag. Registered in `PipelineConfiguration.cs:23`.
- `HttpContextExtensions` exposes `TryGetCorrelationId`/`GetCorrelationId` (+ RequestId helpers).
- `RequestLoggingMiddleware` adds `RequestId`, `CorrelationId`, `TraceId`, `SpanId` to a
  Serilog log scope for every request.
- Serilog console sink; `ObservabilityConfiguration` adds OpenTelemetry tracing
  (`ActivitySources.FluxCommerce` + ASP.NET Core instrumentation) and metrics (ASP.NET Core + runtime),
  console exporters.
- Health checks include SQL Server, Redis, and RabbitMQ connectivity.

**Gap for Phase 6:** there is **no propagation of correlation/causation into the Outbox or RabbitMQ
message**, no dispatcher/publisher instrumentation, and no message-level logging of
MessageId/EventId/EventType. The CorrelationId exists only at the HTTP boundary. Phase 6.9 must bridge
HTTP → Domain Event → Outbox → Dispatcher → RabbitMQ. The existing `CorrelationIdMiddleware` +
`IntegrationEvent` metadata (`CorrelationId`/`CausationId`) are the reuse points; currently the
integration-event metadata has no carrier into domain events (domain events only carry `EventId` +
`OccurredAtUtc`), so correlation must be injected at the mapping/outbox layer in Phase 6.3/6.9.

---

## 8. Orders Domain Events — Current Lifecycle

**Classification: PARTIAL**

Events (`FluxCommerce.Modules.Orders.Domain/Events/`):

- `OrderCreated` — raised in `Order.Place(...)` (`Order.cs:124`).
- `OrderCancelled` — raised in `Order.Cancel(...)` (`Order.cs:317`).

Lifecycle today:

1. Aggregate method adds the event to `AggregateRoot._domainEvents`.
2. `OrdersDbContext.SaveChangesAsync` (via `CheckoutStore.PersistAsync` for creation, or
   `OrderStore.SaveChangesAsync` for cancellation) triggers `DomainEventInterceptor`.
3. The interceptor captures the event, the save commits atomically with the business state, then
   `OnTransactionCompleted` (empty) runs and the event is discarded.

There is **no** domain-event → integration-event mapping, no outbox record, and no publication.
`OrderCreated`/`OrderCancelled` are the Phase 6.3 candidates named in the spec.

---

## 9. Inventory Domain Events — Current Lifecycle

**Classification: PARTIAL**

Events (`FluxCommerce.Modules.Inventory.Domain/Events/`):

- `InventoryItemCreated` — raised in `InventoryItem.Create` (`InventoryItem.cs:65`).
- `InventoryStockAdded` — raised in `InventoryItem.AddStock` (`InventoryItem.cs:131`).
- `InventoryReservationCreated` — raised in `InventoryReservation.Reserve` and
  `.CreateReserved` (`InventoryReservation.cs:100,151`).
- `InventoryReservationCommitted` — `InventoryReservation.Commit` (`InventoryReservation.cs:238`).
- `InventoryReservationReleased` — `InventoryReservation.Release` (`InventoryReservation.cs:260`).
- `InventoryReservationExpired` — `InventoryReservation.Expire` (`InventoryReservation.cs:282`),
  invoked by `ExpiredReservationProcessor` in the `ReservationExpirationBackgroundService` cycle.

Lifecycle is identical to Orders: raised on the aggregate, captured by `DomainEventInterceptor` on
the single atomic save, then discarded. The Phase 5 completion report explicitly defers
`ReservationExpired` publication to Phase 6+. `InventoryReservationExpired` is the Phase 6.3
candidate named in the spec.

---

## 10. Architecture Compliance (current state)

**Classification: COMPLIANT — all 153 architecture tests pass**

Enforced rules relevant to Phase 6:

- `Domain -> Infrastructure` forbidden (`ArchitectureTests.Domain_ShouldNot_ReferenceInfrastructure`).
- `Domain -> EF Core` forbidden.
- `Application -> API`, `Application -> EF Core`, `Application -> ASP.NET Core`,
  `Application -> BuildingBlocks.Infrastructure` forbidden.
- `BuildingBlocks -> Modules` forbidden.
- Modules must not reference another module's Infrastructure or DbContext.
- **Orders and Inventory must not reference `BuildingBlocks.Messaging`, `RabbitMQ.Client`, or
  `BuildingBlocks.Outbox`** (`Orders_ShouldNot_ReferenceMessagingOrRabbitMq`,
  `Inventory_ShouldNot_ReferenceMessagingOrRabbitMq`, plus the `*_ShouldNot_ReferenceOutbox` rules).
- **Orders/Inventory *Contracts* must not reference `BuildingBlocks.Messaging` or
  `BuildingBlocks.Outbox`** (`Orders_Contracts_ShouldNot_ReferenceMessagingOrOutbox`,
  `Inventory_Contracts_ShouldNot_ReferenceMessagingOrOutbox`).
- No `*Saga` / `*Orchestrator` types in Orders or Inventory.
- Module DbContexts must reside in `Infrastructure.DbContexts` and derive from `BaseDbContext`.
- Cart owns no DbContext; every SQL-backed module owns exactly one.

### 11.1 Where this leaves Phase 6

The architecture keeps RabbitMQ and Outbox out of Domain and Application and out of module Contracts.
This is consistent with `PHASE_6_OUTBOX_RABBITMQ.md` §2.4 ("Domain X--> RabbitMQ; Application X-->
RabbitMQ client"). The Outbox write must therefore live in module **Infrastructure** (the
`DomainEventInterceptor`/store layer), and Integration Event **contracts** must be defined in a place
that does not violate the Contracts-not-Messaging rule — this is an explicit decision point for
Phase 6.4 (see §12).

---

## 12. Phase 6 Gaps (current → Phase 6 specification)

| # | Gap | Phase that closes it |
|---|-----|----------------------|
| G1 | No Outbox persistence entity/EF mapping/index/migration in any module | 6.2 |
| G2 | Business State + Outbox are not yet written in the same module transaction; `DomainEventInterceptor.OnTransactionCompleted` is empty **and runs post-commit**, so Phase 6.3 must attach the Outbox insert to the same `SaveChangesAsync` (tracked entities) rather than the post-commit hook | 6.2 + 6.3 |
| G3 | No Domain Event → Integration Event mapping | 6.3 |
| G4 | No concrete Integration Event contracts; base contract unused; `EventMetadata` vs `IntegrationEvent` duplication unresolved | 6.4 |
| G5 | `IOutboxStore` has no claiming/recovery semantics; `GetPendingAsync` is naive | 6.5 |
| G6 | No RabbitMQ publisher, exchange/routing, serialization, confirms, metadata propagation | 6.6 |
| G7 | No Outbox Dispatcher background worker | 6.7 |
| G8 | No publication retry/failure-recovery behavior | 6.8 |
| G9 | No correlation/causation/observability across the async publication boundary | 6.9 |
| G10 | No end-to-end publication tests (success, broker-down, rollback, recovery, duplicates) | 6.10 |
| G11 | `RabbitMQConnectionProvider` singleton not registered for disposal; `MessageBrokerOptions.RetryCount` unused | 6.6/6.8/6.11 |
| G12 | Integration Event contract placement vs. the Contracts-not-Messaging architecture rule is **UNRESOLVED** (see below) | 6.4 (decision) |

**G12 — structural tension (must be decided in Phase 6.4, not now):** the architecture tests forbid
module *Contracts* projects from referencing `BuildingBlocks.Messaging`. If Phase 6.4 puts Integration
Event contracts in module Contracts and derives them from `IntegrationEvent`/`IIntegrationEvent`, that
rule breaks. Options include: (a) keep integration-event contracts in module Contracts as plain,
self-contained records and use `BuildingBlocks.Messaging` only as the transport/outbox metadata
carrier; or (b) relax/refine the architecture rule with an explicit decision. This is recorded as an
**open decision for Phase 6.4** and was **not** changed during the audit.

> **Phase 6.4 delta (not a historical rewrite).** G12 is **resolved** by decision **(b)** in
> `docs/adr/007-integration-event-contract-placement.md`: integration-event contracts live in module
> **Contracts** (`FluxCommerce.Modules.<Module>.Contracts.IntegrationEvents`) and derive from the
> canonical `IntegrationEvent` base. The architecture rules were refined so that module Domain and
> Application stay free of `BuildingBlocks.Messaging` and `RabbitMQ.Client`; Infrastructure stays free
> of `RabbitMQ.Client` (and reaches the contract-only Messaging metadata transitively via the Contracts
> events it serializes); Contracts may reference `BuildingBlocks.Messaging` but not `RabbitMQ.Client` or
> the Outbox; and positive "Contracts may reference Messaging" rules pin the decision.

---

## 13. Existing Abstractions to Reuse (do not reinvent)

| Abstraction | Location | Reused by |
|---|---|---|
| `OutboxMessage`, `OutboxStatus` | `BuildingBlocks.Outbox` | 6.2 (entity/EF), 6.5 |
| `IOutboxStore` | `BuildingBlocks.Outbox` | 6.2/6.5 (extend with claim/recovery) |
| `IOutboxDispatcher` | `BuildingBlocks.Outbox` | 6.7 |
| `IIntegrationEvent`, `IntegrationEvent`, `EventVersion` | `BuildingBlocks.Messaging` | 6.4 (subject to G12) |
| `IRabbitMQConnectionProvider`, `RabbitMQConnectionProvider`, `AddRabbitMQ` | `BuildingBlocks.Infrastructure` | 6.6 |
| `BaseDbContext`, `PersistenceConfigurationExtensions` | `BuildingBlocks.Persistence` | 6.2 (EF mapping/conventions) |
| `DomainEventInterceptor` (event capture; post-commit hook) | `BuildingBlocks.Persistence` | 6.3 (Outbox insert — but attach to the same save, not the post-commit hook; see §3.2 design caution) |
| `IDomainEventDispatcher` | `BuildingBlocks.Domain` | 6.3 (if in-process dispatch is chosen; currently unused) |
| `CorrelationIdMiddleware`, `ActivitySources` | `BuildingBlocks.Web` / `Observability` | 6.9 |
| `ICurrentUser`, `IDateTimeProvider` | `BuildingBlocks.Application` | 6.2+ (persistence conventions) |
| `IIdempotencyStore`, `IDuplicateDetector`, `ProcessedMessage` | `BuildingBlocks.Idempotency` | Phase 7 (out of 6 scope) |

---

## 14. Duplicate, Incomplete, Obsolete, or Conflicting Abstractions

| Item | Assessment |
|---|---|
| `EventMetadata` (Messaging) vs `IntegrationEvent`/`IIntegrationEvent` (Messaging) | **Duplicate/overlapping** — both model event metadata (`EventId`, `CorrelationId`, `CausationId`, `Version`, `OccurredAtUtc`); `EventMetadata` adds `EventType`/`CreatedAtUtc` but has **zero usages**. Not a conflict, but Phase 6.4 should select one canonical metadata carrier and retire or repurpose the other |

> **Phase 6.4 delta (not a historical rewrite).** Resolved in Phase 6.4: `IIntegrationEvent` +
> `IntegrationEvent` + `EventVersion` is the single canonical metadata model and `EventMetadata` was
> **removed** (zero usages; `EventType` is a per-contract constant, `CreatedAtUtc` is owned by the
> Outbox record). See `docs/adr/007-integration-event-contract-placement.md` and the Phase 6.4
> completion summary.
| `IDomainEventDispatcher` (Domain) | **Incomplete/unused** — defined, never implemented or registered. Leave as-is; its role is a Phase 6.3 decision. Do not add a second dispatcher abstraction |
| `RabbitMQConnectionProvider` disposal + `MessageBrokerOptions.RetryCount` | **Incomplete/obsolete** — `RetryCount` is not consumed; provider is not DI-disposed. Flag for Phase 6.6/6.8 |
| `BuildingBlocks.Messaging` + `BuildingBlocks.Outbox` + `BuildingBlocks.Idempotency` + `BuildingBlocks.Resilience` contracts | **COMPLIANT** — four narrow contract projects match ADR-003; no merging needed |
| No MediatR / FluentValidation / GenericRepository / GenericService / GenericController introduced | **COMPLIANT** — none found |
| No concrete Integration Events, no publisher, no dispatcher | **GAP, not a duplicate** — nothing to reconcile; Phase 6.2+ creates these |

No *conflicting* messaging abstractions (two competing implementations of the same contract) were
found. The only conflict-risk is G12 (contract placement).

---

## 15. Recommended Dependency Order for the Remaining Phase 6 Sub-phases

The order below matches `PHASE_6_OUTBOX_RABBITMQ.md` §20 and is expressed against the audited
foundation. Do not reorder without a documented architectural reason.

```text
6.1 Baseline & Audit (this document)          — DONE
        |
        v
6.2 Outbox Persistence Model                  — add Outbox entity + EF mapping in each SQL-backed
        |                                        module's Infrastructure; reuse OutboxMessage fields;
        |                                        new migration per module DbContext; no cross-module
        |                                        persistence coupling (G1)
        v
6.3 Domain Event -> Outbox Integration         — fill DomainEventInterceptor.OnTransactionCompleted;
        |                                        explicit mapping of selected events (OrderCreated,
        |                                        OrderCancelled, ReservationExpired); same-transaction
        |                                        guarantee (G2, G3)
        v
6.4 Integration Event Contracts & Versioning   — decide G12; canonical metadata contract; versioning;
        |                                        serialization contract (G4)
        v
6.5 Outbox Store & Claiming Semantics          — extend IOutboxStore with claim/recovery/expiry ops;
        |                                        concurrent-safe pending query (G5)
        v
6.6 RabbitMQ Publishing Infrastructure         — publisher abstraction; exchange/routing; serialization;
        |                                        confirms; channel lifecycle; reuse IRabbitMQConnectionProvider (G6, G11)
        v
6.7 Outbox Dispatcher Background Service       — background worker polling/claiming/publishing;
        |                                        graceful shutdown (G7)
        v
6.8 Retry & Failure Recovery                   — retryable vs non-retryable, backoff, crash recovery,
        |                                        no message loss (G8)
        v
6.9 Correlation, Causation & Observability     — HTTP -> Outbox -> Dispatcher -> RabbitMQ propagation;
        |                                        message-level structured logs (G9)
        v
6.10 End-to-End Integration Tests              — success / broker-down / rollback / recovery /
        |                                        multiple-messages / duplicate-publication scenarios
        |                                        against real RabbitMQ (G10)
        v
6.11 Architecture & Security Hardening         — re-run architecture tests; secrets/config review
        |
        v
6.12 Performance & Operational Hardening       — indexes, batching, channel reuse, backpressure
        |
        v
6.13 Documentation Synchronization             — ARCHITECTURE.md, PROJECT_CONTEXT.md, ADRs only if a
        |                                        decision changed
        v
6.14 Final Verification & Completion Report    — build + all test suites + PHASE_6_COMPLETION_REPORT.md
```

**Key sequencing rationale from the audit:** 6.2 must precede 6.3 because the same-transaction
guarantee requires a persistence model first. 6.4's contract decision (G12) must be made before 6.6
(wire/serialization) and 6.7 (dispatcher payload type). 6.5 claims semantics must exist before 6.7
(the dispatcher depends on safe claiming). Observability (6.9) depends on the publisher existing but
can be developed alongside 6.7/6.8.

---

## 16. Verification Results (Phase 6.1)

| Check | Result |
|---|---|
| `dotnet build FluxCommerce.slnx -c Debug` | **Passed — 0 warnings, 0 errors** |
| Architecture tests (`FluxCommerce.ArchitectureTests`) | **153 / 153 passed** |
| Unit tests (`FluxCommerce.UnitTests`) | **1,675 / 1,675 passed** |
| Integration tests (`FluxCommerce.IntegrationTests`) | **246 / 246 passed** against the live Docker environment (SQL Server 2022, Redis 8, RabbitMQ 4 — all healthy) |

No test or build behavior was changed by this audit. No production code was modified.

---

## 17. Scope Confirmation

- [x] Only Phase 6.1 executed.
- [x] No Outbox persistence changes (Phase 6.2) implemented.
- [x] No Domain Event → Integration Event mapping (Phase 6.3) implemented.
- [x] No Outbox claiming semantics (Phase 6.5) implemented.
- [x] No RabbitMQ publisher (Phase 6.6) implemented.
- [x] No Outbox Dispatcher (Phase 6.7) implemented.
- [x] No retry/recovery behavior (Phase 6.8) implemented.
- [x] No Phase 7 consumers / consumer idempotency / Inbox introduced.
- [x] No Saga / Payments / Stripe / compensation workflows introduced.
- [x] No new messaging abstractions invented; no architecture redesigned; module boundaries preserved.
- [x] Audit documentation created under `docs/` following the project's documentation conventions.

**Next step:** Phase 6.2 — Outbox Persistence Model.
