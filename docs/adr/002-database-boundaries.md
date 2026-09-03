# ADR-002: Module-per-DbContext Persistence Boundaries

## Status

Accepted

## Context

Business modules must own their state. If all modules shared one persistence model or one DbContext, module boundaries would exist only in documentation and extraction of any module into an independent service would be blocked by entangled schema and access patterns. The specification requires explicit persistence ownership even while the system initially runs against a single SQL Server environment.

The module ownership and database rules are documented:

- `PROJECT_CONTEXT.md` §4.3 — "A module must never directly modify another module's database state."
- `PROJECT_CONTEXT.md` §17 — "Prefer Module-per-DbContext. Modules should have strong persistence boundaries even when they initially share the same physical SQL Server database."
- `ARCHITECTURE.md` §9 — "The persistence model follows Module-per-DbContext... Each DbContext belongs to one module. No module may access another module's DbContext. No cross-module EF Core relationships. No cross-module foreign keys. No shared business entity tables across module boundaries."
- `ARCHITECTURE.md` §10 — the logical ownership boundaries must remain explicit to allow evolution toward multiple module databases and independent service databases.
- `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.5 — "A module must not directly access another module's DbContext."

## Decision

FluxCommerce uses a Module-per-DbContext persistence architecture:

- Each SQL-backed module owns exactly one DbContext (`CatalogDbContext`, `OrdersDbContext`, `InventoryDbContext`, `PaymentsDbContext`, plus `IdentityDbContext` for the Identity platform capability). Redis-backed modules own no SQL DbContext — Cart state lives in Redis (Phase 3 decision).
- Each module DbContext inherits from a shared `BaseDbContext` that provides only cross-cutting persistence conventions (e.g., precision/column-type conventions, save-changes interceptors), never module data.
- Modules never reference another module's DbContext, entities, or schema.
- No cross-module EF Core relationships or foreign keys exist.
- Modules initially connect to the same SQL Server instance but keep per-module database boundaries (`FluxCommerce_Catalog`, `FluxCommerce_Orders`, `FluxCommerce_Inventory`, `FluxCommerce_Payments`, `FluxCommerce_Identity`), prepared for later separation.
- Information needed across modules must come through contracts, queries, cached projections, or integration events (`ARCHITECTURE.md` §9), not through direct database access.

## Consequences

Positive:

- Module isolation and state ownership are structurally guaranteed at the persistence layer.
- Future evolution to multiple databases or extracted services is a deployment change, not a refactoring.
- Cross-module bugs (foreign keys, shared tables, hidden coupling) are prevented by design.

Negative:

- Each module duplicates small amounts of persistence setup (DbContext configuration, connection handling) rather than sharing them; this duplication is accepted as the cost of ownership.
- Cross-module data requirements must be served through contracts/events, which is more indirection than direct query access.
- The shared `BaseDbContext` must remain free of business data to avoid becoming an implicit shared schema.

## Alternatives Considered

- A single shared DbContext for the whole system — implicitly excluded by the module ownership principles (`PROJECT_CONTEXT.md` §4.2, §4.3) and by the explicit Module-per-DbContext mandate; the source documentation does not record it as a considered option.
- The documentation prescribes Module-per-DbContext directly (`PROJECT_CONTEXT.md` §17, `ARCHITECTURE.md` §9) and does not document other considered persistence alternatives.
