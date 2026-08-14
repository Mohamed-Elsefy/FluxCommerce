# Phase 6.2 — Outbox Persistence Model

> **Document purpose.** This is the Phase 6.2 deliverable from
> `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md` §6. It records what was implemented, the schema produced,
> the architecture rule change, the tests added, and the verification results. It follows the Phase 6.1
> audit (`docs/phases/PHASE_6_1_MESSAGING_AUDIT.md`) as the current-state baseline and deliberately does
> **not** implement Phase 6.3+ behavior.

- **Phase:** 6.2 — Outbox Persistence Model
- **Date:** 2026-08-14
- **Scope:** Strictly Phase 6.2. Persistence model only. No Domain Event → Outbox integration, no
  post-commit Outbox insertion, no claiming/recovery, no dispatcher, no publishing, no retry behavior.
- **Authoritative references:** `docs/PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`,
  `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md`, `docs/phases/PHASE_6_1_MESSAGING_AUDIT.md`, ADRs
  `001`–`005`.

---

## 1. Summary

Phase 6.2 establishes the persistent Outbox record in the two SQL-backed modules that Phase 6 requires
to publish integration events: **Orders** and **Inventory**. It reuses the existing
`FluxCommerce.BuildingBlocks.Outbox` contracts (`OutboxMessage`, `OutboxStatus`) without redefining
them, maps them into each module's existing DbContext (module-per-DbContext preserved), and ships one
migration per module that adds only the `OutboxMessages` table. No shared/global Outbox DbContext and
no cross-module foreign keys were introduced. Cart (Redis), Catalog, Identity and Payments persistence
boundaries are untouched.

The model is deliberately capable of participating in the same `SaveChangesAsync` as Business State in
Phase 6.3: it is a plain tracked entity inside the module DbContext, so a later Phase 6.3 write adds it
to the change tracker of the existing module save. No second transaction is created for Outbox
persistence.

## 2. Changes Made

### 2.1 Orders module

| File | Change |
|---|---|
| `src/Modules/Orders/...Orders.Infrastructure.csproj` | Added `ProjectReference` to `FluxCommerce.BuildingBlocks.Outbox` |
| `src/Modules/Orders/...Persistence/Configurations/OutboxMessageConfiguration.cs` | **New.** EF mapping for `OutboxMessage` → table `OutboxMessages` |
| `src/Modules/Orders/...DbContexts/OrdersDbContext.cs` | Added `DbSet<OutboxMessage> OutboxMessages` (and `using FluxCommerce.BuildingBlocks.Outbox.Messages`) |
| `src/Modules/Orders/...Persistence/Migrations/20260814153225_AddOutboxMessages.cs` (+ `.Designer.cs`) | **New.** Adds `OutboxMessages` table |
| `src/Modules/Orders/...Persistence/Migrations/OrdersDbContextModelSnapshot.cs` | Updated snapshot |

### 2.2 Inventory module

| File | Change |
|---|---|
| `src/Modules/Inventory/...Inventory.Infrastructure.csproj` | Added `ProjectReference` to `FluxCommerce.BuildingBlocks.Outbox` |
| `src/Modules/Inventory/...Persistence/Configurations/OutboxMessageConfiguration.cs` | **New.** EF mapping for `OutboxMessage` → table `OutboxMessages` |
| `src/Modules/Inventory/...DbContexts/InventoryDbContext.cs` | Added `DbSet<OutboxMessage> OutboxMessages` (and `using FluxCommerce.BuildingBlocks.Outbox.Messages`) |
| `src/Modules/Inventory/...Persistence/Migrations/20260814153225_AddOutboxMessages.cs` (+ `.Designer.cs`) | **New.** Adds `OutboxMessages` table |
| `src/Modules/Inventory/...Persistence/Migrations/InventoryDbContextModelSnapshot.cs` | Updated snapshot |

### 2.3 Architecture rules (tests)

- `Orders_ShouldNot_ReferenceOutbox` / `Inventory_ShouldNot_ReferenceOutbox`: scoped to the **protected
  layers** (Domain, Application, Contracts). The rules still forbid business layers from depending on
  the Outbox abstraction.
- Added positive rules `Orders_Infrastructure_IsAllowedToReferenceOutbox` /
  `Inventory_Infrastructure_IsAllowedToReferenceOutbox`: at least one Infrastructure type must depend on
  `FluxCommerce.BuildingBlocks.Outbox`, pinning the intentional Phase 6.2 dependency so it cannot be
  accidentally removed.
- The existing `Orders_Contracts_ShouldNot_ReferenceMessagingOrOutbox` /
  `Inventory_Contracts_ShouldNot_ReferenceMessagingOrOutbox` rules are unchanged (Contracts stay clean;
  the G12 contract-placement decision remains deferred to Phase 6.4).

> **Phase 6.4 delta (not a historical rewrite):** G12 was resolved in Phase 6.4 — Contracts host the
> module Integration Event contracts deriving from the contract-only `BuildingBlocks.Messaging` base,
> so these two Contracts rules were refined to forbid `RabbitMQ.Client` + the Outbox while allowing the
> Messaging base (see `docs/adr/007-integration-event-contract-placement.md`). The retained row above is
> the historical Phase 6.2 snapshot.

## 3. Persistence Model

`OutboxMessage` (from `FluxCommerce.BuildingBlocks.Outbox`, unchanged) is mapped **as-is** — no new
Outbox abstraction was introduced. The same configuration exists in each module's Infrastructure
assembly and is discovered by `BaseDbContext.OnModelCreating`
(`ApplyConfigurationsFromAssembly(GetType().Assembly)`), so each module owns its own table with zero
cross-module coupling.

### 3.1 Table and columns (`OutboxMessages`)

| Column | SQL Server type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK (client-generated `Guid`) |
| `EventId` | `uniqueidentifier` | No | |
| `Type` | `nvarchar(512)` | No | Event type identifier |
| `Payload` | `nvarchar(max)` | No | Serialized event payload (default unconstrained string) |
| `CorrelationId` | `uniqueidentifier` | No | |
| `CausationId` | `uniqueidentifier` | Yes | |
| `OccurredAtUtc` | `datetime2` | No | |
| `CreatedAtUtc` | `datetime2` | No | |
| `ProcessedAtUtc` | `datetime2` | Yes | Future publication/failure lifecycle |
| `Status` | `int` | No | `OutboxStatus` stored as int (`HasConversion<int>()`) |
| `RetryCount` | `int` | No | Retry metadata |
| `Error` | `nvarchar(max)` | Yes | Future failure lifecycle |

`Type`/`Payload`/`Error` use project conventions (`HasMaxLength` on `Type`; unconstrained strings map to
`nvarchar(max)`). Timestamps use the project `datetime2` convention and are explicitly configured.

### 3.2 Indexes

- `IX_OutboxMessages_Status_CreatedAtUtc` — `(Status, CreatedAtUtc)` supports the Phase 6.5 Pending
  selection/ordering query.
- `IX_OutboxMessages_Status_ProcessedAtUtc` — `(Status, ProcessedAtUtc)` supports the Phase 6.5 stale
  InProgress claim-recovery query.

The indexes **support** the future claim workflow; no claiming behavior was implemented (Phase 6.5).

### 3.3 Constraints

- Primary key `PK_OutboxMessages` on `Id`.
- No foreign keys, no navigations, no check constraints — the entity is fully isolated within each
  module database (verified by tests).
- No DB-level defaults; the writer (Phase 6.3+) always supplies values.

### 3.4 Migration correctness

Each migration (`20260814153225_AddOutboxMessages`) contains **only** the `CreateTable(OutboxMessages)`
and the two `CreateIndex` statements plus the `DropTable` in `Down`. Verified with
`dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the
last migration" for both modules.

## 4. DbContexts / Modules Affected

| Module | DbContext | DbSet added | Migration |
|---|---|---|---|
| Orders | `OrdersDbContext` | `OutboxMessages` | `20260814153225_AddOutboxMessages` |
| Inventory | `InventoryDbContext` | `OutboxMessages` | `20260814153225_AddOutboxMessages` |

Modules intentionally **not** affected: Cart (Redis-backed, no SQL DbContext), Catalog, Identity,
Payments (no Phase 6 messaging requirement; adding Outbox there would be premature).

## 5. Migrations Created / Applied

- Created via the project's documented workflow (`EF_CORE_COMMANDS.md`) using each module's
  design-time factory and user secrets.
- Applied to the dev SQL Server (`localhost:1433`, docker container `fluxcommerce-sqlserver`):
  - `FluxCommerce_Orders` — applied.
  - `FluxCommerce_Inventory` — applied.
- Verified directly against SQL Server: the `OutboxMessages` table, the PK, both indexes, and all column
  types/nullability match §3 exactly in both databases.

## 6. Tests Added / Updated

### 6.1 Unit tests (16 new — 8 per module)

- `tests/UnitTests/FluxCommerce.UnitTests/Persistence/OrdersOutboxPersistenceTests.cs`
- `tests/UnitTests/FluxCommerce.UnitTests/Persistence/InventoryOutboxPersistenceTests.cs`

Each covers: full-field round trip; all four `OutboxStatus` values round-trip and persist as their int
values; nullable fields (`CausationId`, `ProcessedAtUtc`, `Error`) persist as `NULL`; a large
(`> 200 KB`) serialized payload round-trips; table mapping to `OutboxMessages`; zero foreign keys /
navigations (module isolation); both status indexes present; SQL Server column types configured
(`uniqueidentifier`, `nvarchar(512)`, `nvarchar(max)`, `datetime2`, `int`) via the design-time model.

### 6.2 Integration tests (2 new — real SQL Server)

- `tests/IntegrationTests/FluxCommerce.IntegrationTests/InventoryOutboxPersistenceTests.cs` (uses the
  existing `SqlServerInventoryTestFixture`, which applies migrations via `Database.Migrate()`):
  - `OutboxMessage_RoundTrips_OnSqlServer` — full-field round trip on the real engine.
  - `OutboxMessage_Status_IsPersistedAsInt_OnSqlServer` — `Failed` persists as `3` and reads back as the
    enum.

### 6.3 Architecture tests (updated + 2 new)

- Scoped `Orders_ShouldNot_ReferenceOutbox` / `Inventory_ShouldNot_ReferenceOutbox` to
  Domain/Application/Contracts.
- Added `Orders_/Inventory_Infrastructure_IsAllowedToReferenceOutbox`.

Explicitly **not** added (out of scope): publishing, dispatcher, retry/recovery, Domain Event → Outbox
mapping, consumer, saga tests.

## 7. Verification Results

| Suite | Phase 6.1 baseline | Phase 6.2 result | Delta |
|---|---|---|---|
| Build (`FluxCommerce.slnx`, Debug) | 0 warnings / 0 errors | 0 warnings / 0 errors | none |
| Architecture tests | 153 / 153 | **155 / 155** | +2 (positive Infrastructure→Outbox rules) |
| Unit tests | 1,675 / 1,675 | **1,691 / 1,691** | +16 (Outbox persistence) |
| Integration tests (real SQL Server) | 246 / 246 | **248 / 248** | +2 (Outbox on SQL Server) |

All pre-existing tests still pass — no regressions.

## 8. Documentation Changes

- `docs/phases/PHASE_6_2_OUTBOX_PERSISTENCE.md` — this document (new).
- `docs/phases/PHASE_6_1_MESSAGING_AUDIT.md` — added a clearly-marked **"Phase 6.2 delta"** note in §4
  recording that the "Outbox store implementation" row is no longer a gap for Orders/Inventory and that
  the Outbox architecture rule was scoped to protected layers. Historical findings were **not**
  rewritten.
- `FluxCommerce.slnx` — solution file now lists `PHASE_6_OUTBOX_RABBITMQ.md` and
  `PHASE_6_1_MESSAGING_AUDIT.md` (consistent with how the PHASE 0–5 docs are listed).
- `ARCHITECTURE.md`, `PROJECT_CONTEXT.md`, and ADRs were **not** modified (no architectural fact that
  those documents must reflect was changed; Outbox persistence is an implementation detail of Phase 6).

## 9. Findings / Gaps Discovered

- **SQLite compatibility:** an explicit `.HasColumnType("nvarchar(max)")` breaks SQLite `EnsureCreated`
  (SQLite rejects the `(max)` type syntax). Resolved by relying on the EF Core default
  (`nvarchar(max)` for unconstrained strings on SQL Server), which is also more idiomatic. The produced
  SQL Server schema is identical.
- **NetArchTest positive-rule semantics:** `Types.InAssembly(...).Should().HaveDependencyOn(...)` asserts
  *every* type has the dependency; the correct "at least one type depends" form uses
  `.That().HaveDependencyOn(...)`.
- The Phase 6.1 critical caution still holds: the Outbox insert must be attached to the **same**
  `SaveChangesAsync` as Business State in Phase 6.3, **not** to the post-commit
  `DomainEventInterceptor.SavedChangesAsync` hook. Phase 6.2 did not wire any write.

## 10. Scope Confirmation

- ✅ Phase 6.2 only was implemented.
- ✅ No Phase 6.3 functionality (no Domain Event → Outbox integration, no mapping).
- ✅ No post-commit Outbox insertion (no write path exists yet).
- ✅ No Phase 6.4+ functionality (no integration-event contracts; G12 untouched).
- ✅ No Phase 6.5+ functionality (no claiming/recovery/dispatcher/retry).
- ✅ No Phase 7+ functionality (no consumers, Inbox, or consumer idempotency).
- ✅ No Saga / Payments / Stripe / compensation workflows.
- ✅ No new Outbox abstraction; `OutboxMessage` / `OutboxStatus` reused as-is.
- ✅ Module-per-DbContext preserved; no shared Outbox DbContext; no cross-module FKs.
- ✅ Cart stays Redis-backed; Catalog/Identity/Payments boundaries untouched.
- ✅ No unrelated production behavior changed.

## 11. Remaining Phase 6.3 Work

> **Phase 6.3 delta (2026-08-14):** the items below are now **implemented** in
> `docs/phases/PHASE_6_3_OUTBOX_INTEGRATION.md`. They are retained here as the historical Phase 6.2
> snapshot and are not rewritten.

- Domain Event → Outbox integration: attach the Outbox record to the same `SaveChangesAsync` as Business
  State inside the module transaction (not the post-commit hook).
- Domain Event → Integration Event mapping within the module boundary.
- Verification that Outbox rolls back with the business state and is not independently committed.

## 12. Next Step

**Phase 6.3 — Domain Event → Outbox Integration.**
