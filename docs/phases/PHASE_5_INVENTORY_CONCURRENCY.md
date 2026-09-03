# Phase 5 — Inventory + Concurrency

## 1. Phase Overview

**Phase:** 5  
**Name:** Inventory + Concurrency  
**Primary Goal:** Build the Inventory bounded context and establish correctness guarantees for stock reservation, concurrency control, and overselling prevention.

Phase 5 follows the completion of **Phase 4 — Orders & Checkout**.

The phase focuses on Inventory business correctness before introducing the distributed messaging and Saga infrastructure planned for later phases.

### Core Responsibilities

- Inventory stock management.
- Inventory reservation lifecycle.
- Available / reserved quantity management.
- Reservation release.
- Reservation commit.
- Reservation expiration.
- Concurrency control.
- Overselling prevention.
- Inventory contracts and integration surface.
- Inventory persistence and database schema.
- Hardening and concurrency verification.

### Explicit Scope Boundary

Phase 5 must **not** introduce the distributed workflow infrastructure that belongs to later phases.

The following are outside this phase:

- RabbitMQ messaging infrastructure.
- Transactional Outbox implementation.
- Idempotent message consumers.
- Order/Inventory Saga orchestration.
- Payment processing.
- Stripe integration.
- Distributed transactions across module databases.

The objective is to make Inventory itself correct and reliable before connecting it to the later event-driven workflow.

---

# 2. Architectural Position

Inventory is a dedicated bounded context/module in the modular monolith.

```text
FluxCommerce
│
├── Catalog
├── Cart
├── Orders
├── Inventory
└── Payments
```

Inventory owns its own business state:

```text
Inventory
├── Stock State
└── Reservation State
```

Other modules must not directly access or modify Inventory persistence internals.

### Forbidden

```text
Orders
    ↓
InventoryDbContext
    ↓
Update Inventory
```

### Required Boundary

```text
Orders / Future Saga
        ↓
Inventory Contract
        ↓
Inventory Application
        ↓
Inventory Domain
        ↓
Inventory Persistence
```

Inventory must remain independent from the internal implementation of Orders, Payments, and other modules.

---

# 3. Phase Structure

Phase 5 is divided into the following implementation sub-phases:

```text
5.1  Inventory Module Foundation & Architecture
5.2  Inventory Domain Model & Invariants
5.3  Inventory Persistence & Database Schema
5.4  Inventory Stock Management
5.5  Reservation Lifecycle & Application Use Cases
5.6  Concurrency Control & Overselling Prevention
5.7  Reservation Expiration & Recovery Semantics
5.8  Inventory Contracts, API & Integration Surface
5.9  Hardening, Verification & Phase Completion
```

Implementation must follow this order unless a documented dependency requires otherwise.

---

# 4. 5.1 — Inventory Module Foundation & Architecture

## Objective

Establish and verify the Inventory module structure before implementing business behavior.

## Scope

Review and implement:

- `Domain`
- `Application`
- `Infrastructure`
- `Contracts`
- `InventoryDbContext`
- Module registration.
- Dependency injection.
- Database configuration.
- Runtime configuration.
- Architecture boundaries.

Verify consistency with the project's established module template.

## Architectural Rules

Inventory must:

- Own its own persistence.
- Own stock state.
- Own reservation state.
- Depend only on approved shared abstractions.
- Avoid direct dependencies on other modules' Infrastructure layers.
- Avoid direct access to other module DbContexts.
- Avoid future messaging/Saga dependencies at this stage.

## Exit Criteria

- Inventory module has a clean architectural boundary.
- DI registration is correct.
- DbContext registration is correct.
- Architecture tests can enforce the boundary.
- No premature dependency on later-phase infrastructure exists.

---

# 5. 5.2 — Inventory Domain Model & Invariants

## Objective

Define the Inventory domain model and establish the business invariants that all subsequent application and persistence behavior must protect.

## Core Domain Models

### InventoryItem

Conceptually:

```text
InventoryItem
├── SKU
├── OnHandQuantity
├── ReservedQuantity
└── AvailableQuantity
```

### InventoryReservation

Conceptually:

```text
InventoryReservation
├── Id
├── OrderId
├── SKU
├── Quantity
├── Status
└── ExpiresAt
```

The final representation must follow the existing project's domain modeling conventions.

## Domain Decisions

Define and implement:

- SKU identity.
- Quantity representation.
- Reservation identity.
- Reservation ownership through `OrderId`.
- Reservation status model.
- Reservation expiration semantics.
- Domain errors.
- Valid state transitions.
- Aggregate boundaries.
- Domain methods responsible for protecting invariants.

## Core Invariants

The domain must guarantee rules such as:

```text
OnHandQuantity >= 0
ReservedQuantity >= 0
AvailableQuantity >= 0
ReservedQuantity <= OnHandQuantity
```

A successful reservation must correspond to stock that was actually made unavailable for competing reservations.

## Reservation State Rules

The lifecycle must define valid and invalid transitions.

Conceptually:

```text
Pending
   │
   ├── Commit  → Committed
   │
   ├── Release → Released
   │
   └── Expire  → terminal expiration/release state
```

Terminal states must not be mutated through invalid repeated lifecycle operations.

## Tests

Implement domain tests for:

- Valid inventory creation.
- Invalid quantities.
- Reservation creation.
- Invalid reservation quantities.
- Valid state transitions.
- Invalid state transitions.
- Commit.
- Release.
- Expiration.
- Repeated lifecycle operations.

## Exit Criteria

The Inventory domain can enforce its own business rules independently of EF Core, SQL Server, API, or messaging.

---

# 6. 5.3 — Inventory Persistence & Database Schema

## Objective

Create the SQL Server persistence model that represents the Inventory domain without violating module boundaries.

## Persistence Model

Conceptually:

```text
InventoryDbContext
│
├── InventoryItems
└── InventoryReservations
```

## Scope

Implement:

- EF Core entity configurations.
- Table mappings.
- Required fields.
- Quantity constraints where appropriate.
- SKU uniqueness.
- Reservation indexes.
- SKU lookup indexes.
- OrderId lookup indexes.
- Expiration lookup indexes.
- Appropriate relationships inside Inventory.
- Concurrency support required by the selected strategy.
- Migration.
- Database update.
- Database verification.

## Boundary Rules

Do not create cross-module foreign keys such as:

```text
Inventory → Orders database table
```

`OrderId` is an external business reference owned by Inventory's reservation state; Orders remain responsible for Order state.

## Exit Criteria

- Migration is valid.
- Database schema matches the domain model.
- Required indexes exist.
- Inventory has no cross-module database writes or foreign keys.
- Persistence can support the reservation operations efficiently.

---

# 7. 5.4 — Inventory Stock Management

## Objective

Provide the controlled application-level operations required to initialize and manage inventory state.

## Scope

Determine and implement only the stock-management capabilities actually required by the project.

Potential operations include:

```text
Create / Initialize Inventory
Get Inventory
Adjust Stock
```

Do not create generic CRUD merely because the entity exists.

## Business Rules

Stock operations must:

- Validate quantities.
- Protect invariants.
- Prevent invalid negative states.
- Handle duplicate SKU correctly.
- Return domain/application errors consistently.
- Respect authorization when exposed through an API.

## Tests

Cover:

- Inventory creation.
- Duplicate SKU.
- Missing inventory.
- Valid stock adjustment.
- Invalid stock adjustment.
- Quantity boundary cases.
- Available quantity correctness.

## Exit Criteria

Inventory stock state can be created, inspected, and safely adjusted through controlled application use cases.

---

# 8. 5.5 — Reservation Lifecycle & Application Use Cases

## Objective

Implement the complete reservation lifecycle inside the Inventory bounded context.

## Required Operations

```text
Reserve
Release
Commit
Expire
```

## Reserve

Input conceptually includes:

```text
OrderId
SKU
Quantity
Expiration
```

Expected behavior:

```text
Sufficient Stock
    ↓
Reservation Created
    ↓
Reserved Quantity Updated
```

Insufficient stock:

```text
Insufficient Stock
    ↓
Reservation Failed
    ↓
Inventory State Unchanged
```

## Release

```text
Reservation
    ↓
Release
    ↓
Reserved Quantity Reduced
    ↓
Reservation Terminal State
```

Release must not return more quantity than was actually reserved.

## Commit

```text
Reservation
    ↓
Commit
    ↓
Reserved Stock Becomes Consumed Stock
    ↓
Reservation Committed
```

Conceptually:

```text
Before:
OnHand = 10
Reserved = 3
Available = 7

After Commit:
OnHand = 7
Reserved = 0
Available = 7
```

## Expire

Expired reservations must safely release their reserved quantity according to the selected expiration semantics.

## Application Layer

Implement the required commands/handlers and result/error mapping using the project's existing application abstractions.

Do not introduce MediatR or FluentValidation.

## Tests

Cover:

- Successful reservation.
- Insufficient stock.
- Release.
- Commit.
- Expiration.
- Invalid reservation state.
- Repeated operations.
- Missing reservation.
- Multiple reservations for the same SKU.

## Exit Criteria

The full Reservation lifecycle works correctly within the Inventory module without relying on RabbitMQ, Outbox, or Saga infrastructure.

---

# 9. 5.6 — Concurrency Control & Overselling Prevention

## Objective

Guarantee that concurrent reservation attempts cannot oversell inventory.

This is the critical technical sub-phase of Phase 5.

## Primary Invariant

```text
AvailableQuantity >= 0
```

must remain true under concurrent operations.

## Concurrency Strategy

Evaluate the persistence model and implement the appropriate mechanism.

The preferred strategy should use an atomic conditional database operation where appropriate.

Conceptually:

```sql
UPDATE InventoryItems
SET AvailableQuantity = AvailableQuantity - @quantity
WHERE SKU = @sku
  AND AvailableQuantity >= @quantity
```

Then:

```text
RowsAffected = 1
    → Reservation may succeed

RowsAffected = 0
    → Reservation must fail
```

The implementation must ensure that stock validation and the corresponding mutation cannot be separated into an unsafe:

```text
SELECT
    ↓
Check
    ↓
UPDATE
```

sequence.

## Concurrency Techniques To Evaluate

Depending on the final persistence design:

- Atomic conditional updates.
- Optimistic concurrency.
- Concurrency tokens.
- Transactional boundaries.
- Appropriate SQL Server isolation behavior.
- Database constraints.

The final strategy must be explicitly documented.

## Mandatory Concurrency Tests

### Basic Race

```text
Available = 1

Request A → Reserve 1
Request B → Reserve 1
```

Expected:

```text
A → Success
B → Failure
```

### Parallel Reservations

Test:

- Multiple concurrent requests for one SKU.
- Stock lower than total requested quantity.
- Stock equal to total requested quantity.
- Stock greater than total requested quantity.
- Many concurrent requests.
- Different SKUs concurrently.

### Lifecycle Concurrency

Test appropriate concurrent combinations of:

- Reserve.
- Release.
- Commit.
- Expiration.

Verify:

```text
No overselling
No negative available quantity
No double release
No double commit
No lost reservation
No inconsistent reservation state
```

## Exit Criteria

The implementation has automated evidence that concurrent requests cannot cause overselling.

---

# 10. 5.7 — Reservation Expiration & Recovery Semantics

## Objective

Implement safe expiration processing for reservations that remain pending beyond their allowed lifetime.

## Scope

Implement:

- Expiration policy.
- Clock abstraction consistent with project conventions.
- Detection of expired reservations.
- Safe release of expired reservations.
- Protection against repeated expiration processing.
- Correct handling of already committed reservations.
- Correct handling of already released reservations.
- Background processing mechanism required for expiration.

Conceptually:

```text
Background Processing
        ↓
Expired Reservations
        ↓
Release
        ↓
Inventory State Updated
```

## Important Boundary

Publishing:

```text
ReservationExpired
```

through RabbitMQ and coordinating Order/Saga behavior belongs to later messaging/orchestration phases.

Phase 5 should establish the local Inventory expiration semantics only.

## Tests

Cover:

- Reservation expires.
- Reservation does not expire before its deadline.
- Expired reservation releases quantity.
- Already committed reservation is not released.
- Already released reservation is not released twice.
- Repeated expiration processing is safe.
- Multiple expired reservations are processed correctly.

## Exit Criteria

Expired reservations can be detected and safely released without corrupting Inventory state.

---

# 11. 5.8 — Inventory Contracts, API & Integration Surface

## Objective

Expose only the Inventory capabilities that should be externally consumable while preserving the module boundary.

## Contracts

Define the contracts required for Inventory operations.

Conceptually:

```text
ReserveInventory
ReleaseInventory
CommitInventory
```

The exact contract shape must follow the project's existing Contracts conventions.

Contracts must not expose:

- EF entities.
- DbContext.
- Internal repositories.
- Infrastructure implementations.
- Internal domain implementation details.

## API

If Inventory management requires REST endpoints, implement the appropriate administrative/read operations.

Potential surface:

```text
GET    /inventory/{sku}
POST   /inventory
PATCH  /inventory/{sku}
GET    /inventory/reservations/{id}
```

The exact endpoints must be determined from the established project API conventions and actual requirements; do not create endpoints without a business need.

## API Concerns

Apply existing project conventions for:

- Authorization.
- Validation.
- ProblemDetails.
- Localization.
- Swagger/OpenAPI.
- Error mapping.
- HTTP status codes.
- Resource ownership/security.

## Integration Boundary

Prepare the Inventory contract surface for future Order/Saga integration without implementing RabbitMQ or Saga orchestration in this phase.

## Exit Criteria

Inventory capabilities are exposed through clean contracts/API boundaries without leaking internal implementation details.

---

# 12. 5.9 — Hardening, Verification & Phase Completion

## Objective

Perform the final production-readiness review and close Phase 5.

## Validation Review

Verify:

- Request validation.
- Domain invariants.
- Quantity boundaries.
- SKU validation.
- Reservation state rules.
- Error mapping.

## Authorization Review

Verify:

- Administrative inventory operations.
- Role/policy requirements.
- No unauthorized stock modification.
- No IDOR-style access to reservation resources where applicable.

## Localization Review

Verify:

- Inventory error codes.
- Localized ProblemDetails.
- Resource completeness across supported cultures.
- No hard-coded user-facing error messages.

## Performance Review

Inspect:

- SKU lookup.
- Reservation lookup.
- Expiration query.
- Index usage.
- Query count.
- N+1 issues.
- Transaction scope.
- DbContext lifetime.
- Atomic reservation operation performance.

## Testing

The phase must include appropriate:

- Domain unit tests.
- Application tests.
- Persistence integration tests.
- Concurrency integration tests.
- API integration tests.
- Architecture tests.

The concurrency test suite is mandatory because normal sequential tests cannot prove overselling prevention.

## Documentation Review

Update where necessary:

- `ARCHITECTURE.md`
- `PROJECT_CONTEXT.md`
- Inventory-specific documentation.
- Concurrency decision documentation.
- Reservation lifecycle documentation.
- Swagger/OpenAPI documentation.

Verify:

- EF migrations.
- Database schema.
- Runtime configuration.
- DI registration.
- Health checks if applicable.
- Logging/observability requirements.

## Completion Report

Create:

```text
docs/completed/PHASE_5_COMPLETION_REPORT.md
```

The completion report should document:

- Implemented capabilities.
- Architectural decisions.
- Concurrency strategy.
- Reservation lifecycle.
- Database changes.
- Tests and results.
- Known limitations.
- Deferred work.
- Confirmation that Phase 5 is complete.

---

# 13. Phase 5 Dependency Graph

```text
5.1 Inventory Foundation
        │
        ▼
5.2 Domain Model & Invariants
        │
        ▼
5.3 Persistence & Database
        │
        ▼
5.4 Stock Management
        │
        ▼
5.5 Reservation Lifecycle
        │
        ▼
5.6 Concurrency & Overselling Prevention
        │
        ▼
5.7 Expiration & Recovery
        │
        ▼
5.8 Contracts & API
        │
        ▼
5.9 Hardening & Completion
```

---

# 14. Definition of Done

Phase 5 is complete only when all of the following are true:

- [ ] Inventory module follows the established modular architecture.
- [ ] Inventory owns its stock state.
- [ ] Inventory owns its reservation state.
- [ ] No cross-module direct database access exists.
- [ ] Inventory domain invariants are implemented.
- [ ] Inventory persistence is implemented.
- [ ] Database migration is created and verified.
- [ ] Stock management is implemented where required.
- [ ] Reservation lifecycle is implemented.
- [ ] Reserve is concurrency-safe.
- [ ] Overselling is prevented.
- [ ] Available quantity cannot become negative through valid operations.
- [ ] Release is safe.
- [ ] Commit is safe.
- [ ] Expiration is safe.
- [ ] Repeated invalid lifecycle operations cannot corrupt state.
- [ ] Reservation expiration processing is implemented.
- [ ] Required contracts are defined.
- [ ] Required API surface is implemented.
- [ ] Authorization is reviewed.
- [ ] Localization is reviewed.
- [ ] Swagger/OpenAPI is reviewed.
- [ ] Unit tests pass.
- [ ] Integration tests pass.
- [ ] Concurrency tests pass.
- [ ] Architecture tests pass.
- [ ] Performance-critical queries are reviewed.
- [ ] `ARCHITECTURE.md` is synchronized.
- [ ] `PROJECT_CONTEXT.md` is synchronized if required.
- [ ] `PHASE_5_COMPLETION_REPORT.md` is created.
- [ ] No Phase 6+ infrastructure has been prematurely introduced.

---

# 15. Explicitly Deferred to Later Phases

The following capabilities must remain deferred:

```text
Transactional Outbox
        ↓
RabbitMQ
        ↓
Idempotent Consumers
        ↓
Order / Inventory Saga
        ↓
Payment Integration
        ↓
Distributed Compensation
```

Phase 5 establishes the Inventory capability that those later phases will consume.

The guiding principle is:

> **Make Inventory correct first. Integrate Inventory into the distributed order workflow later.**
