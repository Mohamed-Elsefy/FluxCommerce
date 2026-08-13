# Refactoring Cycle 1 — Codebase & Architectural Hardening

## 1. Purpose

This document is the authoritative execution plan for **Refactoring Cycle 1** of FluxCommerce.

The refactoring cycle is a **standalone engineering cycle** after completion of Phase 5. It is **not a numbered business phase** and must not be inserted into the official phase sequence.

Current position:

```text
Phase 0 — Architecture Foundation
        ↓
Phase 1 — Identity & Access
        ↓
Phase 1.5 / 1.6 — Runtime + Localization
        ↓
Phase 2 — Catalog
        ↓
Phase 3 — Cart
        ↓
Phase 4 — Orders + Checkout
        ↓
Phase 5 — Inventory + Concurrency
        ↓
Refactoring Cycle 1  ← CURRENT
        ↓
Next Official Phase
```

The objective is to transform the codebase from a feature-complete implementation through Phase 5 into a **clean, consistent, verified, maintainable, and hardened baseline** for subsequent phases.

This cycle must improve the existing implementation without changing the project's architectural direction or introducing future-phase business functionality.

---

# 2. Source of Authority

Before performing any refactoring, the agent MUST read and understand:

1. `docs/PROJECT_CONTEXT.md`
2. `docs/ARCHITECTURE.md`
3. All applicable ADRs in `docs/adr/`
4. The completed-phase documentation relevant to the affected area
5. The Phase 5 specification and completion documentation
6. This document

### Precedence

When making decisions:

```text
PROJECT_CONTEXT.md
        ↓
ARCHITECTURE.md
        ↓
Applicable ADRs
        ↓
Completed Phase Specifications / Decisions
        ↓
This Refactoring Plan
        ↓
Existing Implementation
```

If the implementation conflicts with a higher-level architectural decision, the agent MUST NOT silently redefine the architecture.

The conflict must be identified, documented, and resolved according to the authoritative project documents.

---

# 3. Refactoring Objective

The objective is:

> Improve the internal quality of the existing implementation while preserving externally observable business behavior and architectural intent.

The refactoring cycle focuses on:

- Architectural consistency
- Responsibility boundaries
- Maintainability
- Code quality
- Dependency hygiene
- Persistence consistency
- API and contract quality
- Test quality
- Security hardening
- Performance review
- Documentation accuracy

The cycle is **not** a rewrite.

---

# 4. Core Refactoring Principles

The agent MUST follow these principles throughout the entire cycle.

## 4.1 Preserve Architecture

The project remains a:

- Modular Monolith
- Clean Architecture-based system
- DDD-oriented system
- Module-per-DbContext system

Do not replace the architecture with:

- Microservices
- Distributed deployment
- A shared monolith architecture
- A different persistence ownership model

---

## 4.2 Preserve Module Ownership

Each module owns:

- Its business state
- Its domain logic
- Its application use cases
- Its persistence model

A module MUST NOT directly access another module's:

- DbContext
- Domain entities
- Application internals
- Infrastructure implementations
- Internal repositories
- Internal services

Cross-module communication must continue to use explicit contracts or approved communication mechanisms.

---

## 4.3 Refactor, Do Not Redesign

A refactoring must solve an identified problem.

Do NOT change code merely because:

- Another pattern is fashionable
- Another framework supports it
- A different architecture looks cleaner
- Duplication exists in isolation
- A generic abstraction could theoretically be introduced

The agent must prefer the **smallest justified change**.

---

## 4.4 Do Not Invent Requirements

The agent MUST NOT infer new business requirements during refactoring.

If behavior is unclear:

1. Inspect project documentation.
2. Inspect tests.
3. Inspect ADRs and completed phase decisions.
4. If still ambiguous, document the ambiguity.
5. Do not silently choose new business behavior.

---

## 4.5 Preserve Behavior

Unless an explicit defect is identified and approved as part of hardening, refactoring must preserve:

- Business rules
- Error semantics
- API contracts
- Persistence semantics
- Transaction boundaries
- Concurrency guarantees
- Authorization behavior
- Localization behavior

---

## 4.6 Duplication Is Not Automatically a Problem

The project architecture explicitly prefers correct boundaries over artificial sharing.

Before consolidating duplicated code, determine:

- Is the concept genuinely identical?
- Is the behavior expected to evolve together?
- Is the abstraction cross-cutting?
- Would sharing create coupling between modules?

If not, keep the duplication.

---

## 4.7 BuildingBlocks Are Not a Dumping Ground

Only genuinely cross-cutting technical concepts belong in BuildingBlocks.

Do not move module-specific business logic into BuildingBlocks merely to remove duplication.

---

## 4.8 No Premature Optimization

Performance improvements must be evidence-based.

Prioritize:

- Correctness
- Clear responsibilities
- Maintainability
- Architectural integrity

before micro-optimizations.

---

# 5. Explicitly Out of Scope

Refactoring Cycle 1 MUST NOT introduce new business functionality.

The following are outside this cycle unless explicitly reactivated by the project owner:

- New business modules
- Payments implementation
- RabbitMQ business workflows
- Outbox dispatcher implementation
- Saga orchestration
- Compensation workflows
- New integration-event workflows
- Elasticsearch
- New checkout features
- New inventory capabilities
- New pricing capabilities
- Microservice extraction
- Distributed deployment
- Replacing EF Core
- Introducing MediatR
- Introducing FluentValidation
- Introducing a generic repository framework
- Introducing a new architecture pattern without documented justification

The goal is to improve the existing codebase, not expand its functionality.

---

# 6. Execution Model

The cycle follows:

```text
Baseline
   ↓
Audit
   ↓
Findings
   ↓
Targeted Refactoring
   ↓
Verification
   ↓
Hardening
   ↓
Final Verification
```

The agent MUST NOT start by making arbitrary code changes.

The first task is always to establish a baseline.

---

# R1.1 — Baseline & Safety Snapshot

## Objective

Capture the verified state of the codebase before any refactoring.

## Tasks

### R1.1.1 — Repository State

Inspect:

- Git branch
- Working tree
- Existing uncommitted changes
- Solution structure
- Current project files
- Existing documentation

Do not discard or overwrite unrelated user changes.

### R1.1.2 — Build Baseline

Run the complete solution build.

Record:

- Success/failure
- Errors
- Warnings
- Projects affected
- Build duration where useful

### R1.1.3 — Test Baseline

Run:

- Architecture tests
- Unit tests
- Integration tests

Record:

- Total tests
- Passed
- Failed
- Skipped
- Duration where useful

### R1.1.4 — Infrastructure Baseline

Verify required infrastructure used by existing tests:

- SQL Server
- Redis
- RabbitMQ where applicable

Do not introduce new infrastructure.

### R1.1.5 — Baseline Report

Create:

```text
docs/refactoring/REFACTORING_1_BASELINE.md
```

The report must record the actual observed baseline.

## Definition of Done

R1.1 is complete only when:

- The current repository state is understood.
- Build status is known.
- Test status is known.
- Infrastructure status is known.
- Baseline results are documented.

---

# R1.2 — Architecture & Dependency Audit

## Objective

Inspect the actual implementation against the project's architectural rules before changing code.

## R1.2.1 — Module Boundary Audit

Inspect:

- Catalog
- Cart
- Orders
- Inventory
- Payments
- Identity/platform capabilities where applicable

Verify:

- Module ownership
- Dependency direction
- Contracts usage
- Cross-module isolation
- Infrastructure isolation

## R1.2.2 — Layer Boundary Audit

For each applicable module verify:

```text
API
 ↓
Application
 ↓
Domain

Infrastructure
 ↓
implements required abstractions
```

Check for:

- Domain → Infrastructure dependencies
- Domain → API dependencies
- Application → API dependencies
- Application → Infrastructure implementation dependencies
- Business logic inside Infrastructure
- Business logic inside API

## R1.2.3 — Persistence Boundary Audit

Verify:

- One DbContext per module
- No cross-module DbContext access
- No cross-module EF relationships
- No cross-module foreign keys
- No shared business tables violating ownership
- No persistence shortcuts bypassing module boundaries

## R1.2.4 — Architecture Test Audit

Review existing architecture tests.

Determine whether critical rules discovered during implementation are actually enforced.

Do not add tests simply to increase test count.

Add rules only when a real architectural gap exists.

## R1.2.5 — Findings Register

Create:

```text
docs/refactoring/REFACTORING_1_FINDINGS.md
```

Each finding should contain:

```text
ID
Area
Location
Problem
Why it is a problem
Severity
Evidence
Recommended action
Risk
Status
```

Recommended severity:

- Critical
- High
- Medium
- Low
- Informational

## Important Rule

R1.2 produces the findings.

It does NOT require fixing every finding immediately.

The implementation phases that follow must use the findings register as their input.

## Definition of Done

- Architectural boundaries have been audited.
- Findings are documented.
- Findings are categorized.
- No major refactoring starts without an identified reason.

---

# R1.3 — BuildingBlocks & Shared Abstractions

## Objective

Ensure shared technical infrastructure remains minimal, justified, and correctly owned.

## R1.3.1 — BuildingBlocks Audit

Review:

- `BuildingBlocks.Domain`
- `BuildingBlocks.Application`
- `BuildingBlocks.Infrastructure`
- `BuildingBlocks.Persistence`
- `BuildingBlocks.Messaging`
- `BuildingBlocks.Outbox`
- `BuildingBlocks.Idempotency`
- `BuildingBlocks.Resilience`
- `BuildingBlocks.Observability`
- `BuildingBlocks.Web`
- `BuildingBlocks.Localization`

For each important abstraction ask:

1. Is it genuinely cross-cutting?
2. Is it used by multiple modules or architectural layers?
3. Does it contain business-specific knowledge?
4. Does it introduce unnecessary coupling?
5. Is its dependency direction correct?
6. Is its responsibility narrow and explicit?

## R1.3.2 — Abstraction Cleanup

Only when justified:

- Remove unused abstractions.
- Move misplaced technical abstractions.
- Simplify unnecessary abstractions.
- Split responsibilities that have clearly diverged.
- Correct dependency direction.

Do not consolidate module-specific concepts into BuildingBlocks.

## R1.3.3 — Dependency Cleanup

Inspect:

- Project references
- Package references
- Unused dependencies
- Incorrect dependency direction
- Duplicate technical dependencies

Remove only dependencies that are demonstrably unused or architecturally incorrect.

## R1.3.4 — Verification

After each meaningful change:

- Build
- Architecture tests
- Relevant unit tests

## Definition of Done

- BuildingBlocks contain only justified cross-cutting concerns.
- No business logic was moved into shared infrastructure.
- Dependencies are cleaner.
- Architecture tests remain green.

---

# R1.4 — Domain & Application Responsibility Refactoring

## Objective

Ensure business rules and use-case orchestration are located in the correct layers.

This phase must be performed module by module.

## R1.4.1 — Domain Audit

Review:

- Entities
- Aggregate Roots
- Value Objects
- Domain Events
- Domain Errors
- Invariants
- Domain services where present

Verify:

- Business invariants are protected by the owning aggregate/domain model.
- Domain objects do not depend on infrastructure.
- Domain logic is not duplicated in handlers/controllers.
- Value Objects correctly protect their invariants.
- Aggregate responsibilities remain explicit.

## R1.4.2 — Application Audit

Review:

- Commands
- Queries
- Handlers
- Application services
- DTOs
- Ports/interfaces
- Validation
- Mapping

Verify that handlers primarily:

```text
Receive request
    ↓
Validate / coordinate
    ↓
Call domain behavior
    ↓
Use application ports
    ↓
Return Result
```

Handlers must not become dumping grounds for business rules.

## R1.4.3 — Responsibility Corrections

Where findings justify it:

- Move business rules into Domain.
- Move orchestration into Application.
- Remove business logic from controllers.
- Remove business logic from Infrastructure.
- Simplify oversized handlers/services.
- Remove unnecessary indirection.

Do not introduce a new abstraction unless its responsibility is justified.

## R1.4.4 — Module Consistency Review

Compare implementations across:

- Catalog
- Cart
- Orders
- Inventory

Look for inconsistent patterns that create unnecessary cognitive overhead.

Consistency is desirable only where the underlying responsibility is actually equivalent.

## R1.4.5 — Verification

For every affected module:

- Unit tests
- Relevant integration tests
- Architecture tests

Business behavior must remain unchanged.

## Definition of Done

- Domain owns business invariants.
- Application owns use-case orchestration.
- Controllers remain thin.
- Infrastructure remains implementation-focused.
- No unnecessary abstraction was introduced.

---

# R1.5 — Infrastructure & Persistence Refactoring

## Objective

Improve persistence consistency, correctness, maintainability, and performance without changing business or transaction semantics.

## R1.5.1 — DbContext Review

For each module:

- Review DbContext configuration.
- Review registration.
- Review connection configuration.
- Review migrations configuration.
- Review interceptors.
- Review transaction boundaries.

Ensure each DbContext remains owned by its module.

## R1.5.2 — EF Configuration Review

Inspect:

- Entity configurations
- Value conversions
- Precision
- Constraints
- Indexes
- Required/optional fields
- Concurrency tokens
- Delete behavior

Remove accidental inconsistencies only when they conflict with the intended domain/persistence model.

## R1.5.3 — Query Review

Inspect for:

- N+1 queries
- Unnecessary materialization
- Premature `ToList`
- Unnecessary tracking
- Inefficient projections
- Repeated database calls
- Missing query constraints
- Incorrect query boundaries

Performance changes must be evidence-based and behavior-preserving.

## R1.5.4 — Transaction Review

Verify that:

- Transactions remain module-local.
- Business state and required persistence records maintain the intended atomicity.
- No distributed database transaction is introduced.
- No refactoring accidentally changes transaction scope.

## R1.5.5 — Inventory Concurrency Safety

Inventory requires special treatment.

Review:

- Atomic conditional reserve
- Concurrency token configuration
- Conditional updates
- Reservation state transitions
- Transaction boundaries
- SQL Server integration tests

The following invariant must remain protected:

```text
AvailableQuantity >= 0
```

The refactoring MUST NOT weaken:

- Overselling prevention
- Atomicity
- Concurrency guarantees
- Reservation correctness

Concurrency tests must remain against real SQL Server where the existing design requires it.

## R1.5.6 — Persistence Cleanup

Where justified:

- Remove dead persistence code.
- Simplify duplicated configuration.
- Correct naming.
- Remove unnecessary repository/adapter layers.
- Improve query shape.
- Improve indexes only when supported by actual access patterns.

Do not introduce a generic repository pattern.

## Definition of Done

- Persistence boundaries remain intact.
- SQL behavior remains correct.
- Inventory concurrency guarantees remain intact.
- Relevant integration tests pass.

---

# R1.6 — API & Contracts Hardening

## Objective

Ensure HTTP APIs and public module contracts represent clean, stable boundaries.

## R1.6.1 — API Review

Inspect:

- Controllers
- Request DTOs
- Response DTOs
- Routing
- Status codes
- ProblemDetails
- Error mapping
- Authorization
- Localization
- Swagger/OpenAPI

Controllers should remain thin:

```text
HTTP
 ↓
Application Use Case
 ↓
Result
 ↓
HTTP Response
```

## R1.6.2 — Error Handling Review

Verify:

- Stable machine-readable error codes
- Correct HTTP status mapping
- Localized user-facing text
- Consistent ProblemDetails
- Expected business errors are not implemented as exceptions
- Unexpected technical failures remain centrally handled

Do not change error codes without an explicit compatibility reason.

## R1.6.3 — Contracts Review

Review module contracts.

Contracts MUST NOT leak:

- EF Core
- DbContext
- Infrastructure implementations
- Internal entities
- Internal persistence types

Verify that contracts expose only what consumers legitimately require.

## R1.6.4 — Inventory Contract Review

Inventory contracts must remain suitable for internal Order/Saga-style use without unnecessarily exposing REST endpoints.

The current intended Inventory REST surface remains limited to the project decision:

```text
GET    /inventory/{sku}
POST   /inventory
PATCH  /inventory/{sku}
GET    /inventory/reservations/{id}
```

Reserve / Release / Commit contracts are internal integration surfaces unless the project specification explicitly changes.

Do not expand the API surface during refactoring.

## R1.6.5 — Verification

Run:

- API integration tests
- Relevant unit tests
- Architecture tests
- Swagger/OpenAPI verification where applicable

## Definition of Done

- API remains behavior-compatible.
- Controllers are thin.
- Contracts are implementation-independent.
- Error and localization conventions remain consistent.

---

# R1.7 — Test & Verification Hardening

## Objective

Improve the ability of the test suite to detect regressions introduced by future changes.

## R1.7.1 — Unit Test Review

Review:

- Domain tests
- Application tests
- Edge cases
- Failure paths
- Naming
- Test duplication
- Over-mocking
- Implementation-coupled tests

Tests should primarily protect behavior and invariants.

## R1.7.2 — Integration Test Review

Review tests involving:

- SQL Server
- Redis
- RabbitMQ where applicable
- EF Core
- Transactions
- Concurrency

Use real infrastructure where correctness depends on infrastructure semantics.

## R1.7.3 — Architecture Test Hardening

Add architecture tests only for meaningful uncovered rules.

Potential categories:

- Module isolation
- Layer boundaries
- Contract purity
- BuildingBlocks isolation
- DbContext ownership

## R1.7.4 — Regression Verification

Every refactoring performed in R1.3–R1.6 must have appropriate verification.

The agent must not rely only on compilation.

## Definition of Done

- Test suite protects important business and architectural invariants.
- Concurrency verification remains intact.
- Architecture rules are automatically enforced where practical.

---

# R1.8 — Final Hardening & Completion

## Objective

Perform the final quality pass and establish the post-refactoring baseline.

## R1.8.1 — Dead Code & Dependency Cleanup

Inspect for:

- Unused classes
- Unused methods
- Unused project references
- Unused package references
- Obsolete comments
- Temporary implementation artifacts
- Duplicate files

Remove only confirmed dead code.

## R1.8.2 — Naming & Consistency

Review:

- Class names
- Method names
- Namespace organization
- Folder structure
- DTO naming
- Command/query naming
- Contract naming
- Error naming

Do not rename public APIs unnecessarily.

## R1.8.3 — Security Review

Review:

- Authorization boundaries
- Sensitive data handling
- Logging
- Configuration
- Secret exposure
- Input handling
- Error leakage

Do not introduce new authentication or authorization features.

## R1.8.4 — Performance Review

Review only evidence-based concerns:

- Database round trips
- Query shape
- Materialization
- Tracking
- Redis usage
- Serialization
- Unnecessary allocations

Do not perform speculative micro-optimization.

## R1.8.5 — Documentation Synchronization

Update documentation only where implementation has materially changed.

Potential files:

```text
docs/ARCHITECTURE.md
docs/PROJECT_CONTEXT.md
docs/adr/*
docs/completed/*
docs/refactoring/*
```

Do not rewrite architectural decisions merely because code was cleaned up.

## R1.8.6 — Final Verification

Run the complete verification suite:

```text
Build
+
Architecture Tests
+
Unit Tests
+
Integration Tests
+
Infrastructure Verification
```

Compare the results against R1.1 baseline.

## R1.8.7 — Completion Report

Create:

```text
docs/refactoring/REFACTORING_1_COMPLETION_REPORT.md
```

The report must contain:

- Scope
- Completed refactorings
- Deferred findings
- Architecture verification
- Test results
- Build result
- Infrastructure result
- Important behavioral guarantees verified
- Remaining technical debt
- Final recommendation

## Definition of Done

Refactoring Cycle 1 is complete only when:

1. The solution builds successfully.
2. Architecture tests pass.
3. Unit tests pass.
4. Integration tests pass.
5. Inventory concurrency tests pass.
6. No module boundary violations remain.
7. No unintended business behavior changes were introduced.
8. BuildingBlocks remain technically scoped.
9. Contracts remain implementation-independent.
10. Documentation reflects the final implementation.
11. Remaining findings are explicitly documented.
12. A final completion report exists.

---

# 7. Refactoring Decision Framework

For every candidate change, the agent MUST classify it.

## KEEP

Use when the implementation is correct and aligned with the architecture.

```text
No change.
```

## REFACTOR

Use when the implementation is correct but responsibility, readability, maintainability, or consistency can be materially improved.

## REMOVE

Use when code is confirmed dead or an abstraction has no valid consumer.

## CONSOLIDATE

Use only when duplicated code represents a genuinely shared concept and consolidation does not create harmful coupling.

## STRENGTHEN

Use when the implementation is correct but an architectural/business invariant lacks sufficient automated protection.

## DEFER

Use when:

- The issue is low value.
- The change has high risk.
- It belongs to a future phase.
- Requirements are unclear.
- It would require architectural redesign.

---

# 8. Refactoring Safety Rules

The agent MUST follow these rules.

### Rule 1 — One logical concern at a time

Do not combine unrelated refactorings.

### Rule 2 — Verify frequently

After meaningful changes:

```text
Build
→ Tests
→ Architecture Tests
```

### Rule 3 — Do not hide behavior changes

If a change modifies business behavior, it is not a pure refactoring.

Document it explicitly.

### Rule 4 — Preserve public contracts

Do not break API or module contracts without explicit approval.

### Rule 5 — Protect concurrency

Inventory concurrency behavior has higher priority than stylistic improvements.

### Rule 6 — Do not introduce future-phase functionality

Refactoring must remain a refactoring cycle.

### Rule 7 — Do not optimize blindly

Performance changes require a concrete reason.

### Rule 8 — Prefer local simplicity

Do not introduce abstractions merely to eliminate a few duplicated lines.

### Rule 9 — Update tests with structural changes

Tests must move with the responsibility they verify.

### Rule 10 — Documentation must follow reality

If implementation changes an architectural fact, update the relevant documentation.

---

# 9. Expected Refactoring Artifacts

At minimum:

```text
docs/
└── refactoring/
    ├── REFACTORING_1_PLAN.md
    ├── REFACTORING_1_BASELINE.md
    ├── REFACTORING_1_FINDINGS.md
    └── REFACTORING_1_COMPLETION_REPORT.md
```

Additional decision records may be added if a significant architectural decision is discovered during the cycle.

---

# 10. Final Refactoring Philosophy

The purpose of this cycle is not to make the code look different.

The purpose is to make the architecture and implementation:

```text
Clear
+
Consistent
+
Correct
+
Testable
+
Maintainable
+
Observable
+
Safe to evolve
```

The most important principle is:

> **Do not refactor because code can be changed. Refactor because a concrete architectural, maintainability, correctness, security, or performance problem has been identified and the proposed change solves that problem with acceptable risk.**

The agent must finish the cycle with a stable baseline suitable for the next official project phase.
