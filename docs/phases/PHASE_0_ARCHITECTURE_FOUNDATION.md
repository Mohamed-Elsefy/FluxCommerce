# Phase 0 — Architecture Foundation

## Status

Phase: 0
Name: Architecture Foundation
Status: Ready for Implementation

This document defines the complete scope and implementation requirements for Phase 0 of the Enterprise Commerce & Order Management Platform.

The AI coding agent MUST read and understand the following documents before implementing this phase:

1. `docs/current/PROJECT_CONTEXT.md`
2. `docs/current/ARCHITECTURE.md`
3. This document

`PROJECT_CONTEXT.md` is the highest-level project context and takes precedence over this document if there is any conflict.

---

# 1. Phase Objective

The objective of Phase 0 is to establish a production-oriented technical and architectural foundation for the entire Enterprise Commerce & Order Management Platform.

Phase 0 is NOT a business-feature implementation phase.

The goal is to create a clean, maintainable, extensible, testable, and architecture-enforced foundation that allows future phases to implement the core commerce workflows without requiring major architectural restructuring.

The system must start as a Modular Monolith.

The architecture must be designed so that individual modules can evolve independently and, if required in the future, be extracted into independent services with minimal architectural disruption.

The implementation must prioritize:

- Clear module boundaries
- Explicit ownership of business state
- Clean dependency direction
- Separation of concerns
- Testability
- Production-oriented infrastructure
- Observability
- Reliability foundations
- Architecture enforcement

---

# 2. Scope of Phase 0

Phase 0 includes the following sub-phases:

## 0.1 Architecture Decisions

Define and document:

- Modular Monolith architecture
- Clean Architecture principles
- DDD principles
- Module ownership rules
- Dependency rules
- Persistence boundaries
- Communication rules
- Messaging strategy
- Reliability strategy
- Observability strategy

Create Architecture Decision Records (ADRs) for important architectural decisions.

---

## 0.2 Solution and Project Structure

Create the initial production-oriented solution structure under `src/`.

The structure must support:

- API
- Shared Building Blocks
- Independent Modules
- Tests
- Future Workers / Background Processing

The structure must avoid a single giant shared Domain/Application/Infrastructure project for the entire system.

Each business module must be capable of owning its own:

- Domain
- Application
- Infrastructure
- Contracts

Shared code belongs in BuildingBlocks only when it represents genuinely cross-cutting technical or architectural concerns.

Do NOT move business logic into BuildingBlocks merely to avoid duplication.

---

## 0.3 Module Boundaries

Define and document the initial business modules / bounded contexts.

The core business modules are:

- Catalog
- Cart
- Orders
- Inventory
- Payments

Identity is treated as a Platform Capability rather than a core business bounded context.

The module boundaries must clearly define:

- Responsibility
- Owned business state
- Public contracts
- Allowed dependencies
- Forbidden dependencies

Modules must not directly modify another module's internal state.

Modules must communicate through explicit contracts and defined communication mechanisms.

---

## 0.4 Domain and Application Building Blocks

Create only the generic building blocks required by the architecture.

Potential building blocks include:

### Domain

- Entity
- Aggregate Root
- Value Object
- Domain Event
- Domain Event abstraction
- Domain error abstractions

### Application

- Command abstractions
- Query abstractions
- Handler abstractions where appropriate
- Result / Result<T>
- Error / ErrorType
- Application service abstractions
- Transaction abstractions where required

These abstractions must remain lightweight.

Do NOT create a large custom framework or over-engineered generic abstraction layer.

Do NOT introduce abstractions without a concrete architectural need.

---

## 0.5 Persistence Foundation

Establish the persistence foundation using:

- Entity Framework Core
- SQL Server

The persistence architecture must follow a Module-per-DbContext approach.

Conceptually:

    SQL Server
        |
        +-- Catalog DbContext
        +-- Cart DbContext
        +-- Orders DbContext
        +-- Inventory DbContext
        +-- Payments DbContext

The system may initially run against one SQL Server instance/database environment, but each module must maintain a clear persistence boundary.

A module must not directly access another module's DbContext.

A module must not directly write to another module's database state.

Each module owns its persistence model.

The implementation must prepare the solution for future database separation if required.

---

## 0.6 API Foundation

Establish the ASP.NET Core Web API foundation.

Phase 0 must include the infrastructure required for:

- Global exception handling
- RFC 7807-style ProblemDetails responses
- Consistent API error responses
- Request validation
- Correlation ID handling
- Request/response logging
- Health checks
- API versioning if selected by the architecture
- Consistent response conventions

Authentication and authorization business flows are NOT part of Phase 0.

Identity implementation belongs to the Identity / Platform Capability phase.

Phase 0 may prepare the API pipeline for future authentication and authorization, but must not implement the full Identity feature set.

---

## 0.7 Infrastructure Connectivity

Prepare development and infrastructure connectivity for:

- SQL Server
- Redis
- RabbitMQ

The purpose of Phase 0 is to establish reliable infrastructure configuration and connectivity foundations.

Do NOT implement business features using these technologies yet.

Examples of excluded functionality:

- No Cart implementation using Redis
- No RabbitMQ business consumers
- No Order events
- No Inventory events
- No Payment events
- No Saga orchestration

Infrastructure connectivity may include:

- Configuration
- Dependency injection registration
- Connection management
- Health checks
- Basic infrastructure abstractions

---

## 0.8 Messaging Contracts

Define the messaging architecture and contracts required by future phases.

The system uses RabbitMQ as the intended asynchronous messaging infrastructure.

The architecture must support:

- Integration Events
- Event metadata
- Event versioning
- Correlation ID
- Causation ID
- Event ID
- OccurredAtUtc

The messaging contract design must support At-Least-Once Delivery semantics.

Consumers must be designed as idempotent.

Phase 0 must define the contracts and architectural foundations.

Do NOT implement complete business event flows or Saga workflows in Phase 0.

---

## 0.9 Reliability Foundations

Prepare abstractions and architectural contracts for:

- Outbox Pattern
- Idempotency

The Outbox Pattern is required to support reliable publication of integration events after successful business transactions.

The design must preserve the following principle:

    Business State Change
            +
    Outbox Record
            |
        Same Transaction

The architecture must avoid the following unsafe flow:

    Database Commit
            |
            X
    Publish Message
            |
        Possible Failure

The system must assume At-Least-Once message delivery.

Therefore:

- Consumers must be idempotent
- Duplicate message delivery must be safely handled
- Event processing must be traceable
- Event identity must be explicit

Phase 0 should establish the contracts and technical foundation.

Full production Outbox dispatching and real idempotent consumers will be implemented when the relevant business modules and workflows are introduced.

---

## 0.10 Observability Foundation

Establish the observability foundation.

The architecture should support:

- Structured logging
- Correlation IDs
- Trace IDs
- OpenTelemetry
- Metrics
- Health checks
- Distributed tracing readiness

The system must be designed so that a future business workflow can be traced across:

    API
      |
      v
    Module
      |
      v
    Database
      |
      v
    Outbox
      |
      v
    RabbitMQ
      |
      v
    Consumer
      |
      v
    Another Module

Phase 0 establishes the foundation.

Detailed business workflow instrumentation will be added in later phases.

---

## 0.11 Dockerized Development Environment

Create a Docker-based local infrastructure environment.

At minimum, provide infrastructure for:

- SQL Server
- Redis
- RabbitMQ

The development environment must be reproducible.

Configuration must be externalized.

Secrets and environment-specific values must not be hardcoded into source code.

The environment should support:

- Local development
- Health checks
- Service connectivity
- Future CI/CD integration

The API may initially run directly from the developer machine while infrastructure dependencies run through Docker Compose.

---

## 0.12 Architecture Verification

Architecture rules must be enforceable through automated tests where practical.

Architecture tests should verify rules such as:

- Domain must not depend on Infrastructure
- Domain must not depend on API
- Application must not depend on API
- Modules must not depend on another module's Infrastructure
- Modules must not access another module's DbContext
- BuildingBlocks must not depend on business modules
- Business modules must not bypass their own Application/Domain boundaries

The goal is to prevent architectural erosion.

Architecture documentation alone is not sufficient.

Critical architectural rules should be verified automatically.

---

# 3. Explicitly Out of Scope

The following MUST NOT be implemented as business features during Phase 0:

- User registration
- Login
- JWT authentication
- Refresh tokens
- Role-based authorization implementation
- Email verification
- Password reset
- Product management
- Category management
- Product variants
- Product search
- Cart functionality
- Order creation
- Order lifecycle
- Inventory reservation
- Payment processing
- Stripe integration
- Saga orchestration
- Compensation workflows
- RabbitMQ business consumers
- Business integration events
- Checkout workflow

These features belong to later phases.

Phase 0 is strictly the architecture and technical foundation.

Do not implement fake business features merely to demonstrate the architecture.

If a minimal technical example is required to verify infrastructure, keep it isolated and clearly marked as infrastructure verification code.

---

# 4. Implementation Principles

The AI coding agent MUST follow these principles.

## 4.1 Do Not Over-Engineer

Do not build a custom framework on top of ASP.NET Core.

Prefer:

- Native ASP.NET Core capabilities
- EF Core
- Standard Dependency Injection
- Standard Middleware
- Standard Options Pattern
- Standard Health Checks
- Well-established libraries only when justified

---

## 4.2 Do Not Create Abstractions Without Purpose

Every abstraction must have a clear architectural reason.

Avoid:

- Generic repositories everywhere
- Generic services everywhere
- Generic managers
- Generic factories without domain justification
- Massive base classes
- Shared abstractions that leak module details

---

## 4.3 Preserve Module Ownership

A module owns its business state.

Never implement:

    Module A
        |
        +--> Module B DbContext
        |
        +--> Modify Module B Entity

Instead:

    Module A
        |
        +--> Public Contract
                |
                v
            Module B

Communication must happen through explicit contracts and defined mechanisms.

---

## 4.4 Prefer Explicit Boundaries

Prefer explicit dependencies over hidden coupling.

A dependency should be:

- Visible
- Intentional
- Testable
- Documented when architectural

---

## 4.5 Production-Oriented Code

The implementation must consider:

- Security
- Maintainability
- Testability
- Observability
- Failure handling
- Configuration management
- Dependency management
- Performance
- Scalability

Do not optimize prematurely, but do not create foundations that prevent future scalability.

---

# 5. Expected Phase 0 Deliverables

At the end of Phase 0, the repository should contain:

- A clean solution structure
- Modular architecture foundation
- BuildingBlocks
- API foundation
- Persistence foundation
- EF Core / SQL Server configuration
- Redis connectivity foundation
- RabbitMQ connectivity foundation
- Messaging contracts
- Outbox abstractions/contracts
- Idempotency abstractions/contracts
- Observability foundation
- Health checks
- Correlation ID support
- Global exception handling
- ProblemDetails
- Docker Compose infrastructure
- Architecture documentation
- ADR documentation
- Architecture tests
- Unit/integration test foundation where appropriate

The solution must build successfully.

Automated tests must pass.

Architecture tests must pass.

Infrastructure health checks must be functional.

The development environment must be reproducible.

No core business workflow should be implemented yet.

---

# 6. Definition of Done

Phase 0 is considered complete only when:

1. The solution builds successfully.
2. The architecture boundaries are clearly defined.
3. Module ownership rules are documented.
4. Dependency rules are documented and enforced where practical.
5. The API starts successfully.
6. SQL Server connectivity is verified.
7. Redis connectivity is verified.
8. RabbitMQ connectivity is verified.
9. Health checks correctly report infrastructure status.
10. Global exception handling is functional.
11. ProblemDetails responses are consistent.
12. Correlation IDs are supported.
13. Structured logging is functional.
14. OpenTelemetry foundation is configured.
15. Messaging contracts are defined.
16. Outbox contracts are defined.
17. Idempotency contracts are defined.
18. Docker Compose infrastructure starts successfully.
19. Architecture tests pass.
20. The solution contains no unauthorized business feature implementation.

---

# 7. Agent Execution Rules

Before writing code:

1. Read `PROJECT_CONTEXT.md`.
2. Read `ARCHITECTURE.md`.
3. Read this Phase 0 specification.
4. Inspect the existing solution structure.
5. Identify conflicts or ambiguities between existing files and this specification.
6. Do not silently change architectural decisions.
7. If a conflict exists, document it and choose the decision from the highest-priority project context.
8. Create or update architecture documentation and ADRs before implementing code where appropriate.
9. Implement Phase 0 incrementally.
10. After each major sub-phase, build and test the solution.
11. Do not proceed while introducing architectural violations.
12. Keep changes focused on Phase 0.

The agent must not implement later-phase business features unless explicitly instructed.

The agent must not replace the Modular Monolith with Microservices.

The agent must not introduce distributed deployment complexity prematurely.

The agent must preserve the ability to evolve the Modular Monolith toward independently deployable services in the future.

---

# 8. Recommended Execution Order

Execute Phase 0 in this order:

    0.1 Architecture Decisions
        |
        v
    0.2 Solution Structure
        |
        v
    0.3 Module Boundaries
        |
        v
    0.4 Domain/Application Building Blocks
        |
        v
    0.5 Persistence Foundation
        |
        v
    0.6 API Foundation
        |
        v
    0.7 Infrastructure Connectivity
        |
        v
    0.8 Messaging Contracts
        |
        v
    0.9 Reliability Contracts
        |
        v
    0.10 Observability
        |
        v
    0.11 Docker Environment
        |
        v
    0.12 Architecture Verification

The agent should work incrementally and verify the solution after each major stage.

Do not implement all components blindly in one large change.

---

# 9. Final Architectural Principle

The primary goal of Phase 0 is not to create a large amount of code.

The primary goal is to create a strong architectural foundation.

The architecture must make the correct implementation easy and the incorrect implementation difficult.

The system must be:

- Modular
- Explicit
- Testable
- Observable
- Reliable
- Maintainable
- Production-oriented

The project starts as a Modular Monolith.

Future distributed-systems capabilities such as RabbitMQ-based asynchronous messaging, Outbox processing, Idempotent Consumers, Saga Orchestration, and Compensation Workflows will be introduced progressively when the business workflows require them.

Phase 0 must prepare for those capabilities without prematurely implementing them.
