# Phase 6.3 — Domain Event → Outbox Integration

> **Document purpose.** This is the Phase 6.3 deliverable from
> `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md` §6. It records what was implemented, the mapping +
> correlation design, the tests added, and the verification results. It builds on the Phase 6.2
> persistence model (`docs/phases/PHASE_6_2_OUTBOX_PERSISTENCE.md`) and deliberately does **not**
> implement Phase 6.4+ behavior.

- **Phase:** 6.3 — Domain Event → Outbox Integration
- **Date:** 2026-08-14
- **Scope:** Strictly Phase 6.3. Domain Event → Integration Event mapping and writing `OutboxMessage`
  records in the **same** `SaveChangesAsync` (transaction) as Business State — atomic commit and atomic
  rollback. No integration-event contracts (G12 deferred to 6.4), no dispatcher, no claiming/recovery,
  no publishing, no retry, no consumers.
- **Authoritative references:** `docs/PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`,
  `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md`, `docs/phases/PHASE_6_1_MESSAGING_AUDIT.md`,
  `docs/phases/PHASE_6_2_OUTBOX_PERSISTENCE.md`, ADRs `001`–`005`.

---

## 1. Summary

Phase 6.3 wires the Outbox write path that Phase 6.2 deliberately left unwired. Each module
(**Orders**, **Inventory**) gains a module-scoped `SaveChangesInterceptor` that appends `OutboxMessage`
records **inside** `SavingChangesAsync` — the same save that writes the Business State — so both commit
or both roll back in a single transaction. A module `OutboxMessageFactory` performs the explicit
domain-event → integration-event mapping, and a shared `ICorrelationIdProvider` abstraction supplies
the outbox `CorrelationId` (web-backed in the API host, with a per-module fallback for background
services and tests). No post-commit hook is used and no second transaction is created.

The design decisions (user-confirmed) that shape this phase:

1. **CorrelationId source:** a new shared `ICorrelationIdProvider` abstraction in
   `BuildingBlocks.Application`. Domain events only carry `EventId` + `OccurredAtUtc` today, and modules
   are forbidden by architecture rule from referencing `BuildingBlocks.Web`, so the abstraction is the
   seam: the API host registers a web-backed implementation (reads the `X-Correlation-ID` assigned by
   `CorrelationIdMiddleware`), and each module registers a `Guid.NewGuid()` fallback so modules are
   self-contained in tests/fixtures. Last registration wins in the host.
2. **Outbox write mechanism:** a module-specific `SaveChangesInterceptor` wired into the module
   DbContext via the `AddDbContext` options action. This automatically covers every save path (checkout,
   cancellation, background expiry, and the explicit transaction inside `TryReserveAsync`).

## 2. Changes Made

### 2.1 Shared Building Blocks

| File | Change |
|---|---|
| `src/BuildingBlocks/FluxCommerce.BuildingBlocks.Application/Abstractions/ICorrelationIdProvider.cs` | **New.** `Guid GetCorrelationId();` |
| `src/BuildingBlocks/FluxCommerce.BuildingBlocks.Infrastructure/Services/GuidCorrelationIdProvider.cs` | **New.** Fallback implementation (`Guid.NewGuid()`); registered by each module so modules are self-contained. |
| `src/BuildingBlocks/FluxCommerce.BuildingBlocks.Web/Middleware/WebCorrelationIdProvider.cs` | **New.** Web-backed implementation; returns the id assigned by `CorrelationIdMiddleware` (`HttpContext.Items["CorrelationId"]` via `TryGetCorrelationId()`), `Guid.TryParse(...) ? parsed : Guid.NewGuid()` outside a request. Stateless → singleton. |

### 2.2 Orders module

| File | Change |
|---|---|
| `...Orders.Infrastructure/IntegrationEvents/OrderCreatedIntegrationEvent.cs` | **New.** Standalone record (`OrderId`, `CustomerId`, `OrderNumber`, `OccurredAt`), `EventType = "orders.order_created"`. |
| `...Orders.Infrastructure/IntegrationEvents/OrderCancelledIntegrationEvent.cs` | **New.** Standalone record, `EventType = "orders.order_cancelled"`. |
| `...Orders.Infrastructure/Outbox/OrdersOutboxMessageFactory.cs` | **New.** Explicit mapping for the two selected Orders domain events → `OutboxMessage`; unselected events produce no record. Injects `ICorrelationIdProvider`. |
| `...Orders.Infrastructure/Outbox/OrdersOutboxSaveChangesInterceptor.cs` | **New.** `SavingChangesAsync` collects domain events from tracked `IAggregateRoot`s and adds outbox records to `context.OutboxMessages`. Stateless → singleton. |
| `...Orders.Infrastructure/DependencyInjection.cs` | Registered `ICorrelationIdProvider → GuidCorrelationIdProvider` (fallback), `OrdersOutboxMessageFactory` and `OrdersOutboxSaveChangesInterceptor` as singletons; `AddDbContext` now two-arg with `.AddInterceptors(serviceProvider.GetRequiredService<OrdersOutboxSaveChangesInterceptor>())`. |

### 2.3 Inventory module

| File | Change |
|---|---|
| `...Inventory.Infrastructure/IntegrationEvents/InventoryReservationExpiredIntegrationEvent.cs` | **New.** Standalone record (`ReservationId`, `OrderId`, `Sku`, `Quantity`, `OccurredAt`), `EventType = "inventory.reservation_expired"`. |
| `...Inventory.Infrastructure/Outbox/InventoryOutboxMessageFactory.cs` | **New.** Explicit mapping for the selected `InventoryReservationExpired` domain event → `OutboxMessage`. |
| `...Inventory.Infrastructure/Outbox/InventoryOutboxSaveChangesInterceptor.cs` | **New.** `SavingChangesAsync` collects domain events and adds outbox records. Stateless → singleton. |
| `...Inventory.Infrastructure/DependencyInjection.cs` | Registered fallback correlation provider, factory and interceptor as singletons; both `AddInventoryInfrastructure` overloads wired via the two-arg `AddDbContext` with `AddInterceptors(...)`. |

### 2.4 API host

| File | Change |
|---|---|
| `src/FluxCommerce.Api/Extensions/DependencyInjection.cs` | Added `AddHttpContextAccessor()` and `AddSingleton<ICorrelationIdProvider, WebCorrelationIdProvider>()` **after** module DI (last registration wins over the per-module fallback). |

### 2.5 Architecture rules

No rule changes were required: module Infrastructure already references `BuildingBlocks.Application`,
`BuildingBlocks.Domain`, and (per Phase 6.2) `BuildingBlocks.Outbox`. The integration events are plain
standalone records in module Infrastructure with **no** `BuildingBlocks.Messaging` reference (the
Contracts clean-room rule `Contracts_ShouldNot_ReferenceMessagingOrOutbox` and the module
no-BuildingBlocks.Web rules remain untouched). Architecture test count is unchanged: **155 / 155**.

## 3. Mapping & Correlation Design

### 3.1 Outbox write path (atomic commit)

`OrdersOutboxSaveChangesInterceptor.SavingChangesAsync` (Orders) and
`InventoryOutboxSaveChangesInterceptor.SavingChangesAsync` (Inventory):

1. Guards on `eventData.Context is XxxDbContext`.
2. Collects `GetDomainEvents()` from every tracked `IAggregateRoot` entry in the change tracker.
3. Asks the module `OutboxMessageFactory` to map the selected domain events; each mapped record is
   added via `context.OutboxMessages.Add(...)` **before** the save executes.

Because the records are tracked entities inside the same `SavingChangesAsync`, the Business State and
the outbox record participate in one EF transaction: **commit or roll back together.** The interceptor
never writes from `SavedChangesAsync` (post-commit) and creates no second transaction. This satisfies
the Phase 6.1 critical caution recorded in §9 of the Phase 6.2 doc.

### 3.2 Outbox record mapping

| Field | Value |
|---|---|
| `Id` | `Guid.NewGuid()` |
| `EventId` | `domainEvent.EventId` |
| `Type` | integration-event const: `orders.order_created`, `orders.order_cancelled`, `inventory.reservation_expired` |
| `Payload` | `JsonSerializer.Serialize(integrationEvent)`; value objects mapped via `.Value`; default PascalCase |
| `CorrelationId` | `ICorrelationIdProvider.GetCorrelationId()` |
| `CausationId` | `null` |
| `OccurredAtUtc` | `domainEvent.OccurredAtUtc` |
| `CreatedAtUtc` | `DateTime.UtcNow` |
| `ProcessedAtUtc` | `null` |
| `Status` | `OutboxStatus.Pending` |
| `RetryCount` | `0` |
| `Error` | `null` |

Only the events the phase selected produce records; all other domain events flow through unchanged.
The factory maps `OrderCreated`, `OrderCancelled` (Orders) and `InventoryReservationExpired`
(Inventory) only.

### 3.3 Correlation id sources

- **HTTP request path:** `WebCorrelationIdProvider` returns the id `CorrelationIdMiddleware` stored in
  `HttpContext.Items["CorrelationId"]` (from the `X-Correlation-ID` header or freshly generated);
  falls back to a fresh `Guid` if the stored value is not a valid `Guid`.
- **Background / non-HTTP path** (e.g. `ReservationExpirationBackgroundService`): no `HttpContext`,
  so the provider returns a fresh `Guid`; the per-module `GuidCorrelationIdProvider` fallback exists so
  modules stay self-contained when the host has not overridden the abstraction.
- **Singleton safety:** provider, factory and interceptor are all stateless → safe as singletons
  (interceptor is resolved from the `AddDbContext` options `serviceProvider`).

### 3.4 DbContext / interceptor composition

`BaseDbContext.OnConfiguring` still adds the existing `DomainEventInterceptor`; the module
options action adds the module outbox interceptor. Both apply to every module save. Existing
constructor-based `new OrdersDbContext(...)` usages, design-time factories, and registration tests are
**unchanged** — the outbox interceptor is wired exclusively through DI (`AddDbContext` options action).

## 4. DbContexts / Modules Affected

| Module | DbContext | Wiring | Covered paths |
|---|---|---|---|
| Orders | `OrdersDbContext` | `OrdersOutboxSaveChangesInterceptor` via `AddDbContext` options action | checkout, cancellation, any store save |
| Inventory | `InventoryDbContext` | `InventoryOutboxSaveChangesInterceptor` via `AddDbContext` options action | reservation expiry (background service), `TryReserveAsync` explicit transaction |

No migration was required — the EF model is unchanged (`dotnet ef migrations has-pending-model-changes`
→ "No changes have been made to the model since the last migration" for both modules).

## 5. Tests Added

### 5.1 Unit tests (12 new)

| File | Facts |
|---|---|
| `.../Persistence/OrdersOutboxMessageFactoryTests.cs` | 4: maps `OrderCreated` + `OrderCancelled` to the correct `Type`/payload/`EventId`/timestamps; unselected events produce no record; `CorrelationId` from provider. |
| `.../Persistence/InventoryOutboxMessageFactoryTests.cs` | 3: maps `InventoryReservationExpired`; unselected events produce no record; provider correlation. |
| `.../Persistence/OrdersOutboxSaveChangesInterceptorTests.cs` | 3 (SQLite): tracked aggregate events are added to `OutboxMessages` during `SavingChangesAsync`; non-Orders context is ignored; no record for unselected events. |
| `.../Persistence/InventoryOutboxSaveChangesInterceptorTests.cs` | 2 (SQLite): event collected on save; non-Inventory context ignored. |

### 5.2 Integration tests (3 new — real SQL Server)

- `.../TestInfrastructure/SqlServerOrdersTestFixture.cs` + `SqlServerOrdersTestCollection.cs` —
  **new** fixture/collection (database `FluxCommerce_Orders`, `Database.Migrate()`, registers the
  `DomainEventInterceptor`).
- `.../OrdersOutboxIntegrationTests.cs` (uses the new Orders fixture):
  - `Checkout_PersistsOrderAndOrderCreatedOutbox_Atomically` — a successful checkout commits the
    Order **and** its `OrderCreated` outbox record in one transaction; record is `Pending`, correct
    `Type`/payload, non-empty `CorrelationId`.
  - `DuplicateCheckout_RollsBackOrderAndOutbox` — a duplicate checkout rolls the whole save back: no
    second order and **no** outbox record survives for the rejected event.
- `.../InventoryOutboxIntegrationTests.cs` (uses the existing `SqlServerInventoryTestFixture`):
  - `ExpiringReservation_PersistsOutbox_Atomically` — expiring a reservation persists exactly one
    `inventory.reservation_expired` outbox record (`Pending`, correct payload) in the same transaction
    as the status change.

## 6. Verification Results

| Suite | Phase 6.2 baseline | Phase 6.3 result | Delta |
|---|---|---|---|
| Build (`FluxCommerce.slnx`, Debug) | 0 warnings / 0 errors | 0 warnings / 0 errors | none |
| Architecture tests | 155 / 155 | **155 / 155** | none (no rule changes needed) |
| Unit tests | 1,691 / 1,691 | **1,703 / 1,703** | +12 (outbox factory + interceptor) |
| Integration tests (real SQL Server) | 248 / 248 | **251 / 251** | +3 (Orders outbox ×2, Inventory outbox ×1) |

All pre-existing tests still pass — no regressions.

**Flakiness note.** One full-suite run hit
`InventoryReservationExpirationTests.MultipleExpiredReservations_AllProcessed` (`Assert.True(expired >= 3)`)
as a **pre-existing, non-deterministic cross-collection race**: the API tests in the Identity
collection boot the full `Program` against the real `FluxCommerce_Inventory` SQL Server with the live
`ReservationExpirationBackgroundService`, so a background cycle can expire the fixture-seeded
reservations inside the seed→process window. `IdentityWebApplicationFactory` swaps only the Identity
DbContext to SQLite; it does not touch Inventory. The test passes in isolation (8/8) and the full suite
passes 251/251 on re-run. The race exists identically in the Phase 6.2 baseline (the background service
and the SQL Server fixture always shared the DB); it is unrelated to Phase 6.3 and is noted here rather
than fixed (out of scope).

## 7. Documentation Changes

- `docs/phases/PHASE_6_3_OUTBOX_INTEGRATION.md` — this document (new).
- `docs/phases/PHASE_6_2_OUTBOX_PERSISTENCE.md` — added a clearly-marked **"Phase 6.3 delta"** note in
  §11 recording that the "Remaining Phase 6.3 Work" items are now implemented. Historical text was
  **not** rewritten.
- `FluxCommerce.slnx` — solution file now lists `PHASE_6_3_OUTBOX_INTEGRATION.md`.
- `ARCHITECTURE.md`, `PROJECT_CONTEXT.md`, ADRs, and the Phase 6.1 audit were **not** modified (no
  architectural fact changed; the Outbox write path is an implementation detail of Phase 6, and the
  G12 contract-placement decision stays deferred to Phase 6.4).

## 8. Findings / Gaps Discovered

- **Correlation id gap:** domain events do not carry the correlation id, so the outbox
  `CorrelationId` is injected at mapping time via `ICorrelationIdProvider`. A web-backed implementation
  reads the middleware-assigned id; background work (e.g. the expiration service) gets a fresh `Guid`.
  Consider threading the correlation id through the command pipeline in a later phase.
- **Registering a SaveChangesInterceptor through `AddDbContext` optionsAction** is clean for
  constructor-based tests and design-time factories: no module DbContext constructor change, no
  `IServiceProvider` plumbing, no impact on direct-construction unit tests.
- **`WebCorrelationIdProvider` is registered after module DI** ("last registration wins"): module
  fallback remains available when a fixture or host omits the web provider.
- The pre-existing flaky race described in §6 is not introduced by Phase 6.3 (background cycle
  frequency is unchanged; the extra outbox insert per save does not change when cycles fire).

## 9. Scope Confirmation

- ✅ Phase 6.3 only was implemented.
- ✅ Domain Event → Outbox integration uses the **same** `SaveChangesAsync` as Business State (module
  `SaveChangesInterceptor`), never the post-commit `SavedChangesAsync` hook.
- ✅ Atomic commit and atomic rollback verified on real SQL Server (successful checkout commits Order +
  outbox; duplicate checkout rolls both back).
- ✅ No integration-event contracts (G12 untouched, deferred to Phase 6.4); 6.3 events are standalone
  records in module Infrastructure with no `BuildingBlocks.Messaging` reference.
- ✅ No Phase 6.5+ functionality (no claiming/recovery/dispatcher/retry).
- ✅ No Phase 7+ functionality (no consumers, Inbox, or consumer idempotency).
- ✅ No Saga / Payments / Stripe / compensation workflows.
- ✅ No new Outbox abstraction; `OutboxMessage` / `OutboxStatus` reused as-is; no migration needed.
- ✅ Module-per-DbContext preserved; no shared Outbox DbContext; no cross-module FKs.
- ✅ No unrelated production behavior changed.

## 10. Remaining Phase 6.4 Work

> **Phase 6.4 delta (not a historical rewrite):** the items below are now **implemented** and the
> Phase 6.3 event records were **moved** from module Infrastructure to module **Contracts**
> (`FluxCommerce.Modules.<Module>.Contracts.IntegrationEvents`) deriving from the canonical
> `IntegrationEvent` base (G12, `docs/adr/007-integration-event-contract-placement.md`). The outbox
> factories still produce identical records — same `Type`, same correlation/causation/timestamps, same
> serialized business payload — so the Phase 6.3 transactional Outbox behavior is unchanged. These
> items are retained here as the historical Phase 6.3 snapshot.

- Formalize integration-event contracts (G12): place the 6.3 event records as shared contracts and
  attach the metadata (type, schema) Phase 6.5 will need, within the architecture rules.
- Review correlation-id propagation options for the command pipeline.

## 11. Next Step

**Phase 6.4 — Integration Event Contracts & Metadata.**