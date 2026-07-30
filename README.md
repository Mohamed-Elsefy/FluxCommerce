
````markdown
# Enterprise Commerce & Order Management Platform

A production-oriented Enterprise Commerce & Order Management Platform built with ASP.NET Core and .NET.

The project is designed to demonstrate how to build a reliable commerce system that goes beyond traditional CRUD-based e-commerce applications.

The primary engineering focus is the reliable processing of the Order lifecycle across distributed business operations such as Inventory and Payments, while handling concurrency, asynchronous messaging, duplicate messages, retries, partial failures, and recovery.

---

## Overview

The platform simulates a real-world enterprise e-commerce system that manages the complete commerce lifecycle:

```text
Customer
    │
    ▼
Browse Products
    │
    ▼
Add to Cart
    │
    ▼
Checkout
    │
    ▼
Validate Cart
    │
    ▼
Recalculate Prices
    │
    ▼
Create Order
    │
    ▼
Reserve Inventory
    │
    ▼
Initiate Payment
    │
    ▼
Customer Pays
    │
    ▼
Payment Webhook
    │
    ▼
Confirm Order
    │
    ▼
Fulfillment
    │
    ▼
Shipping
    │
    ▼
Delivered
````

The system is intentionally designed to handle failure scenarios such as:

```text
Inventory Reserved
        │
        ▼
Payment Failed
        │
        ▼
Release Inventory
        │
        ▼
Cancel Order
```

And:

```text
Payment Succeeded
        │
        ▼
Order Confirmation Failed
        │
        ▼
Retry
        │
        ▼
Retry Limit Reached
        │
        ▼
Dead Letter Queue / Recovery Policy
```

And:

```text
Event Published
        │
        ▼
Consumer Crashes
        │
        ▼
Message Redelivered
        │
        ▼
Idempotency Check
        │
        ▼
Duplicate Detected
        │
        ▼
Safe Reprocessing
```

The goal is to build a system that remains reliable and recoverable when individual operations fail or when asynchronous processing is interrupted.

---

## Engineering Goals

The project is designed to demonstrate practical knowledge of:

* Software Architecture
* System Design
* Domain-Driven Design principles
* Modular Monolith Architecture
* Clean Architecture
* Event-Driven Architecture
* Distributed Systems concepts
* Transactional Outbox Pattern
* Idempotent Consumers
* Saga Orchestration
* Compensating Transactions
* Inventory Reservation
* Concurrency Control
* Retry Policies
* Dead Letter Queues
* Background Processing
* Distributed Caching
* Observability
* Failure Recovery

The most important engineering objective is:

> Build a reliable Order Processing System rather than simply building a collection of CRUD APIs.

---

## Architecture

The initial backend architecture is a **Modular Monolith**.

The system is deployed as one application while maintaining explicit boundaries between business modules.

The architecture is designed so that selected modules can potentially be extracted into independently deployable services in the future if a real architectural reason exists.

```text
                    Angular Applications
                 /                       \
                /                         \
       Customer Storefront            Admin Portal
                \                         /
                 \                       /
                  ▼                       ▼
              ASP.NET Core API Host
                       │
                       ▼
              Modular Monolith
                       │
        ┌──────────────┼───────────────┐
        │              │               │
        ▼              ▼               ▼
      Catalog        Orders         Inventory
        │              │               │
        │              ▼               │
        │           Payments            │
        │              │               │
        └──────────────┼───────────────┘
                       │
                       ▼
                 SQL Server
                       │
                       ├──────────► Redis
                       │
                       ▼
                    Outbox
                       │
                       ▼
                   RabbitMQ
                       │
                       ▼
              Background Workers
                       │
                       ▼
              External Integrations
                       │
                       ▼
                    Stripe
```

The complete architecture is introduced incrementally.

The entire distributed architecture is intentionally **not** implemented during the initial foundation phase.

---

## Architectural Principles

### Modular Monolith First

The backend starts as a Modular Monolith.

Each module owns:

* Domain logic
* Application use cases
* Persistence logic
* Business state

Modules communicate through explicit contracts.

A module must not directly access another module's internal implementation.

---

### Explicit State Ownership

Each module owns its own business state.

```text
Orders
    → Order State

Inventory
    → Stock State
    → Reservation State

Payments
    → Payment State
    → Payment Transaction State
```

The Saga coordinates workflows but does not own the internal state of other modules.

---

### No Cross-Module Direct Database Writes

Incorrect:

```text
Order Module
    │
    ▼
InventoryDbContext
    │
    ▼
Update Inventory State
```

Correct:

```text
Order Module
    │
    ▼
Command / Integration Event
    │
    ▼
Inventory Module
    │
    ▼
Inventory State Change
```

Modules must communicate through approved contracts and messaging mechanisms.

---

### Eventual Consistency

The system accepts eventual consistency where asynchronous processing is appropriate.

The architecture does not attempt to maintain one large distributed transaction across:

* Orders
* Inventory
* Payments
* Stripe

Instead, the system uses:

* Integration Events
* Transactional Outbox
* Message Broker
* Saga Orchestration
* Compensating Actions

---

### At-Least-Once Delivery

The messaging architecture assumes:

```text
At-Least-Once Delivery
```

Exactly-once delivery is not assumed.

Consumers that modify business state must therefore be idempotent.

---

### Idempotency

Idempotency is required at multiple levels.

#### API Idempotency

Important operations such as Checkout and Payment creation should support idempotency where appropriate.

Example:

```http
POST /checkout
Idempotency-Key: ABC123
```

Repeated requests using the same idempotency key must not create duplicate business operations.

#### Message Idempotency

Consumers must safely handle duplicate messages.

#### Webhook Idempotency

Stripe Webhooks must be processed idempotently.

#### Payment Intent Idempotency

Retries must not create multiple Payment Intents for the same Order.

---

## Main Modules

```text
Platform / Cross-Cutting
├── Identity & Access
└── Observability

Core Commerce
├── Catalog
├── Cart
├── Pricing
├── Orders
├── Inventory
└── Payments

Supporting Commerce
├── Promotions & Coupons
├── Wishlist
├── Reviews
└── Shipping

Communication & Reporting
├── Notifications
└── Analytics

Infrastructure Capabilities
├── Messaging
├── Outbox
├── Idempotency
├── Background Processing
└── Resilience
```

---

## Module Responsibilities

### Identity & Access

Responsibilities:

* Customer registration
* Admin management
* Login
* JWT Access Tokens
* Refresh Tokens
* Email Verification
* Password Reset
* Account Lockout
* Role-Based Authorization

Roles:

```text
Customer
Admin
SuperAdmin
```

Identity is treated as a Platform Capability.

Commerce modules should depend on abstractions such as:

```text
CurrentUser
UserId
Roles
```

rather than directly depending on Identity implementation details.

---

### Catalog

Responsibilities:

* Products
* Categories
* Product Variants
* SKU
* Product Images
* Dynamic Attributes and Specifications
* Product Lifecycle
* Soft Delete
* Versioning where required

Initial search strategy:

```text
SQL Server
    +
Filtering
    +
Sorting
    +
Pagination
```

A dedicated Search Engine is not part of the initial Catalog implementation.

---

### Product Search

The initial implementation uses SQL-based querying.

A future architecture may use:

```text
Catalog
    │
    ▼
ProductChanged Event
    │
    ▼
Search Indexer
    │
    ▼
Search Engine
```

Search infrastructure is introduced in a later phase.

---

### Cart

Responsibilities:

* Guest Cart
* Authenticated User Cart
* Add Item
* Update Quantity
* Remove Item
* Clear Cart
* Cart Merge after Login
* Cart Expiration

Redis is used for Cart state.

```text
Guest
   │
   ▼
Redis Cart
   │
   │ Login
   ▼
Merge Cart
   │
   ▼
Authenticated Cart
```

Cart prices are temporary snapshots.

They are not the final source of truth for Orders.

During Checkout:

```text
Cart
    │
    ▼
Recalculate
    │
    ├── Current Product Price
    ├── Discounts
    ├── Coupons
    ├── Tax
    └── Shipping
    │
    ▼
Create Immutable Order Snapshot
```

---

### Pricing

Responsibilities:

* Base Price
* Tax
* Currency
* Discounts
* Price Calculation
* Price History

The monetary model should use a consistent Money concept:

```text
Money
├── Amount
└── Currency
```

Money calculations must have explicit rules for rounding and currency handling.

---

### Promotions & Coupons

Supported concepts include:

* Percentage Discounts
* Fixed Discounts
* Free Shipping
* Expiration
* Minimum Order Value
* Category Restrictions
* Product Restrictions
* Global Usage Limits
* Per-User Usage Limits
* Stacking Rules

This module is introduced after the Core Order Processing workflow is stable.

---

### Orders

Responsibilities:

* Checkout
* Order Creation
* Immutable Order Snapshot
* Order Lifecycle
* Order History
* Cancellation
* Customer Order Queries

Orders preserve the commercial state at the time of purchase.

Example:

```text
OrderItem
├── ProductId
├── SKU
├── ProductNameSnapshot
├── UnitPriceSnapshot
├── Quantity
└── Total
```

The Order must not rely on live Catalog data after creation.

---

### Inventory

Responsibilities:

* Stock Management
* Available Quantity
* Reserved Quantity
* Inventory Reservations
* Release
* Commit
* Reservation Expiration
* Low Stock Alerts

Conceptually:

```text
InventoryItem
├── SKU
├── OnHandQuantity
├── ReservedQuantity
└── AvailableQuantity
```

Reservation:

```text
InventoryReservation
├── Id
├── OrderId
├── SKU
├── Quantity
├── Status
└── ExpiresAt
```

Inventory must prevent overselling under concurrent requests.

For example:

```text
Available = 1

Customer A → Reserve 1
Customer B → Reserve 1
```

Only one reservation may succeed.

Concurrency techniques may include:

* Atomic Conditional Updates
* Optimistic Concurrency
* Transactional Boundaries

---

### Payments

Responsibilities:

* Payment Intent
* Payment Attempts
* Payment Transactions
* Payment Status
* Stripe Integration
* Webhook Processing
* Refunds

Payments own their own state.

The Payment module may reference:

```text
OrderId
```

but does not directly own or modify Order state.

---

### Shipping

Responsibilities:

* Shipping Methods
* Shipping Rates
* Address Validation
* Tracking
* Shipping Status

---

### Reviews

Rules:

* Customer must have purchased the product.
* Reviews are allowed after Delivery.
* Reviews represent verified purchases.
* Reviews may require moderation.
* Admin can approve or reject reviews.

---

### Notifications

Notifications are event-driven.

Examples:

```text
OrderConfirmed
OrderShipped
OrderDelivered
PaymentFailed
ReservationExpired
```

Initial channel:

```text
Email
```

Future channels may include:

```text
SMS
Push Notifications
```

---

### Analytics

Admin analytics may include:

* Revenue
* Orders
* Best Selling Products
* Order Funnel
* Abandoned Cart Rate
* Low Stock Metrics
* Operational Metrics

Analytics should favor read models and asynchronous processing rather than adding expensive analytical queries to transactional workloads.

---

## Core Order Processing Workflow

The primary business workflow is:

```text
Checkout
    │
    ▼
Validate Cart
    │
    ├── Validate Products
    ├── Validate Prices
    ├── Validate Promotions
    ├── Validate Customer
    └── Validate Shipping Information
    │
    ▼
Create Order
    │
    ▼
Order = Pending
    │
    ▼
Reserve Inventory
    │
    ├── Failed
    │      │
    │      ▼
    │   Fail / Cancel Order
    │
    └── Success
           │
           ▼
    Create Payment Intent
           │
           ▼
     Customer Pays
           │
           ▼
    Stripe Webhook
           │
           ▼
    Payment Succeeded
           │
           ▼
     Confirm Order
           │
           ▼
       Fulfillment
           │
           ▼
        Shipping
           │
           ▼
       Delivered
```

The critical ordering rule is:

> Inventory Reservation happens before Payment processing.

This allows the system to compensate more safely when payment fails.

---

## Transactional Outbox

The Outbox Pattern is used for reliable event publication.

```text
Business Operation
       │
       ▼
Database Transaction
       │
       ├── Business State Change
       │
       └── Outbox Message
               │
               ▼
       Outbox Dispatcher
               │
               ▼
            RabbitMQ
```

The responsibilities are separated:

```text
Outbox
→ Reliable Event Publication

RabbitMQ
→ Asynchronous Message Delivery

Consumer
→ Idempotent Processing
```

---

## Messaging

RabbitMQ is the initial Message Broker.

Conceptually:

```text
Topic Exchange
    │
    ├── order.created
    ├── inventory.reserved
    ├── inventory.failed
    ├── payment.succeeded
    ├── payment.failed
    └── reservation.expired
```

Messaging must support:

* Retry
* Manual Acknowledgement
* Dead Letter Exchange
* Dead Letter Queue
* Idempotent Consumers
* Correlation IDs

---

## Saga Orchestration

The Order Processing Saga is conceptually owned by the Orders module.

```text
Order Saga
    │
    ▼
Reserve Inventory
    │
    ├── Success
    │      │
    │      ▼
    │   Initiate Payment
    │
    └── Failure
           │
           ▼
       Fail Order
```

Later:

```text
Reserve Inventory
        ↓
Payment
        ↓
Payment Failed
        ↓
Release Inventory
        ↓
Cancel Order
```

The Saga coordinates the workflow.

It does not directly modify the internal state of other modules.

```text
Orders
→ Order State

Inventory
→ Reservation State

Payments
→ Payment State
```

---

## Compensation and Failure Recovery

Payment failure:

```text
Inventory Reserved
        ↓
Payment Failed
        ↓
Release Inventory
        ↓
Cancel Order
```

Order confirmation failure after payment success:

```text
Payment Succeeded
        ↓
Order Confirmation Failed
        ↓
Retry with Exponential Backoff
        ↓
Retry Limit Reached
        ↓
Dead Letter / Manual Recovery
```

The system should not automatically refund immediately without an explicit recovery policy.

If compensation requires a refund:

```text
Payment Success
        ↓
Refund
        ↓
Release Inventory if applicable
        ↓
Cancel Order
```

The exact recovery policy must be explicitly defined and implemented as a business workflow.

---

## Reservation Expiration

Inventory owns Reservation expiration.

```text
HostedService
      │
      ▼
Find Expired Reservations
      │
      ▼
Release Reservation
      │
      ▼
Publish ReservationExpired
      │
      ▼
Order Saga
      │
      ▼
Cancel Order
```

The Inventory Worker must not directly modify Order state.

---

## Observability

The system is designed to support:

* Structured Logging
* Correlation IDs
* Trace IDs
* Distributed Tracing
* Metrics
* Health Checks

Technology direction:

```text
Serilog
+
OpenTelemetry
```

Example:

```text
Checkout Request
      │
      ├── Create Order
      │
      ├── Reserve Inventory
      │
      ├── Create Payment Intent
      │
      ├── Stripe Webhook
      │
      └── Confirm Order
```

The objective is to trace one business operation across synchronous and asynchronous boundaries.

---

## Implementation Roadmap

The project is implemented incrementally.

```text
Phase 0
Architecture Foundation

Phase 1
Identity & Access

Phase 2
Catalog

Phase 3
Cart

Phase 4
Orders

Phase 5
Inventory + Concurrency

Phase 6
Outbox + RabbitMQ

Phase 7
Idempotent Consumers

Phase 8
Order/Inventory Saga

Phase 9
Stripe Payments

Phase 10
Payment Saga + Compensation

Phase 11
Reservation Expiry

Phase 12
Failure Testing

Phase 13
Search

Phase 14
Promotions & Coupons

Phase 15
Wishlist

Phase 16
Reviews

Phase 17
Notifications

Phase 18
Shipping

Phase 19
Analytics

Phase 20
Production Hardening
```

The current implementation phase determines what may be implemented.

Future phases must not be implemented prematurely.

---

## Phase 0 Scope

Phase 0 establishes the technical foundation.

It includes:

* Solution Structure
* Modular Boundaries
* ASP.NET Core API
* Entity Framework Core
* SQL Server
* Redis
* RabbitMQ
* Docker Compose
* Result Pattern
* Domain Event Contracts
* Integration Event Contracts
* Outbox Contracts
* Idempotency Contracts
* Correlation ID
* Serilog
* OpenTelemetry Base Configuration

Phase 0 does not implement:

* Catalog CRUD
* Cart
* Checkout
* Payment
* Saga
* Inventory Reservation

Those belong to later phases.

---

## Recommended Module Structure

```text
src/
│
├── Api/
│
├── Modules/
│   │
│   ├── Identity/
│   │   ├── Domain/
│   │   ├── Application/
│   │   ├── Infrastructure/
│   │   └── Contracts/
│   │
│   ├── Catalog/
│   │   ├── Domain/
│   │   ├── Application/
│   │   ├── Infrastructure/
│   │   └── Contracts/
│   │
│   ├── Cart/
│   │   ├── Domain/
│   │   ├── Application/
│   │   ├── Infrastructure/
│   │   └── Contracts/
│   │
│   ├── Orders/
│   │   ├── Domain/
│   │   ├── Application/
│   │   ├── Infrastructure/
│   │   └── Contracts/
│   │
│   ├── Inventory/
│   │   ├── Domain/
│   │   ├── Application/
│   │   ├── Infrastructure/
│   │   └── Contracts/
│   │
│   └── Payments/
│       ├── Domain/
│       ├── Application/
│       ├── Infrastructure/
│       └── Contracts/
│
└── BuildingBlocks/
    ├── Domain/
    ├── Application/
    ├── Infrastructure/
    ├── Messaging/
    ├── Outbox/
    ├── Idempotency/
    ├── Observability/
    └── Resilience/
```

Shared code should represent genuinely shared concepts.

Avoid creating abstractions only to eliminate a small amount of duplication.

Avoid unnecessary:

```text
GenericRepository<T>
GenericService<T>
GenericController<T>
```

---

## Database Strategy

The initial architecture uses one SQL Server instance with separate DbContexts per module.

Conceptually:

```text
SQL Server
    │
    ├── Catalog DbContext
    ├── Orders DbContext
    ├── Inventory DbContext
    ├── Payments DbContext
    └── ...
```

Prefer:

> One DbContext per module.

Modules must maintain strong persistence boundaries even when they initially share the same physical database.

Potential future extraction candidates include:

```text
Orders
Inventory
Payments
```

Future evolution:

```text
Modular Monolith
      │
      ▼
Extract Bounded Context
      │
      ▼
Independent Database
      │
      ▼
Independent Service
```

Extraction should only happen when there is a real architectural reason.

---

## Testing Strategy

The project must eventually test failure scenarios explicitly.

### Messaging

* RabbitMQ Unavailable
* Consumer Crash
* Message Redelivery
* Duplicate Message
* Poison Message
* Retry
* Dead Letter Queue

### Payments

* Payment Failure
* Payment Timeout
* Duplicate Webhook
* Invalid Webhook Signature
* Duplicate Payment Intent

### Inventory

* Concurrent Reservations
* Insufficient Stock
* Reservation Expiration
* Reservation Release

### Database

* Database Failure
* Transaction Failure
* Outbox Failure

### Distributed Workflow

* Payment Success + Order Confirmation Failure
* Inventory Reserved + Payment Failure
* Reservation Expired + Order Cancellation

---

## Core MVP Definition of Done

The Core MVP is complete after Phase 12.

The end-to-end workflow must support:

```text
Customer
   ↓
Login
   ↓
Browse Catalog
   ↓
Add Product to Cart
   ↓
Checkout
   ↓
Recalculate Price
   ↓
Create Order
   ↓
Reserve Inventory
   ↓
Create Payment Intent
   ↓
Customer Pays
   ↓
Stripe Webhook
   ↓
Payment Succeeded
   ↓
Order Confirmed
```

Failure scenarios must include:

```text
Inventory Failure
    → Order Failed
```

```text
Payment Failure
    → Release Inventory
    → Cancel Order
```

```text
Reservation Expired
    → Release Inventory
    → ReservationExpired Event
    → Order Cancellation
```

```text
Duplicate Message
    → Idempotency Check
    → No Duplicate Business Operation
```

```text
Consumer Crash
    → Message Redelivery
    → Safe Reprocessing
```

```text
Order Confirmation Failure
    → Retry
    → DLQ / Recovery Policy
```

---

## Project Success Criteria

The project succeeds when it demonstrates more than a collection of APIs.

It should demonstrate:

```text
Business Domain Modeling
        +
Modular Architecture
        +
Reliable Order Processing
        +
Inventory Concurrency
        +
Asynchronous Messaging
        +
Transactional Outbox
        +
Idempotency
        +
Saga Orchestration
        +
Compensation
        +
Failure Recovery
        +
Observability
```

The system should be explainable through real implementation and tests when discussing questions such as:

* Why Modular Monolith instead of Microservices?
* Why is Inventory reserved before Payment?
* How is overselling prevented?
* What happens if Payment succeeds but Order confirmation fails?
* What happens if RabbitMQ delivers the same message twice?
* How are duplicate Stripe Webhooks handled?
* Why is the Outbox Pattern required?
* What happens if a Consumer crashes after processing but before ACK?
* How does the Saga coordinate multiple modules?
* Who owns Order State?
* Who owns Reservation State?
* How could Inventory be extracted into a Microservice later?
* How is one Order traced across asynchronous operations?

---

## Development Philosophy

The project follows this progression:

```text
Simple Correct Architecture
        ↓
Strong Module Boundaries
        ↓
Reliable Business Workflow
        ↓
Concurrency
        ↓
Asynchronous Processing
        ↓
Failure Handling
        ↓
Observability
        ↓
Production Hardening
```

Do not add complexity before the business problem requires it.

The core architectural principle is:

> Build a strong Modular Monolith first, then introduce distributed-systems patterns only where they solve real reliability and scalability problems.

The final system should remain understandable, testable, maintainable, and capable of evolving into independently deployable services when justified.

---

## Development Guidelines

When implementing a new phase:

1. Read the project context and current architecture.
2. Identify the current implementation phase.
3. Inspect the existing codebase.
4. Identify existing conventions.
5. Do not rewrite unrelated modules.
6. Define the domain model.
7. Define module boundaries and dependencies.
8. Implement the Domain layer.
9. Implement the Application layer.
10. Implement Infrastructure and Persistence.
11. Implement API endpoints.
12. Integrate with existing modules only through approved contracts.
13. Add validation and error handling.
14. Add tests.
15. Verify architectural boundaries.
16. Verify failure and concurrency behavior where applicable.
17. Document important architectural decisions.

The current implementation phase determines what is allowed to be implemented.

---

## Documentation

Project documentation is maintained under:

```text
docs/
├── PROJECT_CONTEXT.md
├── ARCHITECTURE.md
├── IMPLEMENTATION_ROADMAP.md
├── DOMAIN_MODEL.md
├── MESSAGING.md
├── RELIABILITY.md
└── ADR/
    ├── 001-modular-monolith.md
    ├── 002-database-boundaries.md
    ├── 003-messaging.md
    └── 004-order-workflow.md
```

---

## License

This project is intended as a portfolio and educational software engineering project.
