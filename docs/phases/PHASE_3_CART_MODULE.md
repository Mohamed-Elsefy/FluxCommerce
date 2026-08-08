# PHASE 3 — CART MODULE

## 1. Phase Overview

Phase 3 introduces the Cart Module into FluxCommerce.

The Cart Module represents temporary shopping state between product selection and
the future checkout/order workflow.

The module must support:

- Guest Carts
- Authenticated User Carts
- Adding Items
- Updating Item Quantities
- Removing Items
- Clearing Carts
- Retrieving Carts
- Cart Merge after Authentication
- Cart Expiration
- Temporary Price Snapshots

The Cart Module must remain independently owned and must not become responsible for:

- Orders
- Checkout
- Inventory
- Payments
- Final Pricing
- Promotions
- Coupons
- Shipping
- Messaging
- Saga orchestration

The implementation must preserve the existing FluxCommerce architecture:

- Modular Monolith
- Clean Architecture
- DDD-oriented domain modeling
- Module-per-DbContext architecture where applicable
- Explicit module contracts
- No cross-module direct persistence access
- Result-based expected business failures
- Centralized localization
- Architecture tests
- Progressive testing


---

# 2. Position in the Project Roadmap

The implementation order is:

Phase 0
Architecture Foundation
        ↓
Phase 1
Identity & Access
        ↓
Phase 1.5
Runtime Preparation
        ↓
Phase 1.6
Internationalization
        ↓
Phase 2
Catalog
        ↓
Phase 3
Cart
        ↓
Phase 4
Orders
        ↓
Phase 5
Inventory + Concurrency
        ↓
Phase 6
Outbox + RabbitMQ
        ↓
Phase 7
Idempotent Consumers
        ↓
Phase 8
Order/Inventory Saga
        ↓
Phase 9
Stripe Payments
        ↓
Phase 10+
Reliability / Supporting Capabilities


Phase 3 must not pull responsibilities forward from later phases.


---

# 3. Business Responsibility

The Cart Module owns temporary shopping state.

Conceptually:

Catalog
    ↓
Cart
    ↓
Orders / Checkout
    ↓
Inventory
    ↓
Payments

Catalog owns product state.

Cart owns shopping state.

Orders own order state.

Inventory owns stock and reservation state.

Payments own payment state.

No module may directly modify another module's business state.


---

# 4. Architectural Principles

## 4.1 Module Ownership

Cart owns:

- Cart
- Cart Items
- Cart lifecycle
- Cart expiration
- Guest cart state
- Authenticated cart state
- Cart merge behavior

Other modules must not directly modify Cart state.


## 4.2 Persistence Ownership

Redis is the primary persistence mechanism for Cart state.

The existing Cart relational DbContext foundation must not be used as the
primary Cart persistence mechanism during Phase 3.

No Cart SQL schema or EF Core migrations should be introduced merely to persist
Cart state.


## 4.3 No Cross-Module Database Access

Cart must never directly access:

- CatalogDbContext
- IdentityDbContext
- OrdersDbContext
- InventoryDbContext
- PaymentsDbContext

Cart must interact with other modules only through explicit contracts or
existing cross-cutting abstractions.


## 4.4 Identity Boundary

Cart may use:

- ICurrentUser
- UserId
- required identity abstractions

Cart must not depend on:

- IdentityDbContext
- Identity entities
- Identity infrastructure implementation details


## 4.5 Messaging Boundary

Phase 3 does not implement:

- RabbitMQ integration
- Outbox publishing
- Integration event consumers
- Saga orchestration
- Idempotent message consumers

These belong to later phases.


---

# 5. Cart Storage Model

Redis is the primary Cart state store.

Conceptual model:

Guest
  ↓
Guest Cart
  ↓
Redis

Authenticated User
  ↓
User Cart
  ↓
Redis


The Cart must be represented as a self-contained serialized state document.

The persistence implementation must provide:

- Get
- Create
- Update
- Delete
- Expiration
- Atomic/concurrency-safe mutation where required


---

# 6. Cart Aggregate

The domain model should center around:

Cart
└── CartItem


Cart is the Aggregate Root.

CartItem is owned by Cart and must not be independently persisted or mutated
outside the Cart aggregate.


## 6.1 Cart Responsibilities

Cart must protect invariants related to:

- Item uniqueness
- Quantity validity
- Add behavior
- Update behavior
- Remove behavior
- Clear behavior
- Merge behavior
- Cart state consistency


## 6.2 Cart Item Identity

A CartItem represents a purchasable Product Variant.

The primary business identity is:

VariantId

The item may also retain:

- ProductId
- SKU

as required reference/snapshot information.


## 6.3 Quantity Rules

Quantity must be:

- greater than zero
- an integer
- explicitly validated

AddItem:

    ExistingQuantity + RequestedQuantity

UpdateQuantity:

    CurrentQuantity = RequestedQuantity

RemoveItem:

    Remove the item entirely

ClearCart:

    Remove all items


## 6.4 Duplicate Items

A Cart cannot contain multiple CartItems representing the same VariantId.

Adding an existing VariantId must increase its quantity rather than create a
second item.


---

# 7. Cart State

The Cart state should contain only information required by the Cart business
workflow.

Conceptual state:

Cart
├── CartId
├── Owner
├── Items
├── Version
├── CreatedAt
└── UpdatedAt


CartItem
├── ProductId
├── VariantId
├── SKU
├── ProductNameSnapshot
├── UnitPriceSnapshot
├── Currency
└── Quantity


The exact serialized representation is an implementation detail and must not
leak into the Domain or Application layers.


---

# 8. Guest Cart

Guest users must be able to create and manipulate a Cart without an Identity
UserId.

Guest Cart identity must be based on an opaque Cart identifier.

Requirements:

- identifier must be non-sequential
- identifier must be difficult to guess
- identifier must not contain PII
- client must be able to retain the identifier
- server must validate ownership/access


Conceptually:

Guest Client
    ↓
Opaque CartId
    ↓
Redis
    ↓
Guest Cart


A guest must never be able to access another guest's Cart by guessing an
identifier.


---

# 9. Authenticated Cart

An authenticated user has one active Cart.

Conceptually:

Authenticated User
        ↓
UserId
        ↓
User Cart
        ↓
Redis


The Cart Module must use the existing current-user abstraction instead of
depending directly on Identity internals.


---

# 10. Cart Ownership

The system must support two ownership modes:

Guest
Authenticated User


The Cart must never ambiguously belong to multiple authenticated users.

For authenticated users:

    UserId → One Active Cart


Guest ownership is independent from Identity ownership.


---

# 11. Cart Merge

Cart Merge is a first-class Phase 3 capability.

Scenario:

Guest
    ↓
Adds items
    ↓
Guest Cart
    ↓
Login
    ↓
Authenticated User
    ↓
Merge
    ↓
Authenticated Cart


## 11.1 Merge Rules

If the authenticated user has no existing Cart:

Guest Cart becomes the user's Cart state.

If the authenticated user already has a Cart:

The two carts are merged.


## 11.2 Duplicate Variant

If both carts contain the same VariantId:

Guest Quantity + User Quantity


Example:

Guest:

Variant A × 2
Variant B × 1

User:

Variant A × 1
Variant C × 3

Result:

Variant A × 3
Variant B × 1
Variant C × 3


## 11.3 Guest Cart After Successful Merge

After successful merge:

Guest Cart is consumed/deleted.

The authenticated Cart becomes the authoritative Cart.


## 11.4 Empty Guest Cart

If the Guest Cart is empty:

- no item merge is required
- authenticated Cart remains unchanged
- Guest Cart may be removed


## 11.5 Merge Idempotency

Merge must be logically idempotent.

Repeating a successful merge must not duplicate quantities.

The implementation must therefore consume the Guest Cart after successful merge
and/or use an equivalent atomic state transition.


---

# 12. Cart Expiration

Cart expiration is implemented using Redis expiration semantics.

The exact TTL value must be configurable and must not be hard-coded in Domain
logic.


## 12.1 TTL Behavior

The recommended behavior is a sliding TTL for meaningful Cart mutations.

TTL may be refreshed after:

- Add Item
- Update Quantity
- Remove Item
- Clear
- Merge


A normal Get Cart operation must not continuously extend the lifetime of the
Cart.


## 12.2 Expired Cart

When Redis expiration occurs:

The Cart state is considered gone.

The system must not require a background cleanup worker for normal Cart
expiration.


---

# 13. Price Snapshot

Cart may store a temporary price snapshot for display and Cart continuity.

Example:

UnitPriceSnapshot
Currency


This snapshot is not authoritative.

Important rule:

Cart Price Snapshot
        ≠
Final Order Price


The future checkout flow must recalculate/validate commercial values before
creating the immutable Order snapshot.


---

# 14. Pricing Boundary

Phase 3 does not implement a Pricing Engine.

Cart must not own:

- tax calculation
- discount calculation
- coupon calculation
- shipping calculation
- final order total calculation
- pricing history


At this stage Cart may consume the current Catalog commercial information
required to construct its temporary snapshot.


Final calculation belongs to the future checkout/order workflow.


---

# 15. Catalog Integration

Cart requires explicit interaction with Catalog when it needs to validate or
obtain information about a Variant.

The interaction must use an explicit contract.

Allowed:

Cart Application
    ↓
Catalog Contract
    ↓
Catalog Application
    ↓
Catalog Result


Forbidden:

Cart
    ↓
CatalogDbContext


Cart must not depend on Catalog infrastructure implementation details.


## 15.1 Required Catalog Information

The Cart should request only the information required for its operation, such
as:

- ProductId
- VariantId
- SKU
- Product name/display information where required
- Current price
- Currency
- Sellable/active status


The exact contract must remain minimal and purpose-specific.


## 15.2 Catalog Availability

If the Variant is no longer available for adding to Cart:

The Add/Update operation must fail with a proper business error.

Existing Cart items do not require background synchronization during Phase 3.

Future Checkout will revalidate the Cart.


---

# 16. Cart Read Performance

Get Cart must not require one Catalog request per item.

The Cart must retain the minimum snapshot data required for normal Cart display.

Therefore:

GET Cart
    ↓
Redis
    ↓
Cart Snapshot


not:

GET Cart
    ↓
Redis
    ↓
N Catalog Requests


Final validation/recalculation is deferred to Checkout.


---

# 17. Concurrency

Cart mutations must not use an unsafe:

GET
↓
Modify in memory
↓
SET


pattern without concurrency protection.


The implementation must protect against lost updates.

Preferred approach:

- Redis atomic operations where practical
- optimistic Cart versioning where needed
- avoid distributed locks unless a concrete requirement demonstrates that they
  are necessary


Conceptually:

Version = 5

Request A → version 5 → success → version 6

Request B → version 5 → stale → conflict/retry


The simplest mechanism that guarantees correctness should be selected.


---

# 18. Redis Key Design

The implementation must use a predictable and isolated key namespace.

Conceptually:

cart:{cartId}

and an authenticated-user lookup:

cart:user:{userId}


These are conceptual conventions.

Before implementation, the agent must inspect existing Redis/Cache infrastructure
and reuse existing project conventions where they exist.

Cart must not introduce a second incompatible Redis configuration model.


---

# 19. Serialization

Cart state must be serialized into a stable representation suitable for Redis.

The serialization implementation belongs to Infrastructure.

Domain objects must not depend on Redis serialization libraries.


The implementation must consider:

- backward compatibility during future model evolution
- null/default handling
- enum representation
- DateTime representation
- monetary representation
- payload size


---

# 20. Money Representation

Cart must follow the existing project monetary model.

Where a Money abstraction already exists, it must be reused.

Do not introduce a second incompatible representation of:

- Amount
- Currency


The Cart must not implement independent monetary arithmetic that conflicts with
the future Pricing module.


---

# 21. Application Use Cases

The Application layer must expose the Cart use cases.

Required operations:

## Commands

- AddItem
- UpdateQuantity
- RemoveItem
- ClearCart
- MergeCart

## Query

- GetCart


Each use case must:

- validate input
- enforce ownership
- coordinate required contracts
- invoke Domain behavior
- persist through Cart persistence abstraction
- return the project's standard Result/response model


---

# 22. Cart Persistence Abstraction

Application/Domain layers must not depend directly on Redis.

Introduce an appropriate Cart persistence abstraction, for example:

ICartStore


Responsibilities may include:

- Get Cart
- Save Cart
- Delete Cart
- Exists
- Atomic/concurrency-aware update where required


The exact interface must follow the project's existing abstraction conventions.


---

# 23. API Layer

Controllers must remain thin.

The API layer is responsible for:

- HTTP contract
- request DTO binding
- invoking Application use cases
- HTTP response mapping
- authorization/access context
- OpenAPI documentation


Controllers must not contain:

- Redis logic
- Cart business rules
- merge algorithms
- Catalog persistence access
- pricing calculations


---

# 24. API Surface

The recommended REST surface is:

GET    /api/cart
POST   /api/cart/items
PUT    /api/cart/items/{variantId}
DELETE /api/cart/items/{variantId}
DELETE /api/cart
POST   /api/cart/merge


These routes are the proposed Phase 3 API design and must be verified against
existing project routing conventions before implementation.


---

# 25. Guest vs Authenticated API Behavior

Guest users:

- Get Cart
- Add Item
- Update Quantity
- Remove Item
- Clear Cart

Authenticated users:

- Get Cart
- Add Item
- Update Quantity
- Remove Item
- Clear Cart
- Merge Guest Cart


Merge requires an authenticated user context.


---

# 26. Authorization and Ownership

Cart access must always be scoped to the current owner.

The system must prevent:

- accessing another user's Cart
- modifying another user's Cart
- deleting another user's Cart
- merging another user's Guest Cart


Guest Cart identifiers must be treated as sensitive access credentials.


---

# 27. Validation

Phase 3 must use the project's existing validation architecture.

Validation must cover:

- missing VariantId
- invalid CartId
- invalid Quantity
- zero Quantity
- negative Quantity
- invalid ownership
- invalid merge request
- unavailable Variant
- invalid Cart state


Validation must not be duplicated inconsistently across:

- Controller
- Application
- Domain


Business invariants belong in Domain.

Request-shape validation belongs at the appropriate application/API boundary.


---

# 28. Error Handling

Expected business failures must use the project's Result/Error architecture.

Examples:

- CartNotFound
- CartAccessDenied
- CartItemNotFound
- InvalidQuantity
- VariantNotFound
- VariantUnavailable
- CartMergeFailed
- CartConcurrencyConflict


The final error codes must follow the established module error-code conventions.


Human-readable error text must be localized.

Stable error codes must remain machine-readable.


---

# 29. Localization

Cart must use the existing Phase 1.6 localization architecture.

Requirements:

- no ASP.NET localization dependency in Domain/Application
- stable error codes
- localized human-readable messages
- localized validation messages
- localized ProblemDetails where applicable
- localization resources for Cart errors


Cart must not create an alternative localization mechanism.


---

# 30. Swagger / OpenAPI

All Cart endpoints must be documented.

Swagger must correctly represent:

- Guest endpoints
- Authenticated endpoints
- Authentication requirements
- Request DTOs
- Response DTOs
- Validation errors
- ProblemDetails
- Localization behavior where applicable


The existing Swagger security conventions must be reused.


---

# 31. Configuration

Cart configuration must use:

- ASP.NET Core Configuration
- Options Pattern where appropriate
- Environment-specific configuration
- Environment variables/secrets where required


Potential configuration:

- Redis connection
- Cart TTL
- Redis key namespace
- serialization options if configurable


Secrets must never be hard-coded or committed.


---

# 32. Observability

Cart must follow the existing observability architecture.

Operations should participate in:

- structured logging
- correlation IDs
- trace IDs
- existing request tracing
- metrics where already supported
- health checks where appropriate


Important operations to observe:

- Add Item
- Update Quantity
- Remove Item
- Clear Cart
- Merge Cart
- Redis failures
- Catalog contract failures
- concurrency conflicts


No new observability framework should be introduced.


---

# 33. Testing Strategy

Phase 3 must introduce progressive testing.

## 33.1 Unit Tests

Cover:

- Cart creation
- Add Item
- duplicate Variant behavior
- quantity increments
- Update Quantity
- Remove Item
- Clear
- Merge
- duplicate items during merge
- empty cart merge
- invalid quantities
- domain invariants


## 33.2 Infrastructure Integration Tests

Cover:

- Redis connectivity
- Save
- Get
- Update
- Delete
- serialization/deserialization
- TTL
- key isolation
- concurrency behavior


## 33.3 Application Integration Tests

Cover:

- Guest Cart
- Authenticated Cart
- Catalog contract
- ownership
- merge
- error mapping


## 33.4 API Integration Tests

Cover:

- GET Cart
- Add
- Update
- Remove
- Clear
- Merge
- guest access
- authenticated access
- unauthorized ownership access
- validation
- localized errors


## 33.5 Architecture Tests

Verify:

- Cart Domain does not depend on Infrastructure
- Cart Application does not depend on Redis implementation
- Cart does not depend on Catalog Infrastructure
- Cart does not depend on Identity Infrastructure
- Cart does not access another module's DbContext
- Cart follows BuildingBlocks restrictions
- API does not contain business logic


---

# 34. Performance Requirements

The Cart is a high-frequency read/write component.

The implementation must consider:

- Redis round trips
- serialization size
- avoiding N+1 Catalog calls
- avoiding unnecessary Redis calls
- avoiding unnecessary TTL refreshes
- efficient Cart mutations
- atomic/concurrency-safe operations


Do not prematurely introduce:

- Redis distributed locks
- Lua scripts everywhere
- complex caching layers
- secondary databases
- Elasticsearch


Use the simplest implementation that satisfies correctness and performance.


---

# 35. Security Requirements

The implementation must protect against:

- Cart identifier guessing
- Unauthorized Cart access
- Cross-user Cart modification
- forged Cart ownership
- leaking internal Redis keys
- PII stored unnecessarily in Redis


Guest Cart identifiers must be opaque and sufficiently unpredictable.


---

# 36. Explicitly Out of Scope

The following must NOT be implemented in Phase 3.

## Orders

- Order Aggregate
- Order State Machine
- Create Order
- Order persistence
- Order cancellation

## Checkout

- Final Checkout workflow
- Final price calculation
- Order creation

## Inventory

- Stock
- Reservation
- Reservation expiration
- Inventory concurrency

## Messaging

- RabbitMQ
- Outbox publishing
- Consumers
- DLQ
- message retry

## Saga

- Order Saga
- Inventory Saga
- Compensation workflow

## Payments

- Stripe
- Payment Intent
- Webhooks
- Payment idempotency

## Pricing

- Pricing engine
- tax engine
- discount engine
- price history

## Promotions

- coupons
- promotion rules
- stacking rules

## Search

- Elasticsearch
- product indexing


These capabilities belong to later phases according to the project roadmap.


---

# 37. Phase 3 Sub-Phases

Phase 3 is divided into the following execution sub-phases.


## 3.1 — Cart Module Foundation

### Objective

Prepare the Cart Module structure and infrastructure boundaries without implementing
business behavior.

### Scope

- Review existing Cart projects
- Review existing Cart DbContext foundation
- Establish Redis as Cart persistence
- Verify module project structure
- Configure DI
- Configure Redis
- Define Cart configuration/options
- Define Contracts boundaries
- Define Application abstractions
- Verify module isolation
- Verify existing BuildingBlocks conventions

### Deliverables

- Cart Domain project ready
- Cart Application project ready
- Cart Infrastructure project ready
- Cart Contracts project ready
- Redis configuration
- DI registration
- Cart persistence abstraction
- Architecture tests for initial boundaries

### Out of Scope

- Cart business behavior
- API endpoints
- Merge
- Catalog integration


---

# 3.2 — Cart Domain Model

### Objective

Implement the Cart aggregate and all core Cart invariants.

### Scope

- Cart Aggregate Root
- CartItem
- ownership model
- Variant identity
- quantity rules
- Add
- Update
- Remove
- Clear
- Merge domain behavior
- domain errors
- domain events if justified

### Verification

Unit tests must verify all domain invariants.


---

# 3.3 — Redis Cart Persistence

### Objective

Implement reliable Redis-backed Cart persistence.

### Scope

- ICartStore
- Redis implementation
- serialization
- key naming
- save
- get
- delete
- existence checks
- TTL
- version/concurrency strategy

### Verification

Integration tests must run against real Redis.

Test:

- round trip
- serialization
- TTL
- missing key
- key isolation
- concurrent updates


---

# 3.4 — Guest Cart

### Objective

Implement Guest Cart lifecycle.

### Scope

- guest Cart identifier
- create Cart
- get Cart
- add item
- update item
- remove item
- clear Cart
- expiration
- ownership protection

### Verification

Test the complete guest lifecycle.


---

# 3.5 — Authenticated Cart

### Objective

Integrate Cart with the existing Identity abstraction.

### Scope

- ICurrentUser integration
- UserId ownership
- one active Cart per user
- authenticated Get
- Add
- Update
- Remove
- Clear

### Constraints

No direct Identity infrastructure dependency.


---

# 3.6 — Cart Merge

### Objective

Implement Guest → Authenticated Cart merge.

### Scope

- merge use case
- duplicate Variant handling
- quantity aggregation
- empty guest cart
- missing user cart
- existing user cart
- guest cart consumption
- logical idempotency
- concurrency protection

### Verification

Test:

- guest only
- user only
- both carts
- duplicate items
- empty guest
- empty user
- repeated merge
- conflict scenarios


---

# 3.7 — Catalog Contract Integration

### Objective

Allow Cart to validate and snapshot the minimum required Variant information
without violating module boundaries.

### Scope

- define explicit Catalog contract
- implement Catalog-side contract
- Cart integration
- Variant availability validation
- price snapshot creation
- product/SKU snapshot data

### Constraints

Forbidden:

- CatalogDbContext access
- Catalog Infrastructure dependency
- direct entity sharing


### Verification

Test:

- valid Variant
- missing Variant
- unavailable Variant
- current price snapshot
- multiple Cart items
- Catalog failure


---

# 3.8 — Cart Application Use Cases

### Objective

Complete Application orchestration for all Cart operations.

### Scope

Commands:

- AddItem
- UpdateQuantity
- RemoveItem
- ClearCart
- MergeCart

Query:

- GetCart

### Responsibilities

Application handlers coordinate:

- validation
- ownership
- Domain
- Catalog contract
- Cart store
- Result mapping


No business logic belongs in Controllers.


---

# 3.9 — Cart API

### Objective

Expose the Cart functionality through REST API.

### Scope

- Cart Controller
- request DTOs
- response DTOs
- endpoint routing
- guest support
- authenticated support
- merge
- authorization
- ProblemDetails
- Swagger


### Verification

All endpoints must be manually verified through Swagger and automated API
integration tests.


---

# 3.10 — Validation, Localization & Error Handling

### Objective

Bring Cart into the project's cross-cutting conventions.

### Scope

- Cart error codes
- validators
- localized resources
- ProblemDetails
- Result mapping
- API validation
- culture behavior
- Swagger localization where applicable


### Verification

Verify:

- resource parity
- error code stability
- Arabic/English user-facing messages
- validation failures
- API error contracts


---

# 3.11 — Testing, Concurrency, Performance & Security Hardening

### Objective

Harden Cart before completion.

### Scope

Testing:

- Unit
- Integration
- API
- Architecture
- Concurrency

Performance:

- Redis round trips
- serialization size
- Catalog call count
- mutation performance

Security:

- guest identifier protection
- ownership
- unauthorized access
- Redis key isolation

Reliability:

- Redis unavailable
- Catalog unavailable
- stale Cart version
- merge conflict
- expired Cart


### Exit Criteria

No known blocking correctness, security, architecture, or performance issues.


---

# 3.12 — Documentation & Completion

### Objective

Close Phase 3 and prepare the project for Phase 4.

### Scope

Review:

- Swagger
- Redis configuration
- Cart API
- error resources
- architecture boundaries
- tests
- Docker/runtime configuration
- Project Context
- Architecture documentation

Create:

docs/completed/PHASE_3_COMPLETION_REPORT.md


The completion report must document:

- implemented scope
- architecture
- Redis model
- APIs
- Catalog integration
- Guest Cart
- Authenticated Cart
- Merge
- Expiration
- testing
- performance review
- security review
- known deferred items
- Phase 4 readiness


---

# 38. Sub-Phase Execution Rule

Each sub-phase must follow this sequence:

1. Read the current Project Context.
2. Read Architecture documentation.
3. Inspect the current implementation.
4. Inspect existing conventions from previous phases.
5. Define the intended change.
6. Implement only the current sub-phase.
7. Build the solution.
8. Run relevant tests.
9. Perform architecture verification.
10. Fix issues caused by the current sub-phase.
11. Update documentation where required.
12. Report completion before moving to the next sub-phase.


An AI agent must not:

- rewrite unrelated modules
- introduce future-phase features
- bypass existing abstractions
- create parallel architectural patterns
- modify completed Catalog behavior without a concrete requirement
- introduce new infrastructure without architectural justification


---

# 39. Phase 3 Dependency Map

Cart depends conceptually on:

Identity
    ↓
ICurrentUser

Catalog
    ↓
Explicit Contract

BuildingBlocks
    ↓
Shared abstractions

Redis
    ↓
Cart State


Cart must NOT depend directly on:

Identity Infrastructure
Catalog Infrastructure
Orders
Inventory
Payments
RabbitMQ
Saga


---

# 40. Phase 3 Completion Criteria

Phase 3 is complete only when all of the following are satisfied.

## Architecture

- [ ] Cart is an independent Module
- [ ] Module boundaries are respected
- [ ] No cross-module DbContext access
- [ ] Redis is the Cart persistence mechanism
- [ ] Existing architecture conventions are preserved

## Domain

- [ ] Cart aggregate implemented
- [ ] CartItem implemented
- [ ] Quantity rules enforced
- [ ] Duplicate Variant behavior implemented
- [ ] Add implemented
- [ ] Update implemented
- [ ] Remove implemented
- [ ] Clear implemented
- [ ] Merge implemented

## Guest

- [ ] Guest Cart supported
- [ ] Secure opaque Cart identity
- [ ] Guest expiration implemented
- [ ] Guest ownership enforced

## Authenticated

- [ ] Authenticated Cart supported
- [ ] ICurrentUser integration
- [ ] One active Cart per user
- [ ] Ownership enforced

## Merge

- [ ] Guest → User merge
- [ ] Duplicate quantities aggregated
- [ ] Guest Cart consumed after successful merge
- [ ] Repeated merge does not duplicate state
- [ ] Merge concurrency handled

## Catalog

- [ ] Explicit Catalog contract
- [ ] Variant validation
- [ ] Availability validation
- [ ] Temporary price snapshot
- [ ] No Catalog persistence dependency

## Redis

- [ ] Save/Get/Delete
- [ ] Serialization
- [ ] TTL
- [ ] Key isolation
- [ ] Concurrency protection
- [ ] Redis integration tests

## API

- [ ] Get Cart
- [ ] Add Item
- [ ] Update Quantity
- [ ] Remove Item
- [ ] Clear Cart
- [ ] Merge Cart
- [ ] Swagger documentation
- [ ] Correct guest/authenticated behavior

## Localization

- [ ] Error codes
- [ ] Validation resources
- [ ] Localized ProblemDetails
- [ ] Resource parity
- [ ] Existing localization architecture reused

## Testing

- [ ] Domain unit tests
- [ ] Application tests
- [ ] Redis integration tests
- [ ] API integration tests
- [ ] Architecture tests
- [ ] Concurrency tests
- [ ] Security tests

## Hardening

- [ ] Performance review
- [ ] Security review
- [ ] Redis failure behavior reviewed
- [ ] Catalog failure behavior reviewed
- [ ] No blocking issues

## Documentation

- [ ] Swagger reviewed
- [ ] Architecture updated if required
- [ ] Project Context updated
- [ ] PHASE_3_COMPLETION_REPORT.md created


---

# 41. Phase 4 Readiness

The final goal of Phase 3 is not merely to have a working Cart API.

The Cart must provide a clean foundation for Phase 4.

Expected future flow:

Cart
   ↓
Checkout
   ↓
Recalculate Price
   ↓
Validate Product
   ↓
Create Immutable Order Snapshot
   ↓
Order Module


Phase 3 must therefore leave the following responsibilities to Phase 4:

- Checkout orchestration
- final pricing
- final product validation
- immutable Order snapshot
- Order state machine
- Order persistence


Phase 3 must end at the Cart boundary.


---

# 42. Final Phase 3 Architecture

                         ┌──────────────────┐
                         │     Catalog      │
                         │      Module      │
                         └────────┬─────────┘
                                  │
                           Explicit Contract
                                  │
                                  ▼
┌───────────────────────────────────────────────────┐
│                   Cart Module                     │
│                                                   │
│  Domain                                           │
│  ├── Cart                                         │
│  └── CartItem                                     │
│                                                   │
│  Application                                      │
│  ├── AddItem                                      │
│  ├── UpdateQuantity                               │
│  ├── RemoveItem                                   │
│  ├── ClearCart                                    │
│  ├── GetCart                                      │
│  └── MergeCart                                    │
│                                                   │
│  Infrastructure                                  │
│  ├── Redis Store                                  │
│  ├── Serialization                                │
│  ├── TTL                                          │
│  └── Concurrency                                  │
│                                                   │
│  API                                              │
│  └── Cart Controller                              │
│                                                   │
│  Contracts                                        │
│  └── Explicit public/integration contracts       │
└────────────────────────┬──────────────────────────┘
                         │
                         ▼
                    ┌──────────┐
                    │  Redis   │
                    └──────────┘


Guest
  │
  ▼
Guest Cart
  │
  │ Login
  ▼
Cart Merge
  │
  ▼
Authenticated Cart
  │
  ▼
Phase 4 — Orders


---

# 43. Phase 3 Final Boundary

Phase 3 owns:

    Temporary Shopping State

Phase 4 owns:

    Orders + Checkout

Phase 5 owns:

    Inventory + Concurrency

Phase 6 owns:

    Outbox + RabbitMQ

Phase 7 owns:

    Idempotent Consumers

Phase 8 owns:

    Saga

Phase 9 owns:

    Payments


The Cart Module must be implemented completely within its boundary and must
remain intentionally unaware of the implementation details of future phases.
