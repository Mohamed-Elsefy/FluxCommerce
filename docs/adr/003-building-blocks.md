# ADR-003: BuildingBlocks Responsibility and Dependency Boundaries

## Status

Accepted

## Context

Shared code is necessary for cross-cutting technical concerns (domain primitives, application abstractions, persistence support, messaging contracts, observability), but misplaced shared code becomes a dumping ground that couples modules to each other and hides business logic inside generic projects. The specification distinguishes clearly between shared technical infrastructure and module-owned business logic.

The boundary rules are documented:

- `ARCHITECTURE.md` §4 — "BuildingBlocks contains genuinely cross-cutting technical primitives... Only concepts that are truly shared across multiple modules should be placed here. Do not place business-specific logic in BuildingBlocks. Do not use BuildingBlocks as a dumping ground. Do not create shared abstractions merely because two modules currently have similar code. Duplication is sometimes preferable to incorrect coupling."
- `PROJECT_CONTEXT.md` §16 — BuildingBlocks contains Domain, Application, Infrastructure, Messaging, Outbox, Idempotency, Observability, and Resilience; "Do not create shared abstractions simply to avoid duplicating a few lines of code. Shared code must represent a genuinely shared concept."
- `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.2 — "Shared code belongs in BuildingBlocks only when it represents genuinely cross-cutting technical or architectural concerns. Do NOT move business logic into BuildingBlocks merely to avoid duplication."
- `ARCHITECTURE.md` §5 — the dependency direction is API -> Application -> Domain, with Infrastructure implementing abstractions required by Application and Domain.
- `ARCHITECTURE.md` §23 — architecture rules must be enforced, not merely documented.

## Decision

FluxCommerce centralizes cross-cutting technical primitives in dedicated BuildingBlocks projects, each with a narrow responsibility:

- `BuildingBlocks.Domain` — entities, aggregate roots, value objects, domain events (module-independent primitives only).
- `BuildingBlocks.Application` — command/query/handler abstractions, Result/Error abstractions, and shared application ports (e.g., `ICurrentUser`, `IDateTimeProvider`, `IUnitOfWork`).
- `BuildingBlocks.Infrastructure` — shared infrastructure implementations (Redis connection provider, RabbitMQ connection provider, `DateTimeProvider`).
- `BuildingBlocks.Persistence` — `BaseDbContext`, save-changes/domain-event interceptors, design-time helpers.
- `BuildingBlocks.Messaging` — integration event contracts, metadata, event versioning.
- `BuildingBlocks.Outbox` / `BuildingBlocks.Idempotency` / `BuildingBlocks.Resilience` — standalone contracts for the reliability foundation.
- `BuildingBlocks.Observability` / `BuildingBlocks.Web` — cross-cutting platform capabilities.
- `BuildingBlocks.Localization` — framework-independent localization abstractions (`ILocalizer`, `ICurrentCulture`) and the shared resource repository. The ASP.NET Core localization implementation (`IStringLocalizer<SharedResources>`) lives only inside this block; modules depend on the abstractions only. Added in Phase 1.6 (see `docs/IMPLEMENTATION_PLAN.md#phase-16--internationalization-foundation-` and `ARCHITECTURE.md` §26).

Additional rules:

- BuildingBlocks must never reference business modules.
- Business logic must never be moved into BuildingBlocks to avoid duplication.
- Modules reference BuildingBlocks for technical primitives only; module-owned business rules stay inside the module.
- The boundaries are enforced by automated architecture tests (BuildingBlocks -> Modules is forbidden, Domain -> Infrastructure is forbidden, Application -> API is forbidden, modules must not reference another module's Infrastructure).

## Consequences

Positive:

- Shared code has a clear justification and an explicit owner.
- Modules depend on stable technical primitives instead of each other's internals.
- Architecture tests can enforce that BuildingBlocks stays free of module references.

Negative:

- Duplication across modules is tolerated and sometimes preferred, which may look wasteful in the short term.
- BuildingBlocks projects require discipline: without review and tests they could grow into a shared dumping ground.
- Moving an abstraction into BuildingBlocks creates a strong commitment, since many modules depend on it.

## Alternatives Considered

- A single giant shared project containing all Domain/Application/Infrastructure code — rejected in `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.2 ("The structure must avoid a single giant shared Domain/Application/Infrastructure project for the entire system").
- Moving duplicated business logic into BuildingBlocks — rejected in `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.2 and `ARCHITECTURE.md` §4 ("Do not place business-specific logic in BuildingBlocks"); duplication is explicitly preferred over incorrect coupling.
