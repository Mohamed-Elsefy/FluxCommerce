# PHASE 4 --- Orders & Checkout

## 1. Phase Overview

Phase 4 introduces the **Orders Module** and transforms the temporary
Cart state into a durable, immutable commercial Order.

The primary flow is:

``` text
Authenticated Customer
        │
        ▼
      Cart
        │
        ▼
    Checkout
        │
        ├── Cart validation
        ├── Catalog revalidation
        ├── Current price resolution
        ├── Order calculation
        ├── Immutable order snapshot
        └── Checkout idempotency
        │
        ▼
   Order Aggregate
        │
        ▼
 OrdersDbContext
        │
        ▼
    SQL Server
```

The Order becomes the durable commercial record of the checkout
operation and must not depend on mutable Cart or Catalog state after
creation.

------------------------------------------------------------------------

# 2. Phase Objectives

Phase 4 must establish a production-ready Orders bounded context that:

1.  Owns the Order Aggregate and its invariants.
2.  Converts an authenticated customer's Cart into an Order.
3.  Revalidates Catalog data during Checkout.
4.  Recalculates authoritative commercial prices at Checkout.
5.  Creates an immutable commercial snapshot.
6.  Persists Orders in the Orders database.
7.  Supports Checkout API idempotency.
8.  Supports Customer Order queries.
9.  Supports Order cancellation within the Phase 4 lifecycle.
10. Establishes clean contracts between Orders, Cart, and Catalog.
11. Preserves the modular-monolith boundaries.
12. Provides comprehensive tests, security review, performance review,
    localization, Swagger, and documentation.

------------------------------------------------------------------------

# 3. Architectural Decisions

The following decisions are authoritative for Phase 4.

## 3.1 Orders Scope

Phase 4 includes:

-   Orders Domain
-   Order Aggregate
-   Order Items
-   Money
-   Order snapshots
-   Order totals
-   Order lifecycle
-   Cancellation
-   Checkout orchestration
-   Cart integration
-   Catalog revalidation
-   Current commercial price resolution
-   Checkout idempotency
-   Orders persistence
-   Customer Order queries
-   Order REST API
-   Authorization
-   Localization
-   Swagger
-   Unit tests
-   Integration tests
-   Architecture tests
-   Security review
-   Performance review
-   Documentation

## 3.2 Explicitly Out of Scope

The following must NOT be implemented in Phase 4:

``` text
Inventory reservation
Inventory concurrency
Outbox
RabbitMQ
Message consumers
Consumer idempotency
Order/Inventory Saga
Payments
Payment compensation
Shipping capability
Promotions/Coupons engine
Search infrastructure
```

These capabilities belong to later phases according to the project
roadmap.

------------------------------------------------------------------------

# 4. Core Architectural Boundaries

The intended dependency structure is:

``` text
                 Identity
                    │
                    ▼
              ICurrentUser
                    │
                    ▼
Cart ───────► Checkout / Orders ◄────── Catalog
                    │
                    ▼
              OrdersDbContext
                    │
                    ▼
                SQL Server
```

Orders must NOT directly depend on:

-   Catalog Infrastructure
-   Catalog DbContext
-   Cart Redis implementation
-   Cart Infrastructure
-   Inventory Infrastructure
-   Payments Infrastructure
-   RabbitMQ
-   Stripe

Cross-module interaction must use explicit contracts.

------------------------------------------------------------------------

# 5. Checkout Model

Checkout is an Application-level orchestration workflow.

The intended flow is:

``` text
Authenticated User
        │
        ▼
POST /orders/checkout
        │
        ▼
Validate Idempotency-Key
        │
        ▼
Check Existing Checkout
        │
   ┌────┴────┐
   │         │
Existing    New
   │         │
Return      Load Cart
Order          │
               ▼
        Validate Ownership
               │
               ▼
          Validate Not Empty
               │
               ▼
      Batch Catalog Revalidation
               │
               ▼
       Recalculate Current Prices
               │
               ▼
         Build Order Aggregate
               │
               ▼
      Persist Order + Idempotency
               │
               ▼
           Clear Cart
               │
               ▼
          Return Order
```

Inventory, Payments, Messaging, and Saga orchestration are not part of
this workflow in Phase 4.

------------------------------------------------------------------------

# 6. Checkout Customer Model

Phase 4 supports **authenticated Checkout only**.

Guest Cart remains supported by Phase 3, but Guest Checkout is
explicitly deferred.

The expected flow is:

``` text
Guest Cart
    │
    ▼
Authentication
    │
    ▼
Cart Merge
    │
    ▼
Authenticated Cart
    │
    ▼
Checkout
    │
    ▼
Order
```

Orders must use the existing `ICurrentUser` abstraction and must not
depend directly on Identity infrastructure.

------------------------------------------------------------------------

# 7. Order Aggregate

`Order` is the Aggregate Root.

Conceptually:

``` text
Order
├── OrderId
├── CustomerId
├── Status
├── Items
├── Currency
├── Subtotal
├── DiscountTotal
├── TaxTotal
├── ShippingTotal
├── GrandTotal
├── ShippingAddressSnapshot
├── CreatedAt
├── UpdatedAt
└── Domain Events
```

The Order Aggregate is responsible for protecting Order invariants and
lifecycle rules.

Application code must not directly mutate sensitive domain state.

Prefer:

``` csharp
order.Cancel();
```

over:

``` csharp
order.Status = OrderStatus.Cancelled;
```

------------------------------------------------------------------------

# 8. Order Item

`OrderItem` is an entity owned by the Order Aggregate.

It must contain the immutable commercial information required to
represent the purchased item:

``` text
OrderItem
├── ProductId
├── VariantId
├── SKU
├── ProductNameSnapshot
├── UnitPrice
├── Quantity
├── LineTotal
└── Currency
```

The Order Item must not maintain runtime dependencies on Catalog.

------------------------------------------------------------------------

# 9. Immutable Commercial Snapshot

At successful Checkout, the Order must capture the authoritative
commercial state.

The snapshot must contain, at minimum:

-   ProductId
-   VariantId
-   SKU
-   ProductNameSnapshot
-   UnitPrice
-   Quantity
-   LineTotal
-   Currency

After Order creation:

``` text
Catalog changes
     X
Order
```

Changing a Product, Variant, SKU, or current Catalog price must not
alter an existing Order.

Cart price snapshots are temporary and non-authoritative. Checkout must
use current Catalog data to construct the final Order snapshot.

------------------------------------------------------------------------

# 10. Money Model

Monetary values must use a `Money` Value Object or the project's
established equivalent.

Conceptually:

``` text
Money
├── Amount
└── Currency
```

Rules:

-   Use `decimal`, never `double` or `float`.
-   Currency is required.
-   Money values from different currencies cannot be combined.
-   All monetary values inside one Order must use a consistent currency.
-   SQL precision must be explicitly configured.
-   Monetary arithmetic must be centralized and deterministic.

------------------------------------------------------------------------

# 11. Pricing Scope

Phase 4 implements the minimum Checkout pricing capability required to
create a correct Order.

Current scope:

``` text
Unit Price × Quantity = Line Total

Σ Line Totals = Subtotal
```

Current totals:

``` text
DiscountTotal = 0
TaxTotal      = 0
ShippingTotal = 0
GrandTotal    = Subtotal
```

Do NOT implement a complete Pricing/Promotion/Tax/Shipping engine in
Orders.

The design must remain extensible so future pricing capabilities can
contribute to Checkout without requiring an Order Aggregate redesign.

------------------------------------------------------------------------

# 12. Catalog Revalidation

Cart price and product snapshots are not authoritative at Checkout.

Orders must request current Catalog information through an explicit
batch contract.

Conceptually:

``` text
VariantId[]
    │
    ▼
Catalog Contract
    │
    ▼
Current Variant State
```

The contract should provide only the data required by Checkout, such as:

``` text
VariantId
ProductId
SKU
ProductName
CurrentPrice
Currency
Sellable/Eligible State
```

Do not perform one Catalog request per Cart item.

Use batch retrieval to avoid N+1 communication.

Orders must never access `CatalogDbContext`.

------------------------------------------------------------------------

# 13. Product Eligibility

For Phase 4, a Variant must satisfy:

``` text
Exists
AND
Active/Sellable
AND
Commercially purchasable
```

Stock availability is NOT part of Orders validation.

Inventory availability and reservation belong to Phase 5.

Therefore:

``` text
Catalog
→ Can this variant currently be sold?

Inventory
→ Can this quantity actually be reserved?
```

These responsibilities must remain separate.

------------------------------------------------------------------------

# 14. Address Snapshot

Orders may store a `ShippingAddressSnapshot` as part of the immutable
Order record.

The snapshot represents the address associated with the Order at
Checkout time.

Phase 4 does NOT implement:

-   Shipping service
-   Shipping rates
-   Shipping methods
-   Tracking
-   Delivery orchestration
-   Shipping workflow

Address information is stored as Order data, not as a dependency on a
future Shipping module.

------------------------------------------------------------------------

# 15. Order Lifecycle

Phase 4 implements only lifecycle states that have real behavior in this
phase.

Initial lifecycle:

``` text
Pending
   │
   ▼
Cancelled
```

Allowed transition:

``` text
Pending → Cancelled
```

Invalid transitions include:

``` text
Cancelled → Pending
Cancelled → Cancelled
```

Future states such as:

``` text
AwaitingPayment
Paid
Processing
Shipped
Delivered
```

must not be implemented merely for future completeness. They will be
introduced when their corresponding business capabilities exist.

------------------------------------------------------------------------

# 16. Cancellation

Cancellation is supported in Phase 4.

Rules:

``` text
Pending → Cancelled
```

Cancellation must be implemented through domain behavior:

``` csharp
order.Cancel();
```

Phase 4 cancellation does NOT:

-   Release inventory.
-   Refund payments.
-   Trigger Saga compensation.
-   Call Payment services.
-   Call Inventory services.

Those behaviors belong to later phases.

------------------------------------------------------------------------

# 17. Cart Contract

Orders must not access Cart Redis or Cart infrastructure directly.

Orders should consume a Checkout-specific contract that exposes only the
required information.

Conceptually:

``` text
CartCheckoutSnapshot
├── CartId
├── CustomerId
└── Items
    ├── ProductId
    ├── VariantId
    ├── SKU
    ├── Quantity
    └── Temporary Price Snapshot
```

The Cart Aggregate, Redis models, and Redis store must remain internal
to Cart.

------------------------------------------------------------------------

# 18. Cart Ownership

Checkout must validate that the Cart belongs to the authenticated
customer.

The system must prevent:

``` text
User A
   │
   └── Checkout Cart B
```

The ownership check must occur before Order creation.

------------------------------------------------------------------------

# 19. Empty Cart

Checkout of an empty Cart must fail.

No Order may be created when:

``` text
Cart.Items.Count == 0
```

The Cart remains unchanged after the failure.

------------------------------------------------------------------------

# 20. Checkout Idempotency

Checkout API must be idempotent.

The request requires:

``` http
Idempotency-Key: <key>
```

Idempotency must be persistent and SQL-backed within the Orders
boundary.

Conceptually:

``` text
CheckoutIdempotency
├── UserId
├── IdempotencyKey
├── OrderId
├── RequestFingerprint
└── CreatedAt
```

Uniqueness scope:

``` text
(UserId, IdempotencyKey)
```

The system must support:

``` text
Same User
Same Key
Same Request
    ↓
Return Existing Order
```

and reject:

``` text
Same User
Same Key
Different Request
    ↓
Idempotency Conflict
```

Do not implement RabbitMQ consumer idempotency here. That belongs to a
later phase.

------------------------------------------------------------------------

# 21. Checkout Transaction Boundary

The following must be atomic inside the Orders database where possible:

``` text
Order creation
+
CheckoutIdempotency record
```

Do NOT attempt a distributed transaction across:

``` text
Orders SQL Server
+
Cart Redis
+
Catalog database
```

The Cart is cleared after successful Order persistence.

If Cart clearing fails after Order creation, retrying the same
idempotent Checkout request must return the existing Order rather than
create another Order.

------------------------------------------------------------------------

# 22. Order Persistence

Orders use SQL Server through `OrdersDbContext`.

Minimum tables:

``` text
Orders
OrderItems
CheckoutIdempotency
```

The Order database must not use foreign keys to entities owned by other
modules.

Cross-module IDs such as:

``` text
ProductId
VariantId
CustomerId
CartId
```

are business references, not cross-module relational foreign keys.

------------------------------------------------------------------------

# 23. Orders Queries

Phase 4 provides:

``` http
GET /orders
GET /orders/{orderId}
```

Customer queries must enforce ownership.

Example:

``` text
User A → Order A ✓
User A → Order B ✗
```

Order list should support the project's established pagination and
sorting conventions.

Basic status filtering may be supported.

Do not introduce a separate read database or search infrastructure.

------------------------------------------------------------------------

# 24. Order Cancellation API

The expected API is:

``` http
POST /orders/{orderId}/cancel
```

Authorization rules:

-   User must be authenticated.
-   User must own the Order.
-   Order must be in a cancellable state.
-   Phase 4 allows cancellation only from `Pending`.

------------------------------------------------------------------------

# 25. API Surface

The baseline API surface is:

``` http
POST /orders/checkout

GET /orders

GET /orders/{orderId}

POST /orders/{orderId}/cancel
```

Exact route naming should follow existing project API conventions.

Swagger must document:

-   Authentication.
-   `Idempotency-Key`.
-   Request schemas.
-   Response schemas.
-   Error responses.
-   Authorization requirements.

------------------------------------------------------------------------

# 26. Domain Events

The Order Domain may produce Domain Events such as:

``` text
OrderCreated
OrderCancelled
```

However:

``` text
Domain Event != Integration Event
```

Phase 4 does not implement:

-   Outbox
-   RabbitMQ
-   Event dispatcher infrastructure
-   Consumers

Later phases can connect Order Domain Events to Outbox and messaging
without redesigning the Order Aggregate.

------------------------------------------------------------------------

# 27. Localization

Use the project's existing Localization capability.

Domain and Application layers must not depend directly on resource
implementations.

Use the existing abstractions and conventions.

Errors should remain stable and machine-readable:

``` text
Error.Code
+
Localized Message
```

Do not hard-code user-facing messages in Domain/Application logic.

------------------------------------------------------------------------

# 28. Authorization

Required authorization model:

``` text
Checkout
→ Authenticated Customer

My Orders
→ Authenticated Customer

Order Details
→ Owner

Cancel Order
→ Owner + Allowed State

Administrative Reads
→ Existing Admin/Policy mechanism where applicable
```

The implementation must prevent IDOR and cross-user Order access.

------------------------------------------------------------------------

# 29. Testing Strategy

## 29.1 Domain Tests

Cover:

-   Order creation.
-   Empty Order rejection.
-   Item creation.
-   Quantity validation.
-   Money validation.
-   Currency consistency.
-   Line totals.
-   Order totals.
-   Lifecycle.
-   Cancellation.
-   Invalid transitions.
-   Domain errors.
-   Snapshot integrity.

## 29.2 Application Tests

Cover:

-   Checkout.
-   Cart ownership.
-   Empty Cart.
-   Catalog validation.
-   Price recalculation.
-   Order creation.
-   Idempotency.
-   Conflicting idempotency keys.
-   Customer queries.
-   Cancellation authorization.

## 29.3 Integration Tests

Cover:

``` text
Cart → Checkout → Order
Catalog → Checkout → Order
Order → SQL persistence
Idempotent retry
Conflicting idempotency reuse
Catalog validation failure
Cart loading failure
Order persistence failure
Cart clear failure
Order retrieval
Ownership protection
Cancellation
```

## 29.4 Architecture Tests

Verify:

-   Orders layer dependencies.
-   Module isolation.
-   No Catalog DbContext dependency.
-   No Cart infrastructure dependency.
-   No forbidden Inventory dependency.
-   No Payments dependency.
-   No Messaging/RabbitMQ dependency.
-   Contracts remain clean.

------------------------------------------------------------------------

# 30. Performance Requirements

Review and verify:

-   Batch Catalog revalidation.
-   No N+1 Catalog calls.
-   Efficient Order queries.
-   Projection for read endpoints.
-   Correct indexes.
-   Efficient idempotency lookup.
-   Reasonable SQL transaction scope.
-   No unnecessary entity loading.

The implementation must not introduce distributed calls inside item
loops.

------------------------------------------------------------------------

# 31. Security Requirements

Review:

-   Authentication.
-   Authorization.
-   Order ownership.
-   IDOR protection.
-   Idempotency-key abuse.
-   Cross-user access.
-   Cart ownership.
-   Sensitive information exposure.
-   PII logging.
-   Error information leakage.

No Order endpoint should accept a CustomerId from the client as an
authority source. Customer identity must come from the authenticated
context.

------------------------------------------------------------------------

# 32. Sub-Phase Execution Plan

## 4.1 --- Orders Module Foundation & Architecture

### Scope

-   Review existing Orders skeleton.
-   Verify Domain/Application/Infrastructure/Contracts.
-   Verify `OrdersDbContext`.
-   Verify BaseDbContext integration.
-   Verify DI registration.
-   Verify configuration.
-   Verify migrations infrastructure.
-   Establish architecture-test boundaries.

### Exit Criteria

-   Orders module builds.
-   DI resolves.
-   DbContext is correctly configured.
-   Architecture boundaries are enforced.
-   No business behavior is introduced prematurely.

------------------------------------------------------------------------

## 4.2 --- Order Domain Model & Monetary Model

### Scope

-   Implement Order Aggregate.
-   Implement OrderItem.
-   Implement Money.
-   Implement Order totals.
-   Implement Product/Variant snapshots.
-   Implement Address snapshot.
-   Implement domain invariants.
-   Add domain unit tests.

### Exit Criteria

-   Domain is persistence-independent.
-   All core invariants are enforced.
-   Monetary calculations are deterministic.
-   Tests pass.

------------------------------------------------------------------------

## 4.3 --- Order Lifecycle, Cancellation & Domain Errors

### Scope

-   Implement `OrderStatus`.
-   Implement Pending/Cancelled lifecycle.
-   Implement cancellation behavior.
-   Implement domain errors.
-   Implement domain events where appropriate.
-   Add exhaustive lifecycle tests.

### Exit Criteria

-   Valid transitions work.
-   Invalid transitions fail deterministically.
-   Cancellation rules are protected by the Aggregate.
-   Error codes follow project conventions.

------------------------------------------------------------------------

## 4.4 --- Orders Persistence & Database Schema

### Scope

-   Configure Orders EF entities.
-   Configure relationships.
-   Configure monetary precision.
-   Configure indexes.
-   Configure constraints.
-   Configure `CheckoutIdempotency`.
-   Create migration.
-   Apply migration.
-   Verify database.

### Exit Criteria

-   Database schema matches domain requirements.
-   No cross-module foreign keys exist.
-   Migration applies cleanly.
-   Persistence tests pass.

------------------------------------------------------------------------

## 4.5 --- Cart & Catalog Checkout Contracts

### Scope

-   Define Cart Checkout contract.
-   Define Catalog Checkout/revalidation contract.
-   Implement Cart contract provider.
-   Implement Catalog batch provider.
-   Add contract DTOs.
-   Enforce module boundaries.
-   Add architecture/integration tests.

### Exit Criteria

-   Orders can consume required Cart data through a contract.
-   Orders can batch revalidate Catalog variants.
-   Orders has no direct infrastructure dependency on Cart/Catalog.
-   No N+1 communication exists.

------------------------------------------------------------------------

## 4.6 --- Checkout Pricing & Immutable Order Snapshot

### Scope

-   Implement current-price resolution.
-   Recalculate line totals.
-   Calculate Order subtotal.
-   Establish current Phase 4 totals.
-   Create immutable Order snapshot.
-   Handle price changes between Cart creation and Checkout.
-   Add pricing/snapshot tests.

### Exit Criteria

-   Cart price is never treated as authoritative.
-   Current Catalog price is used.
-   Order snapshot is immutable.
-   Totals are internally consistent.
-   Currency is consistent.

------------------------------------------------------------------------

## 4.7 --- Checkout Application Workflow & Idempotency

### Scope

-   Implement Checkout command.
-   Validate authenticated customer.
-   Validate Cart ownership.
-   Validate Cart not empty.
-   Validate Idempotency-Key.
-   Implement persistent idempotency.
-   Implement request fingerprint.
-   Build Order.
-   Persist Order + idempotency record atomically.
-   Return existing Order on safe retry.
-   Reject conflicting key reuse.

### Exit Criteria

-   Checkout happy path works.
-   Same idempotent request never creates duplicate Orders.
-   Conflicting reuse is rejected.
-   Application orchestration remains free of domain business logic
    duplication.

------------------------------------------------------------------------

## 4.8 --- Cart Consumption & Checkout Failure Semantics

### Scope

-   Clear Cart after successful Order creation.
-   Define failure behavior.
-   Verify retry semantics.
-   Verify Order persistence failure behavior.
-   Verify Catalog failure behavior.
-   Verify Cart clear failure behavior.
-   Add integration tests.

### Exit Criteria

-   Failed Checkout does not create Orders.
-   Successful Checkout creates exactly one Order.
-   Cart is cleared after successful creation.
-   Cart-clear failure cannot cause duplicate Orders on retry.
-   No distributed transaction is introduced.

------------------------------------------------------------------------

## 4.9 --- Order Queries & REST API

### Scope

-   Implement My Orders.
-   Implement Order Details.
-   Implement Order cancellation.
-   Implement pagination.
-   Implement sorting/filtering where applicable.
-   Implement ownership authorization.
-   Add Contracts DTOs.
-   Configure Swagger.
-   Add API integration tests.

### Exit Criteria

-   Customer can access only owned Orders.
-   API follows existing conventions.
-   Responses expose immutable Order snapshot data.
-   Cancellation follows Domain rules.
-   Swagger accurately documents the API.

------------------------------------------------------------------------

## 4.10 --- Hardening, Testing, Documentation & Completion

### Scope

-   Validation review.
-   Authorization review.
-   Localization review.
-   Swagger review.
-   Security review.
-   Performance review.
-   Unit tests review.
-   Integration tests review.
-   Architecture tests review.
-   EF migration review.
-   Documentation review.
-   Update `PROJECT_CONTEXT.md` if required.
-   Update `ARCHITECTURE.md` if required.
-   Create `PHASE_4_COMPLETION_REPORT.md`.

### Exit Criteria

-   Full solution build succeeds.
-   All relevant tests pass.
-   Architecture tests pass.
-   No warnings/errors introduced.
-   Documentation is synchronized.
-   Orders is production-ready within Phase 4 scope.

------------------------------------------------------------------------

# 33. Dependency Order

The Sub-Phases must be executed in this order:

``` text
4.1 Orders Foundation
        │
        ▼
4.2 Order Domain + Money
        │
        ▼
4.3 Lifecycle + Errors
        │
        ▼
4.4 Persistence
        │
        ▼
4.5 Cart/Catalog Contracts
        │
        ▼
4.6 Pricing + Snapshot
        │
        ▼
4.7 Checkout + Idempotency
        │
        ▼
4.8 Cart Consumption + Failure Semantics
        │
        ▼
4.9 Queries + API
        │
        ▼
4.10 Hardening + Documentation
```

Do not start Checkout implementation before the Domain, Persistence, and
module contracts are sufficiently established.

------------------------------------------------------------------------

# 34. Phase Completion Criteria

Phase 4 is complete only when all of the following are satisfied.

## Architecture

-   [ ] Orders module is isolated.
-   [ ] Correct dependency direction.
-   [ ] No cross-module DbContext access.
-   [ ] Explicit Cart contract.
-   [ ] Explicit Catalog contract.
-   [ ] No forbidden future-phase dependencies.

## Domain

-   [ ] Order Aggregate.
-   [ ] OrderItem.
-   [ ] Money.
-   [ ] Immutable snapshots.
-   [ ] Totals.
-   [ ] Lifecycle.
-   [ ] Cancellation.
-   [ ] Domain errors.
-   [ ] Domain events where appropriate.

## Checkout

-   [ ] Authenticated Checkout.
-   [ ] Cart ownership.
-   [ ] Empty Cart protection.
-   [ ] Catalog revalidation.
-   [ ] Current price resolution.
-   [ ] Final calculation.
-   [ ] Immutable Order snapshot.
-   [ ] Order persistence.
-   [ ] Cart consumption.

## Idempotency

-   [ ] SQL-backed.
-   [ ] User-scoped.
-   [ ] Unique constraint.
-   [ ] Request fingerprint.
-   [ ] Safe retry.
-   [ ] Conflicting reuse rejection.

## API

-   [ ] Checkout.
-   [ ] My Orders.
-   [ ] Order Details.
-   [ ] Cancellation.
-   [ ] Authorization.
-   [ ] Swagger.
-   [ ] ProblemDetails.

## Quality

-   [ ] Localization.
-   [ ] Unit tests.
-   [ ] Application tests.
-   [ ] Integration tests.
-   [ ] Architecture tests.
-   [ ] Security review.
-   [ ] Performance review.

## Documentation

-   [ ] EF migrations verified.
-   [ ] Swagger reviewed.
-   [ ] Architecture documentation synchronized.
-   [ ] Project Context synchronized.
-   [ ] Phase completion report created.

------------------------------------------------------------------------

# 35. Phase 4 Deliverables

At the end of Phase 4 the project must contain:

``` text
FluxCommerce.Modules.Orders.Domain
FluxCommerce.Modules.Orders.Application
FluxCommerce.Modules.Orders.Infrastructure
FluxCommerce.Modules.Orders.Contracts
```

Core domain:

``` text
Order
OrderItem
Money
OrderStatus
AddressSnapshot
Domain Errors
Domain Events
```

Persistence:

``` text
OrdersDbContext
Orders
OrderItems
CheckoutIdempotency
EF Configurations
EF Migration
```

Application capabilities:

``` text
Checkout
GetMyOrders
GetOrder
CancelOrder
```

API:

``` text
POST /orders/checkout
GET  /orders
GET  /orders/{orderId}
POST /orders/{orderId}/cancel
```

Cross-cutting:

``` text
Authorization
Localization
Validation
ProblemDetails
Swagger
Logging
```

Tests:

``` text
Domain Unit Tests
Application Tests
Integration Tests
Architecture Tests
```

Documentation:

``` text
PHASE_4_COMPLETION_REPORT.md
PROJECT_CONTEXT.md updates
ARCHITECTURE.md updates where required
```

------------------------------------------------------------------------

# 36. Future Integration Boundary

Phase 4 must leave a clean extension point for the following roadmap:

``` text
Phase 4
Orders / Checkout
        │
        ▼
Phase 5
Inventory + Concurrency
        │
        ▼
Phase 6
Outbox + RabbitMQ
        │
        ▼
Phase 7
Idempotent Consumers
        │
        ▼
Phase 8
Order / Inventory Saga
        │
        ▼
Phase 9
Payments
        │
        ▼
Phase 10
Payment Compensation
```

Phase 4 must therefore avoid implementing future capabilities
prematurely while ensuring that the Order Aggregate, Domain Events,
contracts, and persistence model can evolve without redesign.

------------------------------------------------------------------------

# 37. Architectural Completion Statement

Phase 4 is successful when the system can reliably perform:

``` text
Authenticated Customer
        │
        ▼
      Cart
        │
        ▼
    Checkout
        │
        ├── Validate Cart
        ├── Revalidate Catalog
        ├── Recalculate Price
        ├── Build Order
        ├── Persist Immutable Snapshot
        ├── Persist Idempotency Record
        └── Clear Cart
        │
        ▼
      Order
        │
        ├── Query
        └── Cancel
```

while preserving the project's modular-monolith boundaries and leaving
Inventory, Messaging, Saga, and Payments for their designated future
phases.
