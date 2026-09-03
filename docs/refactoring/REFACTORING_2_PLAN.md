# FluxCommerce — Refactoring Cycle 2 (R2) Execution Plan

**Status:** Planned  
**Type:** Independent Refactoring Cycle  
**Position:** After Phase 11 and before Phase 12  
**Primary Goal:** Stabilize, simplify, reorganize, and harden the existing architecture without changing business behavior.

---

## 1. Purpose

Refactoring Cycle 2 (R2) is an independent engineering cycle performed after completion of Phase 11.

The purpose of R2 is to improve the structure and maintainability of the existing FluxCommerce implementation after the addition of the reliability infrastructure introduced across Phases 6–11:

- Outbox and RabbitMQ messaging
- Idempotent consumers
- Order Saga orchestration
- Stripe Payments
- Compensation
- Reservation expiry
- Related persistence and integration-event infrastructure

R2 is a **refactoring cycle, not a feature phase**.

The target is:

> Reorganize, simplify, and harden the architecture that already exists while preserving business behavior, contracts, reliability semantics, and architectural decisions.

---

# 2. Position in the Project Roadmap

```text
Phase 9 — Stripe Payments
        ↓
Phase 10 — Payment Saga + Compensation
        ↓
Phase 11 — Reservation Expiry
        ↓
══════════════════════════════
Refactoring Cycle 2 (R2)
══════════════════════════════
        ↓
Phase 12 — Failure Testing
```

R2 must be completed before Phase 12 so that failures discovered during Phase 12 can be attributed to failure behavior rather than to an unstable refactoring state.

---

# 3. Source of Authority

Before making changes, review and use the following project sources:

1. `docs/PROJECT_CONTEXT.md`
2. `docs/ARCHITECTURE.md`
3. `docs/adr/*`
4. Phase 6–11 documentation
5. Existing completion reports
6. Refactoring Cycle 1 documentation
7. `REFACTORING_2_PLAN.md`

When architectural decisions conflict, do not silently replace an existing decision. Document the conflict and resolve it explicitly.

---

# 4. Objectives

R2 focuses on:

- Structural organization
- File placement
- Folder and namespace consistency
- Module boundaries
- Layer boundaries
- Responsibility ownership
- BuildingBlocks hygiene
- Outbox duplication analysis
- Idempotency duplication analysis
- Saga maintainability
- Payments isolation
- Persistence consistency
- Transaction boundaries
- API and contract quality
- Architecture test protection
- Behavioral regression protection
- Dead-code cleanup
- Dependency hygiene
- Documentation synchronization

---

# 5. Explicitly Out of Scope

The following are prohibited during R2 unless separately approved as a new project decision:

- New business features
- Phase 12 failure-testing implementation
- Elasticsearch
- Microservices migration
- MediatR introduction
- FluentValidation introduction
- Generic Repository introduction
- Distributed transactions
- Business behavior redesign
- Unnecessary architectural pattern introduction
- Speculative performance optimization

R2 must not become Phase 12 implemented early.

---

# 6. R2 Principles

## 6.1 Preserve Behavior

Refactoring must preserve:

- Business rules
- State transitions
- Saga behavior
- Compensation behavior
- Inventory concurrency semantics
- Idempotency semantics
- Outbox semantics
- Transaction boundaries unless explicitly justified
- Public API behavior
- Integration contracts

---

## 6.2 Responsibility Before Location

A type must not be moved merely because another folder appears cleaner.

For every questionable file, determine:

1. What responsibility does the type have?
2. Which layer owns that responsibility?
3. Which module owns the concept?
4. Is the namespace consistent with the responsibility?
5. Is the physical location appropriate?
6. Is the type incorrectly grouped with unrelated types?

Only then decide whether the file should be moved.

---

## 6.3 Duplication Is Not Automatically a Problem

Do not consolidate code solely because implementations look similar.

The decision must consider:

- Behavioral equivalence
- Dependency coupling
- Transaction requirements
- Future evolution
- Change frequency
- Ownership
- Maintainability
- Architectural coupling

Possible decisions:

- `KEEP`
- `CONSOLIDATE`
- `REFACTOR`
- `MOVE`
- `REMOVE`
- `STRENGTHEN`
- `DEFER`

---

## 6.4 Large Files Are Not Automatically Bad

A large class such as `OrderSaga` must not be split merely because of its line count.

Splitting is justified only when analysis demonstrates:

- Distinct responsibilities
- Low cohesion
- Poor testability
- Difficult transition discovery
- Clearly separated change boundaries

If a split is performed, behavior must remain unchanged.

---

# 7. R2.1 — Baseline & Safety Snapshot

## Objective

Capture the exact state of the project before any refactoring.

## Activities

Inspect:

- Git status
- Solution structure
- Project count
- Project references
- Package references
- Source tree
- Existing namespaces
- Docker/infrastructure configuration

Run:

```bash
dotnet restore
dotnet build FluxCommerce.slnx -c Debug
dotnet test
```

Run separately where applicable:

- Architecture tests
- Unit tests
- Integration tests

Verify development infrastructure:

- SQL Server
- Redis
- RabbitMQ
- Docker
- EF Core migrations

## Record

- Build result
- Warnings
- Errors
- Architecture test result
- Unit test result
- Integration test result
- Project count
- Known findings
- Infrastructure state

## Output

```text
docs/refactoring/REFACTORING_2_BASELINE.md
```

## Rule

R2.1 is observation-only.

No refactoring should be performed during the baseline.

---

# 8. R2.2 — Architecture & Structural Audit

This is the primary discovery phase.

## 8.2.1 Module Boundary Audit

Review all relevant modules, including:

- Identity
- Catalog
- Cart
- Orders
- Inventory
- Payments

Verify that modules do not improperly access:

- Another module's DbContext
- Another module's Domain entities
- Another module's Application internals
- Another module's Infrastructure
- Another module's repositories
- Another module's persistence tables

Review all explicitly permitted cross-module dependencies.

---

## 8.2.2 Layer Boundary Audit

Review:

```text
Domain
Application
Infrastructure
Contracts
```

For every suspicious dependency, determine whether:

- The dependency direction is correct.
- Business logic is in the correct layer.
- Infrastructure is not leaking upward.
- Contracts remain implementation-independent.
- Application abstractions do not depend on infrastructure implementations.

---

## 8.2.3 File Placement Audit

Perform a source-tree-level audit of concrete files and types.

Look specifically for:

### Concrete types inside `Interfaces`

Example:

```text
Application
└── Interfaces
    └── CartItemState.cs
```

If `CartItemState` is a concrete application state/model rather than an abstraction, determine whether it belongs under an appropriate location such as:

```text
Application/Models
Application/State
Application/DTOs
```

The exact destination must be determined from its actual responsibility.

### Enums declared inside interface files

For example:

```text
ICartStore.cs
├── ICartStore
└── CartStatus
```

Determine whether the enum belongs to:

```text
Domain/Enums
Application/Enums
Infrastructure/Enums
```

based on ownership.

### Other misplaced types

Search for:

- DTOs in inappropriate namespaces
- Domain concepts in Application
- Application models in Infrastructure
- Integration contracts outside Contracts
- Infrastructure implementations outside Infrastructure
- Shared business concepts incorrectly placed in BuildingBlocks
- Files whose namespace does not match their architectural responsibility

---

## 8.2.4 Namespace Audit

Compare:

```text
Physical path
      ↕
Namespace
      ↕
Architectural responsibility
```

Look for:

- Namespace/path mismatches
- Overly broad namespaces
- Concrete types under `Interfaces`
- Inconsistent naming
- Incorrect module ownership

---

## 8.2.5 BuildingBlocks Audit

Review every BuildingBlock.

For each abstraction determine:

- Is it technical?
- Is it cross-cutting?
- Is it actually consumed by multiple modules?
- Is it stable enough to share?
- Does sharing introduce unwanted coupling?
- Is the concept business-specific?

Do not turn BuildingBlocks into a dumping ground.

---

## 8.2.6 Messaging Boundary Audit

Verify:

```text
Contracts
    ↓
Integration Events

Infrastructure
    ↓
RabbitMQ

Outbox
    ↓
Persistence
```

Ensure RabbitMQ client dependencies and infrastructure-specific messaging details do not leak into Domain, Application, or Contracts.

---

## 8.2.7 Persistence Boundary Audit

Review:

- Catalog DbContext
- Orders DbContext
- Inventory DbContext
- Payments DbContext
- Identity DbContext

Verify:

- No cross-module DbContext access
- No cross-module EF relationships
- No cross-module business table sharing
- Correct ownership of persistence models

---

## 8.2.8 Saga Boundary Audit

Review:

- `OrderSaga`
- `OrderSagaOrchestrator`
- Saga states
- State transitions
- Compensation transitions
- Payment transitions
- Inventory transitions
- Reservation expiry transitions

Verify:

```text
Saga coordinates workflows.
Modules own business state.
```

---

## Output

```text
docs/refactoring/REFACTORING_2_FINDINGS.md
```

Each finding must contain:

```text
ID
Category
Location
Current Responsibility
Expected Responsibility
Severity
Risk
Recommendation
Refactoring Type
Expected Benefit
```

Suggested categories:

- Structural
- Architecture
- Dependency
- Domain
- Application
- Infrastructure
- Persistence
- Messaging
- Saga
- API
- Contracts
- Testing
- Security
- Performance
- Documentation

Severity:

- Critical
- High
- Medium
- Low
- Informational

---

# 9. R2.3 — Structural & BuildingBlocks Refactoring

## 9.3.1 File and Folder Reorganization

Implement approved findings involving:

- Misplaced files
- Incorrect namespaces
- Incorrect folders
- Concrete types under `Interfaces`
- Enums inside unrelated interface files
- DTOs/state models in incorrect locations
- Contracts in incorrect layers

For each move:

```text
Move file
    ↓
Update namespace
    ↓
Update references
    ↓
Update tests
    ↓
Remove old location
    ↓
Build
    ↓
Run relevant tests
```

---

## 9.3.2 BuildingBlocks Cleanup

Remove or relocate:

- Unused abstractions
- Dead dependencies
- Incorrect shared concepts
- Business-specific abstractions incorrectly placed in BuildingBlocks

Only technical and genuinely cross-cutting concepts should remain shared.

---

## 9.3.3 Outbox Duplication Decision

Review:

- `OrdersOutboxStore`
- `InventoryOutboxStore`
- `PaymentsOutboxStore`
- Related configurations and factories

Compare:

- Behavior
- Dependencies
- Transaction semantics
- Error handling
- Concurrency behavior
- Evolution
- Ownership

Decision:

```text
KEEP DUPLICATION
```

or:

```text
CONSOLIDATE
```

Consolidation is allowed only if the shared abstraction does not introduce inappropriate coupling.

---

## 9.3.4 Idempotency Duplication Decision

Perform the same analysis for:

- `ProcessedMessageConfiguration`
- Idempotency stores
- Related infrastructure

Do not consolidate solely because implementations are structurally similar.

---

# 10. R2.4 — Domain & Application Responsibility Refactoring

## 10.4.1 Domain Audit

Review:

- Entities
- Aggregates
- Value Objects
- Domain Events
- Domain Errors
- Enums
- Business Rules
- State transitions

Ensure business invariants remain owned by the Domain.

---

## 10.4.2 Application Audit

Review:

- Commands
- Queries
- Handlers
- DTOs
- Interfaces
- Models
- Validation
- Mapping

Application should primarily:

```text
Receive
Validate
Coordinate
Invoke Domain
Use Ports
Return Result
```

Avoid moving business rules into Application merely to simplify a class.

---

## 10.4.3 Cart Responsibility Review

Specifically review:

- `CartItemState`
- Cart interfaces
- Cart enums
- Cart DTOs
- Catalog integration abstractions

Determine the correct ownership and location of each type.

---

## 10.4.4 OrderSaga Refactoring

Analyze `OrderSaga` using:

- Cohesion
- Responsibility
- Complexity
- Testability
- Transition discoverability
- Change boundaries

Possible result:

```text
Keep as one cohesive class
```

or, if justified:

```text
OrderSaga
├── State
├── Transitions
├── Payment handling
├── Inventory handling
├── Compensation
└── Expiry
```

Do not introduce unnecessary State/Strategy abstractions.

---

## 10.4.5 Payment Responsibility Review

Review:

- Payment aggregate
- Webhook event processor
- Stripe gateway
- Refund behavior
- Payment state transitions

Separate where appropriate:

```text
Webhook verification
Event interpretation
Payment state transition
External gateway interaction
```

---

## 10.4.6 Expiry Responsibility Review

Review `ExpiredReservationProcessor`.

Verify:

- Batch processing
- No N+1 behavior
- Correct Inventory ownership
- Correct event publication
- No inappropriate Order mutation
- Correct idempotency behavior

---

# 11. R2.5 — Infrastructure & Persistence Refactoring

## 11.5.1 EF Core Configuration Review

Review:

- Entity configurations
- Outbox configuration
- Processed message configuration
- Indexes
- Constraints
- Conversions
- Precision
- Concurrency configuration

---

## 11.5.2 Transaction Boundary Review

Verify:

```text
Business State
      +
Outbox
      +
Required Idempotency State
      ↓
Atomic Module Transaction
```

Do not introduce distributed transactions.

Do not change transaction semantics without explicit justification and regression testing.

---

## 11.5.3 F-14 Interceptor Registration Review

Re-evaluate possible duplicate registration involving:

```text
BaseDbContext.OnConfiguring
        +
DI ISaveChangesInterceptor
```

Determine whether duplicate registration exists or creates a realistic behavioral risk.

If required:

```text
Fix
+
Regression Test
```

---

## 11.5.4 Query and Persistence Review

Inspect for:

- N+1 queries
- Unnecessary tracking
- Premature materialization
- Excessive round trips
- Incorrect projections
- Batch processing issues
- Redis inefficiencies
- Serialization overhead

Only fix evidence-based problems.

Do not perform speculative optimization.

---

# 12. R2.6 — API & Contracts Hardening

## 12.6.1 API Review

Review:

- Controllers
- Request models
- Response models
- DTOs
- ProblemDetails
- Swagger/OpenAPI
- Authorization
- Validation

Controllers should remain thin.

---

## 12.6.2 F-19

Re-evaluate and, if still valid:

```text
Swagger ProducesResponseType
```

Apply the smallest appropriate fix.

---

## 12.6.3 F-20

Re-evaluate and, if still valid:

```text
Cart Create endpoint
empty request body
```

Improve contract clarity without introducing unrelated behavior changes.

---

## 12.6.4 F-18

Current recommendation:

```text
DEFER
```

`CheckoutResponse → Domain Order` may require a broader contract/handler change and is not necessary for the primary R2 objectives unless the architecture audit proves otherwise.

---

## 12.6.5 Contract Boundary Review

Ensure:

```text
Integration Events
        ↓
Contracts

Internal Domain/Application Types
        ↓
Never exposed as integration contracts
```

Specifically verify that:

```text
InventoryReservationExpiredIntegrationEvent
```

belongs to `Inventory.Contracts`, and Payments integration events remain in `Payments.Contracts`.

---

# 13. R2.7 — Test & Architecture Hardening

The objective is not merely higher coverage.

The objective is:

> Protect architectural and behavioral invariants after refactoring.

## 13.7.1 Architecture Tests

Add or strengthen rules for:

- Module isolation
- Layer isolation
- Contract purity
- BuildingBlocks boundaries
- Messaging restrictions
- Payments isolation
- Appropriate namespace/type placement where enforceable

---

## 13.7.2 Saga Tests

Protect:

```text
Current State + Event
        ↓
Expected deterministic transition
```

Include compensation and expiry transitions.

---

## 13.7.3 Idempotency Tests

Protect:

```text
Duplicate Message
        ↓
No duplicate business effect
```

---

## 13.7.4 Outbox Tests

Protect:

```text
Business State
      +
Outbox
      =
Atomic persistence
```

---

## 13.7.5 Persistence Tests

Protect:

- Concurrency behavior
- Transaction behavior
- Batch processing
- Persistence invariants
- Inventory reservation semantics

---

# 14. R2.8 — Final Hardening & Completion

## 14.8.1 Dead Code Cleanup

Remove only confirmed:

- Unused files
- Unused methods
- Unused references
- Unused packages
- Obsolete code

No deletion based solely on assumptions.

---

## 14.8.2 Naming Consistency

Review:

- Namespaces
- Folders
- Classes
- Interfaces
- DTOs
- Commands
- Queries
- Events
- Enums

---

## 14.8.3 Security Review

Review:

- Secrets
- Stripe configuration
- RabbitMQ credentials
- Logging
- Sensitive data exposure
- Authorization
- Webhook endpoint configuration

---

## 14.8.4 Performance Review

Only address evidence-based concerns involving:

- Database round trips
- N+1 queries
- Redis operations
- EF tracking
- Materialization
- Batch processing
- Significant allocations

---

## 14.8.5 Documentation Synchronization

Update when implementation facts actually changed:

```text
docs/ARCHITECTURE.md
docs/PROJECT_CONTEXT.md
docs/adr/*
```

Do not rewrite architectural documentation merely to reflect cosmetic changes.

---

## 14.8.6 Final Verification

Run:

```bash
dotnet restore
dotnet build FluxCommerce.slnx -c Debug
dotnet test
```

Verify separately:

- Architecture tests
- Unit tests
- Integration tests
- Infrastructure-dependent tests
- EF model/migration consistency

Compare final results against R2.1 baseline.

---

## 14.8.7 Completion Report

Create:

```text
docs/refactoring/REFACTORING_2_COMPLETION.md
```

Include:

1. Scope
2. Baseline
3. Findings
4. Completed Refactorings
5. Moved Files
6. Removed Files
7. BuildingBlocks Decisions
8. Saga Changes
9. Persistence Changes
10. API/Contract Changes
11. Test Changes
12. Architecture Verification
13. Build Verification
14. Deferred Findings
15. Remaining Technical Debt
16. Phase 12 Readiness

---

# 15. Refactoring Decision Framework

Every finding must receive one of these decisions:

| Decision | Meaning |
|---|---|
| `KEEP` | Current implementation is acceptable |
| `MOVE` | Type/file is in the wrong architectural location |
| `REFACTOR` | Responsibility is correct but implementation needs improvement |
| `CONSOLIDATE` | Shared abstraction is justified |
| `REMOVE` | Dead code is confirmed |
| `STRENGTHEN` | Add tests/rules to prevent regression |
| `DEFER` | Change is outside the appropriate R2 scope or risk is unjustified |

---

# 16. Safety Rules

Every refactoring follows:

```text
Inspect
   ↓
Classify Finding
   ↓
Determine Ownership
   ↓
Choose Minimal Change
   ↓
Build
   ↓
Run Relevant Tests
   ↓
Run Architecture Tests
   ↓
Verify Behavior
```

Mandatory rules:

1. No new business features.
2. No intentional business behavior changes.
3. No unnecessary public contract changes.
4. No generic abstraction solely to remove duplication.
5. No BuildingBlocks promotion without architectural justification.
6. No Saga decomposition solely because of line count.
7. No deletion without evidence.
8. No concurrency semantic changes.
9. No unsafe transaction-boundary changes.
10. No Phase 12 implementation inside R2.

---

# 17. Expected Deliverables

```text
docs/refactoring/
│
├── REFACTORING_2_PLAN.md
├── REFACTORING_2_BASELINE.md
├── REFACTORING_2_FINDINGS.md
└── REFACTORING_2_COMPLETION.md
```

Potentially updated:

```text
docs/ARCHITECTURE.md
docs/PROJECT_CONTEXT.md
docs/adr/*
```

Only when architectural facts change.

---

# 18. Definition of Done

R2 is complete only when:

- [ ] Build succeeds without errors.
- [ ] Architecture tests pass.
- [ ] Unit tests pass.
- [ ] Integration tests pass.
- [ ] No unauthorized module-boundary violations remain.
- [ ] No unauthorized layer-boundary violations remain.
- [ ] File placement follows actual responsibility.
- [ ] Concrete application models are not incorrectly grouped under `Interfaces`.
- [ ] Enums are located with their owning concept.
- [ ] Contracts remain implementation-independent.
- [ ] BuildingBlocks contain only justified shared technical concepts.
- [ ] Outbox duplication has an explicit documented decision.
- [ ] Idempotency duplication has an explicit documented decision.
- [ ] Saga structure is maintainable without behavior changes.
- [ ] Payments isolation is protected.
- [ ] Outbox atomicity is preserved.
- [ ] Idempotency behavior is preserved.
- [ ] Inventory concurrency behavior is preserved.
- [ ] Dead code has been removed only when confirmed.
- [ ] F-18 is explicitly documented as deferred if still deferred.
- [ ] F-19 and F-20 are resolved or explicitly justified if deferred.
- [ ] Documentation is synchronized with actual architecture.
- [ ] `REFACTORING_2_COMPLETION.md` is complete.
- [ ] The project is ready for Phase 12 — Failure Testing.

---

# 19. Final R2 Workflow

```text
R2.1
Baseline
   ↓
R2.2
Architecture & Structural Audit
   ↓
R2.3
Structural & BuildingBlocks Refactoring
   ↓
R2.4
Domain & Application Responsibility
   ↓
R2.5
Infrastructure & Persistence
   ↓
R2.6
API & Contracts Hardening
   ↓
R2.7
Test & Architecture Hardening
   ↓
R2.8
Final Hardening & Completion
   ↓
══════════════════════════════
Phase 12 — Failure Testing
══════════════════════════════
```

## Final Principle

R2 must leave FluxCommerce **structurally cleaner, easier to reason about, and safer to evolve**, while keeping the existing business behavior and reliability architecture intact.

The goal is not to redesign FluxCommerce.

The goal is to make the architecture that has already been built **clearer, more consistent, more maintainable, and better protected by automated verification**.
