# R1.2 — Architecture & Dependency Audit: Findings Register

- **Date:** 2026-08-13
- **Branch:** `refactoring-1`
- **Auditor scope:** R1.2 audit only. No code was modified. No new architecture was introduced.
- **Method:** Static analysis of solution project graph (`FluxCommerce.slnx`), assembly references, namespaces, and the existing architecture-test suite (`tests/Architecture/FluxCommerce.ArchitectureTests`, 128/128 passing per R1.1 baseline) against the rules in ADR-001..006, `docs/ARCHITECTURE.md`, `docs/PROJECT_CONTEXT.md`, and the completed phase documents.
- **Repo state during audit:** working tree clean except (a) untracked `docs/refactoring/` and (b) a user-owned modification to `FluxCommerce.slnx` adding the `docs/refactoring/` folder to the solution. Neither was altered.

## Summary

The architecture is sound. All rules that are enforced by the architecture-test suite currently hold (128/128 green). The audit found **no Critical or High severity issues** and no existing architectural violations. The findings below are predominantly **test-coverage gaps** (rules that are true today but not guarded by automated tests) and **dependency hygiene** items. Everything else is recorded as informational context (deliberate-by-design structures and documentation drift).

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 0 |
| Medium | 2 |
| Low | 4 |
| Informational | 5 |

## Correctly Enforced Rules (no action required)

Recorded as evidence that the intended architecture is real and enforced, not aspirational:

- **P-01 Module isolation.** Cross-module references exist only via approved Contracts: `Cart.Application → Catalog.Contracts`; `Orders.Application → Cart.Contracts + Catalog.Contracts`. No module references any other module's Domain, Application, or Infrastructure. Enforced per module (`*_ShouldNot_ReferenceOtherModules`, `*_ShouldNot_ReferenceOtherModulesDbContexts`, `*_ShouldNot_ReferenceCatalogInfrastructure`, etc.).
- **P-02 Layer purity.** Domain references only `BuildingBlocks.Domain`; EF Core, ASP.NET Core, and Localization are absent from Domain and Application in every module. Enforced by generic + per-module rules (`*_Domain_ShouldNot_ReferenceEfCore/AspNetCore/Localization`, `*_Application_ShouldNot_ReferenceEfCore/AspNetCore/Persistence`).
- **P-03 Persistence ownership.** `BaseDbContext` (BuildingBlocks.Persistence) contains only cross-cutting conventions and the soft-delete/audit interceptor — no business entities. Each module DbContext (Catalog, Identity, Inventory, Orders, Payments) is module-scoped. **No `CartDbContext` exists**; Cart is Redis-backed and EF Core is explicitly forbidden in Cart by tests. All `HasOne`/`HasForeignKey` relationships are internal to their module (OrderId, UserId, CategoryId/ProductId/VariantId) — there are no cross-module FKs.
- **P-04 Contract purity.** All six Contracts projects are leaf projects with zero project references and are guarded against module-layer / EF Core / ASP.NET Core / Messaging / Redis dependencies (Cart, Catalog, Orders, Inventory, Identity have explicit tests).
- **P-05 BuildingBlocks scoping.** BuildingBlocks are narrow, reference no modules, and contain no business logic. The generic architecture test enforces BuildingBlocks → no-module isolation.
- **P-06 Controller thinness.** All controllers (Catalog, Orders, Inventory, Cart, Identity) delegate to command/query handlers and contain no Infrastructure, DbContext, or `SaveChanges` access (verified by grep; enforced by test for Catalog only — see F-05).
- **P-07 Inventory atomicity & concurrency.** `InventoryStore.TryReserveAsync` performs an atomic conditional `UPDATE … WHERE ReservedQuantity + @qty <= OnHandQuantity` inside a module-local transaction guarded by the execution strategy, plus an optimistic-concurrency `Version` column (`SaveLifecycleChangesAsync` catches `DbUpdateConcurrencyException`). Concurrency behavior is exercised against real SQL Server by integration tests. Correctly scoped entirely within Inventory.
- **P-08 Composition root.** `FluxCommerce.Api` wires each module's Infrastructure registrations and hosts no business logic. Cross-module ports are implemented within the owning module: `CartCheckoutSnapshotProvider`/`CartCheckoutConsumption` live in Cart.Application and are registered by Cart.Infrastructure; `CatalogVariantCartLookup`/`CatalogVariantCheckoutLookup` live in Catalog.Application and use Domain repository ports.
- **P-09 Identity as platform capability.** Commerce modules consume only the shared `ICurrentUser` abstraction (`BuildingBlocks.Application`); nothing references the Identity implementation (enforced by `Cart_Application_UsesTheSharedICurrentUser_Abstraction` and generic isolation tests).
- **P-10 Per-module DI ownership.** Each module's Infrastructure registers its own handlers/stores/ports; shared `IUnitOfWork` is defined in `BuildingBlocks.Application`, implemented only by Identity.Infrastructure, and consumed only by Identity.Application — no duplicate registrations, no composition concern.
- **P-11 No infrastructure creep.** No `TODO/FIXME/HACK` markers exist in `src/`. Background services are correctly scoped (Inventory `ReservationExpirationBackgroundService` creates a per-cycle scope and invokes an idempotent use case).

## Findings

### R1.2-F-01 — Unused `BuildingBlocks.Messaging` project references
- **Area:** Dependencies / project hygiene
- **Location:** `FluxCommerce.Modules.Catalog.Infrastructure.csproj`, `FluxCommerce.Modules.Identity.Infrastructure.csproj`, `FluxCommerce.Modules.Payments.Infrastructure.csproj`, `FluxCommerce.Api.csproj`
- **Problem:** `BuildingBlocks.Messaging` is referenced by these four projects but no code in any of them uses a Messaging type.
- **Why it is a problem:** Dead references obscure the real dependency graph and can mask boundary drift (a future author may assume Messaging is available and used where it is not). This is exactly the kind of item R1.3.3 dependency cleanup should resolve.
- **Severity:** Low
- **Evidence:** grep across `src/` shows the references exist in the four csproj files; the only `FluxCommerce.BuildingBlocks.Messaging` usages in code are inside the Messaging project itself.
- **Recommended action:** Remove the unused `ProjectReference` from the four projects during R1.3.3 (verify no transitive need first).
- **Risk:** Low
- **Status:** **Resolved (R1.3)** — reference removed from `Catalog.Infrastructure`, `Identity.Infrastructure`, `Payments.Infrastructure`, and `FluxCommerce.Api`. Verified: solution build has no new errors (only the 4 pre-existing Integration Tests errors), Architecture tests 128/128, Unit tests 1675/1675.

### R1.2-F-02 — Unused `BuildingBlocks.Observability` project references in module Infrastructure
- **Area:** Dependencies / project hygiene
- **Location:** all six module Infrastructure projects (`Cart`, `Catalog`, `Identity`, `Inventory`, `Orders`, `Payments`)
- **Problem:** Each module Infrastructure csproj references `BuildingBlocks.Observability`, but no module code uses any Observability type (only `FluxCommerce.Api` uses it).
- **Why it is a problem:** Same as F-01; the Observability block is a composition-root concern today and the module-level references suggest capability that is not exercised.
- **Severity:** Low
- **Evidence:** grep across `src/` — Observability usage exists only in the API project.
- **Recommended action:** Remove the module-level references during R1.3.3, or justify their presence.
- **Risk:** Low
- **Status:** **Resolved (R1.3)** — reference removed from all six module Infrastructure projects (Cart, Catalog, Identity, Inventory, Orders, Payments); retained in `FluxCommerce.Api` (the only consumer). Verified: solution build has no new errors (only the 4 pre-existing Integration Tests errors), Architecture tests 128/128, Unit tests 1675/1675.

### R1.2-F-03 — No Payments-specific architecture tests
- **Area:** Architecture test coverage
- **Location:** `tests/Architecture/FluxCommerce.ArchitectureTests/` (no `PaymentsArchitectureTests.cs`)
- **Problem:** Payments is the only module without a dedicated architecture-test file. The generic tests do cover Payments for module isolation and DbContext residence, but Payments-specific rules (layer purity, contract purity) are unguarded.
- **Why it is a problem:** Payments is currently an empty skeleton (Domain/Application/Contracts contain 0 source files), so risk is minimal today — but once Payments is implemented (Phase 9+) its boundaries will be unprotected unless tests are added.
- **Severity:** Low
- **Evidence:** test directory listing — only Cart, Catalog, Identity, Inventory, Orders files exist.
- **Recommended action:** Add `PaymentsArchitectureTests.cs` mirroring the per-module pattern when Payments gains real code (fold into R1.7.3 test hardening).
- **Risk:** Low
- **Status:** **Resolved (R1.7)** — `PaymentsArchitectureTests.cs` added (module isolation, layer purity, DbContext residence; see R1.7 resolution notes).

### R1.2-F-04 — "Exactly one DbContext per module" is not enforced
- **Area:** Architecture test coverage
- **Location:** `tests/Architecture/FluxCommerce.ArchitectureTests/`
- **Problem:** Tests enforce that a module's DbContext(es) reside in `*.Infrastructure.DbContexts` and derive from `BaseDbContext`, and that modules do not access other modules' DbContexts — but no test asserts the count/ownership rule (one DbContext per SQL-backed module, and none for a Redis-only module like Cart).
- **Why it is a problem:** Single-DbContext-per-module is a stated architectural invariant (ADR-002); without a count assertion a second context could be introduced silently.
- **Severity:** Medium
- **Evidence:** `*_DbContext_ShouldResideInInfrastructure` and `*DbContext_ShouldDeriveFromBaseDbContext` tests assert location and base type only; current DbContext inventory is Catalog, Identity, Inventory, Orders, Payments (one each), Cart none.
- **Recommended action:** In R1.7.3 add a rule asserting each SQL-backed module's Infrastructure contains exactly one DbContext (and that Cart contains none).
- **Risk:** Low
- **Status:** **Resolved (R1.7)** — `SqlBackedModules_ShouldOwnExactlyOneDbContext_CartHasNone` and `AllModuleDbContexts_ShouldDeriveFromBaseDbContext` added (see R1.7 resolution notes).

### R1.2-F-05 — Controller-thinness rule enforced only for Catalog
- **Area:** Architecture test coverage
- **Location:** `CatalogArchitectureTests.cs` (`Catalog_Controllers_ShouldNot_ReferenceInfrastructure`, `Catalog_Controllers_Should_InheritApiControllerBase`)
- **Problem:** The thin-controller rule (no Infrastructure references, inherit `ApiControllerBase`) is asserted only for Catalog. Orders, Inventory, Cart, and Identity controllers have no equivalent test.
- **Why it is a problem:** Today the controllers are thin by inspection (verified), but a regression in any non-Catalog module would not be caught by the suite.
- **Severity:** Medium
- **Evidence:** grep of the test suite shows the `*_Controllers_*` rules exist only in `CatalogArchitectureTests.cs`; grep of `src/FluxCommerce.Api/Controllers/` shows zero Infrastructure/DbContext/SaveChanges references.
- **Recommended action:** Generalize the thin-controller rule to all modules in R1.7.3.
- **Risk:** Low
- **Status:** **Resolved (R1.7)** — `Controllers_Should_InheritApiControllerBase` and `Controllers_ShouldNot_ReferenceAnyModuleInfrastructure` now cover all controllers in the API assembly (see R1.7 resolution notes).

### R1.2-F-06 — No rule forbidding module layers from referencing `BuildingBlocks.Web`
- **Area:** Architecture test coverage
- **Location:** `tests/Architecture/FluxCommerce.ArchitectureTests/`
- **Problem:** `BuildingBlocks.Web` is the presentation/ApiController BuildingBlock. While `Application → ASP.NET Core` is forbidden (which is the main leak vector), nothing explicitly forbids module layers from referencing `BuildingBlocks.Web`.
- **Why it is a problem:** Minor hardening gap; prevents presentation abstractions leaking into modules.
- **Severity:** Low
- **Evidence:** no such rule in the suite; current graph contains no module → Web references.
- **Recommended action:** Optionally add the rule in R1.7.3.
- **Risk:** Low
- **Status:** **Resolved (R1.7)** — `ModuleLayers_ShouldNot_ReferenceBuildingBlocksWeb` added; no module layer references `BuildingBlocks.Web` today (see R1.7 resolution notes).

### R1.2-F-07 — Outbox / Idempotency / Resilience BuildingBlocks have zero consumers
- **Area:** BuildingBlocks / reliability contracts
- **Location:** `src/BuildingBlocks/FluxCommerce.BuildingBlocks.{Outbox,Idempotency,Resilience}/`
- **Problem:** These three projects are referenced by nothing in the solution.
- **Why it is a problem:** This is **by design** — ADR-004 establishes the reliability contracts in Phase 0 with implementations landing in later phases. They are not dead code and must not be removed.
- **Severity:** Informational
- **Evidence:** project-reference grep — no project references any of the three.
- **Recommended action:** Keep them; call this out in the R1.3.3 dependency review so they are not mistaken for dead dependencies.
- **Risk:** None
- **Status:** Closed (by design)

### R1.2-F-08 — Inventory integration contracts have no consumers yet
- **Area:** Contracts / forward contracts
- **Location:** `src/Modules/Inventory/FluxCommerce.Modules.Inventory.Contracts/Integration/InventoryOperations.cs`
- **Problem:** `ReserveInventoryRequest`, `ReleaseInventoryRequest`, `CommitInventoryRequest`, `InventoryReservationResult` are defined but nothing references them (Orders does not yet reference Inventory.Contracts; the saga is Phase 8).
- **Why it is a problem:** Not a violation — ADR-004 sanctions forward contracts and R1.6.4 intentionally limits the internal REST surface. Recorded for alignment when the Phase 8 saga lands.
- **Severity:** Informational
- **Evidence:** grep — definitions exist in the single file with no consumers.
- **Recommended action:** Keep; verify naming/semantics when Phase 8 wiring begins.
- **Risk:** None
- **Status:** Closed (by design)

### R1.2-F-09 — `InventoryReservationStatus` duplicated between Domain and Contracts
- **Area:** Contracts / duplication
- **Location:** `Inventory.Domain/Enums/InventoryReservationStatus.cs` and `Inventory.Contracts/Enums/InventoryReservationStatus.cs`
- **Problem:** Two enums with identical members; the Contracts copy is documented as a deliberate independent mirror so inter-module consumers never depend on Inventory internals (aligned with ADR-003's "duplication over coupling").
- **Why it is a problem:** Not a violation. Residual risk is drift between the two enums if a member is added/removed on one side only.
- **Severity:** Informational
- **Evidence:** both files exist; `InventoryController` maps Domain → Contracts statuses manually.
- **Recommended action:** Keep. Optionally add a reflection-based drift-protection test in R1.7.3.
- **Risk:** Low (drift)
- **Status:** **Resolved (R1.7)** — `InventoryReservationStatus_ContractsMirror_ShouldMatchDomain` added; Domain and Contracts member names and values are identical (see R1.7 resolution notes).

### R1.2-F-10 — ADR-002 documents a `CartDbContext` that does not exist
- **Area:** Documentation
- **Location:** `docs/adr/002-database-boundaries.md` (and echoed in `docs/PROJECT_CONTEXT.md` / `docs/ARCHITECTURE.md`)
- **Problem:** ADR-002 lists Cart among the modules owning a DbContext, but the Cart module is Redis-backed with no SQL DbContext (a Phase 3 decision).
- **Why it is a problem:** Documentation drift only; no code issue. The ADR's "one DbContext per module" wording is inaccurate for the Redis-backed module.
- **Severity:** Informational
- **Evidence:** no `CartDbContext` anywhere in `src/`; Cart EF Core usage is forbidden by tests.
- **Recommended action:** Update ADR-002 (or add an explicit exception note for Redis-backed modules) during the R1.8.5 documentation sync.
- **Risk:** None
- **Status:** **Resolved (R1.8)** — ADR-002 and `ARCHITECTURE.md` §9 corrected to reflect that SQL-backed modules own exactly one DbContext and Redis-backed modules (Cart) own none (see R1.8 resolution notes).

### R1.2-F-11 — Uncommitted user modification to `FluxCommerce.slnx`
- **Area:** Repository state
- **Location:** `FluxCommerce.slnx`
- **Problem:** The solution file carries an uncommitted working-tree change adding a `docs/refactoring/` folder (BASELINE + PLAN files).
- **Why it is a problem:** Not an architecture issue. Recorded to ensure the change is preserved and not clobbered by R1.3.x work; whether to commit is the user's call.
- **Severity:** Informational
- **Evidence:** `git status` → ` M FluxCommerce.slnx`; `git diff` shows only the added `docs/refactoring/` Folder entries.
- **Recommended action:** User to review and commit (or discard) at their discretion.
- **Risk:** None
- **Status:** Open (user-owned)

## Exit Criteria Check

- ✅ No Critical/High findings — architecture conforms to ADR-001..006 and the existing suite (128/128) green.
- ✅ All required finding fields present (ID, Area, Location, Problem, Why, Severity, Evidence, Recommended action, Risk, Status).
- ✅ Audit-only: no code modified; no new architecture introduced.
- ⚠️ R1.1 Integration Tests compile errors remain **untouched** as instructed (recorded in `REFACTORING_1_BASELINE.md`).
- ✅ Findings report created at `docs/refactoring/REFACTORING_1_FINDINGS.md`.

## R1.3 Resolution Notes (BuildingBlocks & Shared Abstractions)

- **R1.3.1 BuildingBlocks audit outcome:** All 11 BuildingBlocks were reviewed. Each holds only genuinely cross-cutting technical primitives (Domain primitives, Application command/query/result abstractions, Infrastructure connection providers + `DateTimeProvider`, Persistence `BaseDbContext`/interceptors/design-time helpers, Messaging event contracts, Outbox/Idempotency/Resilience reliability contracts, Observability OpenTelemetry config, Web controller/middleware/problem-details, Localization abstractions). No business logic resides in BuildingBlocks; dependency direction (BuildingBlocks → no modules) is enforced and green.
- **R1.3.2 Abstraction cleanup outcome:** Every shared abstraction (`ICommand`/`ICommandHandler`/`IQuery`/`IQueryHandler`, `IUnitOfWork`, `ICurrentUser`, `IDateTimeProvider`, `Result`/`Error`, `PagedResult`, `ICurrentCulture`/`ILocalizer`, Redis/RabbitMQ providers, Domain primitives) has active consumers across modules/layers. `IDomainEventDispatcher` (BuildingBlocks.Domain) is the only zero-consumer abstraction; **kept by explicit user decision** as a potential forward contract (the future outbox path uses `IOutboxDispatcher`/`IOutboxStore`).
- **R1.3.3 Dependency cleanup:** F-01 and F-02 resolved (see statuses above). No other demonstrably unused project references found in scope; non-Cart module Infrastructure references to `BuildingBlocks.Infrastructure` were left intact (not flagged findings; Cart legitimately uses Redis via it, and other modules may consume it in future phases).
- **R1.3.4 Verification:** Solution build — no new errors/warnings (only the 4 pre-existing `InventoryReservationExpirationTests.cs` errors from R1.1 remain, untouched). Architecture tests **128/128 passed**. Unit tests **1675/1675 passed** (including the real-Redis Cart fixture).
- **Outbox/Idempotency/Resilience preserved** as intentional future reliability contracts (F-07 remains Closed by design).

## R1.4 Resolution Notes (Domain & Application Responsibility Refactoring)

- **Date:** 2026-08-13
- **R1.4.1 Audit scope & method:** Reviewed, per module, the Domain/Application responsibility boundary against the R1.4 rules: Domain owns business rules, invariants, value-object validation, aggregate behavior, and domain events; Application owns orchestration, handlers, ports, contracts, and coordination; no business rules are duplicated or incorrectly implemented in handlers/controllers; no forbidden dependencies; shared abstractions come from the correct BuildingBlocks; patterns are consistent across modules. Static review of every implemented module's aggregates, value objects, handlers, services, mappers, and stores plus targeted grep for rule-like arithmetic/status logic in Application and Infrastructure.
- **R1.4.2 Per-module audit outcome — all clean, no violations:**
  - **Orders:** `Order` aggregate owns all invariants (≥1 line item, no duplicate variants, single currency, state transitions via `Place`/`Hydrate`/`AddLineItem`/`UpdateLineItemQuantity`/`RemoveLineItem`/`SetShippingCost`/`SetDiscount`/`Cancel`) and computes `Subtotal`/`GrandTotal` itself; `OrderLineItem` validates its own fields; value objects (`OrderMoney`, `OrderNumber`, `OrderAddress`) validate their own invariants. `CheckoutOrderSnapshotBuilder` and `CheckoutCommandHandler` are pure Application orchestration (authenticate → snapshot → batch-revalidate against Catalog → assemble snapshot → persist atomically with the idempotency record → best-effort cart consumption). Application fail-fast with Domain backstop is used consistently and is acceptable.
  - **Inventory:** `InventoryItem` owns stock invariants (available ≥ 0, `AddStock` ≥ 1, `Release`/`Commit` ≤ reserved); `InventoryReservation` owns lifecycle transitions (Pending→Committed/Released/Expired, `InvalidStateTransition`). Handlers (`Reserve`/`Release`/`Commit`/`Expire`) delegate rules to the Domain and do only input fast-fail + error mapping. `ExpiredReservationProcessor` (Application) orchestrates detection→transition→persist; the Infrastructure background service only hosts the loop (scope, logging, retry) — no business rules in Infrastructure.
  - **Identity:** `User` aggregate owns authentication, lockout, email-verification, password reset/change, refresh-token rotation, logout, and role assignment. `PasswordPolicy` lives in Application.Validation, which is correct: the Domain only ever sees a hash, so the complexity policy must live at the input boundary. Login/RegisterCustomer/ChangePassword handlers delegate to the Domain; the only Application-side check is AuthenticationResult/error mapping.
  - **Cart:** `Cart` aggregate owns quantity rules (≥1, quantity-limit overflow, no duplicate variants, merge rules incl. self-merge/overflow). `GuestCartMutationExecutor` and the authenticated executors are orchestration only (validate identifier → resolve → validate persisted ownership → hydrate → invoke Domain mutation → persist with optimistic concurrency). `CartVariantEligibilityValidator`/lookups are Application-level cross-module coordination.
  - **Catalog:** `Product` aggregate owns all invariants and state transitions (status/visibility, attribute-value validation against `AttributeDefinition`, specification/image collections); `Variant.Create` validates SKU/price. `CreateProductVariantsCommandHandler` does only fast-fail VO construction, duplicate-SKU detection (an input-set/row-level integrity concern, correct at the Application boundary), and `Variant.Create` delegation. `SearchCatalogProductsQueryHandler` is pure query passthrough (validation → repository → mapping).
- **R1.4.3 Consistency observations (informational, no action required):**
  - **Read-side projection duplication (Orders):** `OrderStore.GetMyOrdersPageAsync` recomputes GrandTotal (`Σ UnitPrice×Qty + Shipping − Discount`) in the SQL projection to avoid materializing line items on the list endpoint. This mirrors the Domain-owned `Order.GrandTotal` formula and is documented in both `OrderStore` and `OrderSummaryReadModel`. Accepted as standard CQRS read-model duplication; the two formulas must be kept in lockstep if pricing rules change.
  - **`Roles.cs` placement (Identity):** `Identity.Domain/Aggregates/Roles.cs` is a static constants class of role names, not an aggregate. It is correctly a Domain constant; the folder name is cosmetic only and is not worth a churn-only move.
- **R1.4.4 Verification:** Solution build — no new errors/warnings (only the 4 pre-existing `InventoryReservationExpirationTests.cs` errors from R1.1 remain, untouched). Architecture tests **128/128 passed**. Unit tests **1675/1675 passed**. (Note: one full-suite run produced transient failures in the real-Redis Cart fixture under parallel load; an immediate rerun was 1675/1675 green. R1.4 changed no source, so this is environmental flakiness, not a regression.)
- **R1.4.5 Findings status:** **No new findings** — the audit discovered no genuine responsibility violations and therefore no justifiable refactorings. No existing findings were resolved in R1.4; all F-01..F-11 statuses remain as recorded above. No behavior or public API changed.
- **R1.4.6 Conclusion:** R1.4 complete. Domain/Application responsibility separation is consistently correct across Orders, Inventory, Identity, Cart, and Catalog. The refactoring scope required no changes.

## R1.5 Resolution Notes (Infrastructure & Persistence Refactoring)

- **Date:** 2026-08-13
- **R1.5.1 DbContext review:** All five SQL-backed modules own exactly one DbContext deriving from `BaseDbContext` (Catalog, Identity, Inventory, Orders, Payments); Cart is Redis-backed with no DbContext (enforced by tests). Each module registers its own context in its own Infrastructure DI with `UseSqlServer` + retry-on-failure(3) + `CommandTimeout(30)` + module-scoped migrations history table and assembly. No cross-module DbContext access, relationships, or foreign keys exist (already enforced by architecture tests, 128/128 green). `BaseDbContext` conventions are limited to decimal precision (18,6) and `datetime2`; the SaveChanges interceptor is injected. Design-time factories are consistent across modules (per-module `Database:ModuleConnections:{Module}` key). Migrations are not auto-applied at startup (manual/CI-applied) — consistent, not a defect.
- **R1.5.2 EF configuration review:** Configurations are consistent: value-object conversions, `HasMaxLength` from domain constants, precision (18,6), unique Slug/Sku/OrderNumber/Email indexes, delete behaviors (`Restrict` for parent FKs, `Cascade` for product-owned collections), filtered `Include` in search, and concurrency tokens where the design requires them (`InventoryItem.Version`, `InventoryReservation.Version`). Orders' `Version` column is persisted but intentionally not an optimistic token (state-transition guards in the aggregate make concurrent transitions idempotent; adding a token would change SaveChanges behavior without business benefit). Check constraints on Inventory preserve `OnHandQuantity >= 0`, `ReservedQuantity >= 0`, `ReservedQuantity <= OnHandQuantity`, `Quantity > 0`.
- **R1.5.3 Query review:** Batch reads are used correctly across modules (no N+1 except the one fixed — see F-12). The Order list endpoint uses a documented two-step projection to stay SQLite-portable and never materializes line items. Lookups use `AsNoTracking` for reads and tracked loads only where a mutation follows. The cross-module Catalog lookups used by Cart/Orders resolve a requested batch with a bounded number of queries.
- **R1.5.4 Transaction review:** All transactions are module-local; no distributed transaction exists. Checkout persists the Order + idempotency record in one `SaveChangesAsync` (atomic) with duplicate-key recovery via the `(UserId, IdempotencyKey)` unique index. Inventory reservation operations run inside an execution-strategy-managed transaction (required because the retry strategy forbids user-initiated transactions outside it).
- **R1.5.5 Inventory concurrency safety — preserved unchanged:** `TryReserveAsync` keeps the atomic conditional `UPDATE … WHERE [ReservedQuantity]+@qty <= [OnHandQuantity]` inside a module-local transaction, `Version` increment in the same statement, re-load of the item, reservation insert, and single commit. `SaveLifecycleChangesAsync` keeps its optimistic-concurrency conflict handling (`DbUpdateConcurrencyException` → `ChangeTracker.Clear()` → false → surfaced as a conflict). The `AvailableQuantity >= 0` invariant, overselling prevention, atomicity, and expiration idempotency are untouched. No weakening.
- **R1.5.6 Persistence cleanup applied (small, local, justified):**
  - **F-12 resolved** — `CatalogVariantCartLookup` now loads variants and products via batch `GetByIdsAsync` instead of per-item `GetByIdAsync` loops (mirrors `CatalogVariantCheckoutLookup`), eliminating the N+1 on the Cart item-add eligibility path. Eligibility outcomes are identical (missing variants still map to `Missing` via dictionary miss).
  - **F-13 resolved** — added nonclustered indexes `IX_RefreshTokens_Value`, `IX_EmailVerificationTokens_Value`, `IX_PasswordResetTokens_Value` (Identity), which serve the `ITokenRepository` lookups by token value (the refresh-token hot path). Config in `UserConfiguration`, migration `20260813171639_AddTokenValueIndexes` generated and applied to the local `FluxCommerce_Identity` (verified in `sys.indexes`).
- **R1.5.7 DI registration/lifetime review:** Registrations are correct — DbContexts scoped, stores/repositories/UoW/handlers scoped, Cart Redis stack singletons (store, key generator, serializer, mapper) over a singleton connection provider, Identity stateless security services singletons (`PasswordHasher`, `TokenGenerator`, `JwtProvider`, `SmtpEmailSender`), options validated on start.
- **R1.5.8 Verification:** Solution build — no new errors/warnings (only the 4 pre-existing `InventoryReservationExpirationTests.cs` errors from R1.1 remain, untouched). Architecture tests **128/128 passed**. Unit tests **1675/1675 passed**. Integration Tests project remains **not runnable** (baseline compile errors) — the real-SQL-Server Identity migration was instead validated directly against the local `FluxCommerce_Identity` database. The Inventory SQL Server concurrency tests remain blocked by the baseline.
- **Findings status this cycle:** F-12, F-13 **Resolved**. F-14, F-15 **added (Informational, deferred)**. All F-01..F-11 statuses unchanged.

### R1.5-F-12 — N+1 queries in `CatalogVariantCartLookup`
- **Area:** Query efficiency
- **Location:** `src/Modules/Catalog/FluxCommerce.Modules.Catalog.Application/Services/CatalogVariantCartLookup.cs`
- **Problem:** `GetCartEligibilityAsync` loaded each requested variant and product with one `GetByIdAsync` per id in a loop, issuing ~2 queries per Cart item on the add-item eligibility path. The sibling `CatalogVariantCheckoutLookup` already used batch `GetByIdsAsync` and documented "never one query per Cart item" — the Cart path was inconsistent.
- **Why it is a problem:** N+1 round-trips to SQL Server per cart mutation; the inconsistency is unexplained.
- **Severity:** Low
- **Recommended action:** Load via `GetByIdsAsync` for both variants and products (mirroring the checkout lookup).
- **Risk:** Low (behavior preserved; AsNoTracking reads only)
- **Status:** **Resolved (R1.5)**

### R1.5-F-13 — Missing indexes on Identity token value columns
- **Area:** Query efficiency / indexes
- **Location:** Identity `RefreshTokens`, `EmailVerificationTokens`, `PasswordResetTokens` tables
- **Problem:** `ITokenRepository` performs lookups by token `Value` (`GetRefreshTokenByValueAsync`, `GetVerificationTokenByValueAsync`, `GetPasswordResetTokenByValueAsync`) but none of the three token columns had a supporting index, causing table scans on the refresh-token hot path (every `/auth/refresh`) and on verification/reset lookups.
- **Why it is a problem:** The token tables grow over time (rotation replaces tokens but does not purge them); scans degrade as they grow.
- **Severity:** Low
- **Recommended action:** Add nonclustered indexes on the three `Value` columns.
- **Risk:** Low (purely additive DDL)
- **Status:** **Resolved (R1.5)** — indexes added + migration `20260813171639_AddTokenValueIndexes`, applied and verified against the local `FluxCommerce_Identity`.

### R1.5-F-14 — `DomainEventInterceptor` registered both in DI and in `OnConfiguring`
- **Area:** Persistence / interceptors
- **Location:** `BaseDbContext.OnConfiguring` (BuildingBlocks.Persistence) + `DomainEventInterceptor` registered via `AddScoped<ISaveChangesInterceptor, …>` in the API host
- **Problem:** The interceptor is registered in the application service provider (which EF Core auto-discovers for DI-created contexts) *and* explicitly added in `OnConfiguring`, a redundant/double registration of the same scoped instance.
- **Why it is a problem:** Benign today — `SavingChangesAsync`/`SavedChangesAsync` capture and clear idempotently and the dispatch hook (`OnTransactionCompleted`) is a no-op placeholder for the future outbox. When the outbox hook is implemented (Phase 6+), a double registration risks dispatching domain events twice.
- **Severity:** Informational
- **Recommended action:** When the outbox dispatcher is implemented, confirm deduplication or remove one of the two registration paths (note the design-time factories rely on `OnConfiguring` to attach their `DesignTimeSaveChangesInterceptor`).
- **Risk:** None today
- **Status:** Open (deferred — confirm at outbox implementation)

### R1.5-F-15 — Refresh token retention (table growth)
- **Area:** Persistence hygiene
- **Location:** Identity `RefreshTokens` table
- **Problem:** Token rotation marks replaced tokens as replaced but never purges rows, so the table grows unbounded over the user base.
- **Why it is a problem:** Not a correctness defect; a future retention/housekeeping policy (e.g. purge replaced/expired tokens) would bound growth. The new `IX_RefreshTokens_Value` (F-13) mitigates the lookup cost meanwhile.
- **Severity:** Informational
- **Recommended action:** Add a token retention policy in a future phase; out of scope for R1.5.
- **Risk:** None
- **Status:** Open (deferred)

## R1.6 Resolution Notes (API & Contracts Hardening)

- **Date:** 2026-08-13
- **R1.6.1 API review (controllers):** All 12 controllers (Auth, Cart, 7 Catalog, Orders, Inventory) are thin — they construct commands/queries from requests, delegate to `ICommandHandler`/`IQueryHandler`, and map the `Result` to an `ApiResponse` envelope or ProblemDetails via `ApiControllerBase`. No controller contains business logic, DbContext access, or `SaveChanges` (verified by inspection; the thin-controller rule is enforced by architecture tests for Catalog — see F-05). Routes/verbs/status codes match the phase documentation: Inventory exactly the 4 intended endpoints (`POST /inventory`, `GET /inventory/{sku}`, `PATCH /inventory/{sku}`, `GET /inventory/reservations/{id}`, Admin-only), Orders/Catalog management `[Authorize]`, public reads `[AllowAnonymous]`, guest Cart endpoints anonymous. **No new endpoints were introduced; reserve/release/commit remain internal integration contracts only (no REST surface).**
- **R1.6.2 Error handling:** Consistent everywhere — `ApiControllerBase.MapError` maps `ErrorType` → HTTP status + ProblemDetails (localized title via `ILocalizer`, localized detail via `IErrorCodeLocalizer`), and `ExceptionHandler` centrally maps unexpected exceptions to ProblemDetails with localized titles. Expected business failures are returned as `Result` errors, not exceptions. REST-surfaced error codes (Catalog/Cart/Identity/Order/Inventory application codes) are all present in the `ErrorCodeLocalizer` map; domain backstop codes that never reach the API fall back to their English description by design. No error codes changed.
- **R1.6.3 Contracts purity:** All six Contracts projects remain leaf projects with zero project references and are guarded against module-layer/EF Core/ASP.NET Core/Messaging/Redis dependencies (architecture tests 128/128 green after R1.6 changes). Cross-module Contracts (Cart → Catalog, Orders → Cart + Catalog) expose only read models; the API boundary is the only consumer of request/response Contracts types. **One Domain leak into the public API was found and fixed (F-16).**
- **R1.6.4 Inventory contract surface:** Verified the REST surface is limited to the 4 Admin endpoints per R1.6.4. `InventoryOperations` (Reserve/Release/Commit requests + `InventoryReservationResult`) remain internal forward contracts with no consumers and no REST endpoints (F-08 stays Closed by design).
- **R1.6.5 Authorization consistency:** Inventory `Admin` policy, Catalog management `[Authorize]` (any authenticated role — documented Phase 2 decision), Orders `[Authorize]`, Cart auth/guest split, Auth anonymous/public endpoints — all match the completed-phase decisions. RBAC policies `Customer`/`SuperAdmin` remain registered-but-unattached (documented Phase 1 TD-2, not an R1.6 defect).
- **R1.6.6 Localization:** User-facing titles/details resolve through `ILocalizer` + `ErrorCodeLocalizer` with `Accept-Language` negotiation (en/fr/de/es/ar) documented in Swagger via `AcceptLanguageFilter`. No gaps found for REST-exposed codes.
- **R1.6.7 Cleanup applied (small, local, justified):**
  - **F-16 resolved** — the public search endpoint's `sortBy`/`sortDirection` query parameters used Domain enums (`CatalogProductSortBy`/`CatalogProductSortDirection`), leaking a Domain type into the HTTP contract. Added mirror enums in `Catalog.Contracts.Enums` and mapped Contracts → Domain in the controller (identical member names/values, so the wire format is unchanged), matching the existing `status`/`visibility` pattern on the same endpoint. Verified live: Swagger now documents `sortBy`/`sortDirection` against the Contracts schemas.
  - **F-17 resolved** — removed dead API response-mapping helpers in `BuildingBlocks.Web`: `ErrorResponse`, `ErrorExtensions.ToErrorResponse`, `ResultExtensions.ToApiResponse` (zero consumers across the whole solution; controllers use `ApiControllerBase` helpers instead). `ApiResponse.Fail`/`ApiResponse<T>.Failure` factory methods were retained as part of the response envelope's public surface.
- **R1.6.8 Verification:** Solution build — no new errors/warnings (only the 4 pre-existing `InventoryReservationExpirationTests.cs` errors from R1.1 remain, untouched). Architecture tests **128/128 passed**. Unit tests **1675/1675 passed**. Live smoke test: the API host starts (DI validation passes), `/swagger/v1/swagger.json` returns 200, and the `/catalog` search operation documents `sortBy`/`sortDirection` against the Contracts enums. Integration Tests project remains **not runnable** (baseline compile errors).
- **Findings status this cycle:** F-16, F-17 **Resolved**. F-18..F-21 **added (Informational, deferred)**. All F-01..F-15 statuses unchanged.

### R1.6-F-16 — Search endpoint leaks Domain sort enums into the public API
- **Area:** API / contracts boundary
- **Location:** `FluxCommerce.Api/Controllers/Catalog/ProductsController.cs` (`Search` action)
- **Problem:** `sortBy` and `sortDirection` query parameters were typed as Domain enums (`Catalog.Domain.Enums.CatalogProductSortBy`/`CatalogProductSortDirection`), while `status`/`visibility` on the same endpoint correctly used Contracts enums. This exposes a Domain type through the public HTTP contract (Swagger references the Domain enum) and couples the API surface to a Domain type.
- **Why it is a problem:** Contracts should define the public API vocabulary; Domain enums are internal and their members/values can evolve without API-compatibility constraints.
- **Severity:** Low
- **Recommended action:** Add mirror enums to `Catalog.Contracts.Enums` and map Contracts → Domain in the controller (as `status`/`visibility` are already mapped).
- **Risk:** None (identical member names/values; wire format unchanged)
- **Status:** **Resolved (R1.6)** — Contracts enums added (`CatalogProductSortBy`, `CatalogProductSortDirection`) and mapped in the controller; verified live via Swagger.

### R1.6-F-17 — Dead API response-mapping helpers in `BuildingBlocks.Web`
- **Area:** API / dead code
- **Location:** `BuildingBlocks.Web/Responses/ErrorResponse.cs`, `BuildingBlocks.Web/Extensions/ErrorExtensions.cs`, `BuildingBlocks.Web/Extensions/ResultExtensions.cs`
- **Problem:** `ErrorResponse` and the `ToErrorResponse`/`ToApiResponse` extension methods had zero consumers anywhere in the solution (controllers use `ApiControllerBase.Success`/`Created`/`NoContent`/`ErrorResult`).
- **Why it is a problem:** Dead API mapping surface obscures the real response path and invites future misuse; R1.3.2/R1.6.10 call for removing unused abstractions.
- **Severity:** Low
- **Recommended action:** Remove the three files.
- **Risk:** None (no consumers; verified by repository-wide grep)
- **Status:** **Resolved (R1.6)** — files removed; `ApiResponse.Fail`/`Failure` factories retained as part of the envelope's public surface.

### R1.6-F-18 — Checkout handler returns the Domain `Order` aggregate to the API
- **Area:** API / DTO boundary
- **Location:** `Orders.Application/Commands/Checkout/CheckoutCommandHandler.cs` (`ICommandHandler<CheckoutCommand, Result<Order>>`) + `OrdersController.Checkout`
- **Problem:** The Checkout command handler returns the Domain aggregate `Order`, and the API controller projects it into `CheckoutResponse` (`order.Id`, `order.OrderNumber`, `order.Status`, `order.CreatedAtUtc`). The parallel read paths (`GetOrder`/`GetMyOrders`) already return Contracts DTOs mapped in Application via `OrderResponseMapper`.
- **Why it is a problem:** The Application→API boundary exposes a Domain aggregate; the controller performs the Domain→DTO projection. Architecturally legal (Application references Domain; the projection is pure) but inconsistent with the read paths and couples the API to `Orders.Domain`.
- **Severity:** Informational
- **Recommended action:** Return `Result<CheckoutResponse>` from the handler (add `OrderResponseMapper.ToCheckout`) so the controller stays a pure passthrough. This changes the Application contract and requires reworking ~9 unit tests that assert on `Order` internals plus the blocked integration checkout tests.
- **Risk:** Medium (test churn; behavior identical)
- **Status:** Open (deferred — documented for a future cycle)

### R1.6-F-19 — Inconsistent Swagger `[ProducesResponseType]` metadata
- **Area:** Swagger / OpenAPI metadata
- **Location:** `FluxCommerce.Api/Controllers/` — Orders and Inventory controllers declare `[ProducesResponseType]`; the 7 Catalog controllers, Cart controller, and Auth controller declare none (Auth also lacks a `[Tags]` attribute).
- **Problem:** Response documentation in the OpenAPI document is inconsistent across modules: Orders/Inventory document success/error status codes; Catalog/Cart/Identity document none.
- **Why it is a problem:** Consumers of the generated OpenAPI document get incomplete response contracts for most endpoints.
- **Severity:** Informational
- **Recommended action:** Add `[ProducesResponseType]` (and `[Tags]` for Auth) across the remaining controllers in a future cycle; purely additive metadata, mechanical churn only.
- **Risk:** None
- **Status:** Open (deferred)

### R1.6-F-20 — Cart create endpoints accept an unused request body
- **Area:** API / request DTO
- **Location:** `CartController.CreateGuestCart`, `CartController.CreateAuthenticatedCart` (`[FromBody] CreateGuestCartRequest`, an empty record)
- **Problem:** Both create-cart endpoints accept a `CreateGuestCartRequest` body that is never read (commands carry no input). `[ApiController]` binding therefore requires clients to send a (ignored) JSON body.
- **Why it is a problem:** Misleading request contract; the documented endpoints (`POST /cart/guest`, `POST /cart`) imply a meaningful payload that is ignored.
- **Severity:** Informational
- **Recommended action:** Remove the unused body parameter (changes API semantics: body no longer required); or document that the body is reserved for future initial-items support.
- **Risk:** Low (removal changes the "missing body → 400" behavior)
- **Status:** Open (deferred)

### R1.6-F-21 — Phase docs reference an `/api` route prefix that controllers do not serve
- **Area:** API / documentation drift
- **Location:** Phase docs (`PHASE_5` `GET /api/inventory/{sku}`, etc.) vs actual routes (`[Route("inventory")]`, `[Route("cart")]`, `[Route("auth")]`, `[Route("orders")]`, `[Route("catalog/...")]`) — no global `api` prefix exists in `PipelineConfiguration`/`Program`.
- **Problem:** The served route set is consistently prefix-less, while several completed-phase documents (and the R1.6.4 plan text) use `/api/...`. Other phase docs (Phase 3/4 completion reports) use the prefix-less form, so the documentation is internally inconsistent.
- **Why it is a problem:** Consumers of the docs may call the wrong paths; the source of truth (implementation + Swagger) is prefix-less.
- **Severity:** Informational
- **Recommended action:** Align the docs to the actual prefix-less routes, or introduce a global `api` route prefix as a deliberate, breaking release change (not during refactoring).
- **Risk:** N/A
- **Status:** **Resolved (R1.8)** — phase specs (`PHASE_3_CART_MODULE.md`, `PHASE_4_ORDERS_CHECKOUT.md`), the `PHASE_3_COMPLETION_REPORT.md` prose, and the R1.6.4 surface listing in `REFACTORING_1_PLAN.md` aligned to the actual prefix-less routes (see R1.8 resolution notes).

## R1.7 Resolution Notes (Test & Verification Hardening)

- **Date:** 2026-08-13
- **R1.7.1 Approach:** Added architecture tests that **enforce the documented architecture as it exists today** — no production behavior, no new architecture, no test weakening. Only tests were added/changed; the only production-code change in this cycle was none. Every new rule was verified green against the current codebase before being committed, so the suite documents existing invariants rather than forcing refactors.
- **R1.7.2 Tests added (25 new architecture tests; 128 → 153):**
  - **Cross-cutting rules in `ArchitectureTests.cs`:**
    - `ModuleLayers_ShouldNot_ReferenceBuildingBlocksWeb` (**F-06 resolved**) — all module assemblies (Domain/Application/Infrastructure/Contracts of all six modules) must not depend on `FluxCommerce.BuildingBlocks.Web`. Verified: currently zero module → Web references (only `FluxCommerce.Api` and the test project reference `BuildingBlocks.Web`).
    - `Controllers_Should_InheritApiControllerBase` (**F-05**) — every `*Controller` in the API assembly must inherit `ApiControllerBase`.
    - `Controllers_ShouldNot_ReferenceAnyModuleInfrastructure` (**F-05 resolved**) — every `*Controller` must have no dependency on any `FluxCommerce.Modules.*.Infrastructure` namespace, for all six modules. Verified: all 12 controllers (Auth, Cart, 7 Catalog, Orders, Inventory) are thin; the existing Catalog-only tests are now redundant but retained.
    - `AllModuleDbContexts_ShouldDeriveFromBaseDbContext` (**F-04**) — every `*DbContext` in any module assembly must inherit `BaseDbContext` (closes the per-module gap for Catalog, Identity, Payments; Inventory/Orders already had direct-base assertions).
    - `SqlBackedModules_ShouldOwnExactlyOneDbContext_CartHasNone` (**F-04 resolved**) — each SQL-backed module (Catalog, Identity, Inventory, Orders, Payments) must own exactly one DbContext (per ADR-002), and the Redis-backed Cart module must own none. Verified against the current inventory: one each (Catalog, Identity, Inventory, Orders, Payments), zero for Cart.
  - **`PaymentsArchitectureTests.cs` (**F-03 resolved**):** New per-module file mirroring the established pattern — module isolation (no other module layers, no other modules' DbContexts), Domain purity (no Application/Contracts/Infrastructure/EF Core/ASP.NET Core/Localization/Persistence), Application purity (no Infrastructure/API/EF Core/ASP.NET Core/Persistence), Infrastructure (no API; `PaymentsDbContext` resides in `Infrastructure.DbContexts`), Contracts purity (no module layers/EF Core/ASP.NET Core). Payments' Domain/Application/Contracts are still empty skeletons (0 source files), so these rules pass vacuously today and protect the module as Phase 9+ code lands.
  - **`InventoryArchitectureTests.cs` (**F-09 resolved**):** `InventoryReservationStatus_ContractsMirror_ShouldMatchDomain` — reflection-based drift protection asserting the Contracts mirror enum has exactly the same member names **and** the same underlying values as the Domain enum (`Pending=0, Committed=1, Released=2, Expired=3`). Guards the documented duplication-over-coupling decision (ADR-003) against silent drift.
- **R1.7.3 Verification:**
  - Solution build: **no new errors/warnings** — only the 4 pre-existing `InventoryReservationExpirationTests.cs` errors from R1.1 remain (untouched, documented blocker).
  - Architecture tests: **153/153 passed** (baseline 128; +25 this cycle).
  - Unit tests: **1675/1675 passed** (unchanged).
  - Integration Tests project remains **not runnable** (baseline compile errors; not part of R1.7 scope).
- **Findings status this cycle:** F-03, F-04, F-05, F-06, F-09 **Resolved**. All other F-01..F-21 statuses unchanged. No new findings introduced.

## R1.8 Resolution Notes (Final Hardening & Completion)

- **Date:** 2026-08-13
- **R1.8.1 Dead code & dependency cleanup:** Re-verified the R1.3–R1.7 changes left no orphaned artifacts. No `TODO/FIXME/HACK` markers exist in `src/`; no `*.orig/.rej/.bak` files remain; the R1.6-removed `BuildingBlocks.Web` helpers (`ErrorResponse`, `ErrorExtensions`, `ResultExtensions`) are fully gone; no new unused project/package references were introduced since R1.3. The `ApiResponse.Fail` / `ApiResponse<T>.Failure` factories have zero consumers but remain **deliberately retained** per the documented R1.6-F-17 decision ("part of the response envelope's public surface") — not re-litigated in R1.8. Payments' Domain/Application/Contracts remain empty skeletons by design (no source files). **No dead code removed this cycle beyond the R1.6 cleanup.**
- **R1.8.2 Naming & consistency:** No renames performed — R1.4 and R1.6 already established consistency, and R1.8 does not rename public APIs without justification. Naming remains consistent across modules/controllers/DTOs/enums.
- **R1.8.3 Security review:** Repo-wide grep for hardcoded connection strings, passwords, and secrets in `src/` found **none**. Connection strings and secrets live in user secrets / `appsettings.Development.json` (outside source). Authorization boundaries were reviewed in R1.6.5 and remain unchanged. Error leakage is bounded by the central `ExceptionHandler` → ProblemDetails path (R1.6.2). **No security defects found.**
- **R1.8.4 Performance review:** Evidence-based concerns were already addressed in R1.5 (F-12 N+1 batch fix, F-13 Identity token-value indexes). No new evidence-based performance issues surfaced; no speculative micro-optimization performed.
- **R1.8.5 Documentation synchronization (2 findings resolved):**
  - **F-10 resolved** — corrected the `CartDbContext` documentation drift: `docs/adr/002-database-boundaries.md` now states that SQL-backed modules own exactly one DbContext (`CatalogDbContext`, `OrdersDbContext`, `InventoryDbContext`, `PaymentsDbContext`, `IdentityDbContext`) and that Redis-backed modules (Cart) own no SQL DbContext; the per-module database list now reads `FluxCommerce_Catalog/Orders/Inventory/Payments/Identity` (Cart's `FluxCommerce_Cart` SQL database does not exist). `docs/ARCHITECTURE.md` §9's persistence tree now lists only the five real DbContexts and documents the Redis-backed Cart exception. (`PROJECT_CONTEXT.md` already reflected reality and needed no change.)
  - **F-21 resolved** — aligned the `/api` route-prefix documentation drift to the actual prefix-less routes (source of truth: implementation + Swagger + completion reports): `PHASE_3_CART_MODULE.md` (§24 surface), `PHASE_4_ORDERS_CHECKOUT.md` (queries, cancellation, API-surface, and checkout-flow route listings), `PHASE_3_COMPLETION_REPORT.md` (controller prose), and the R1.6.4 surface listing in `REFACTORING_1_PLAN.md`. The finding's own record text is retained as a historical note.
- **R1.8.6 Final verification:**
  - Solution build: **no new errors/warnings** — only the 4 pre-existing `InventoryReservationExpirationTests.cs` errors from R1.1 remain (untouched, original baseline blocker).
  - Architecture tests: **153/153 passed** (baseline R1.1: 128; final: 153).
  - Unit tests: **1675/1675 passed** (unchanged from R1.1).
  - Integration Tests: **attempted — not runnable**, failing to compile on exactly the same 4 R1.1 baseline errors in `InventoryReservationExpirationTests.cs` (CS1026×2 at 32/132, CS1003 at 132, CS1519 at 135). **No new integration failures introduced**; the Inventory SQL Server concurrency tests remain blocked by the baseline.
  - Working tree: reviewed via `git diff`/`git status` — changes are exactly the R1.3–R1.7 refactorings plus this cycle's two documentation fixes. The user-owned `FluxCommerce.slnx` modification (adding `docs/refactoring/` to the solution) is preserved untouched.
- **Findings status this cycle (final):** F-10, F-21 **Resolved**. All other F-01..F-21 statuses unchanged. **No new findings introduced.** This is the final status pass of Refactoring Cycle 1 — see `REFACTORING_1_COMPLETION.md`.