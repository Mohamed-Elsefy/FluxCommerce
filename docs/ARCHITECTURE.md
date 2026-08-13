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