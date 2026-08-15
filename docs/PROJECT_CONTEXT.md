# Enterprise Commerce & Order Management Platform

## Master Project Specification & AI Implementation Guide

> **Document Purpose**
>
> This document is the authoritative project context and implementation guide for AI agents and developers working on the Enterprise Commerce & Order Management Platform.
>
> The AI agent must read and understand this document before implementing, modifying, or refactoring any part of the system.
>
> The agent must follow the architectural boundaries, implementation phases, domain ownership rules, and reliability principles defined here.
>
> The project must be implemented incrementally. Do not implement future phases prematurely unless explicitly requested.

---

# 1. Project Overview

The project is an **Enterprise Commerce & Order Management Platform** designed to simulate a real-world, production-oriented enterprise e-commerce system.

The goal is **not** to build a simple CRUD-based online store.

The primary goal is to demonstrate the ability to design and implement a reliable commerce platform that handles complex business workflows involving:

* Orders
* Inventory
* Payments
* Cart
* Pricing
* Product Catalog
* Messaging
* Background Processing
* Distributed Operations
* Partial Failures
* Eventual Consistency
* Idempotency
* Reliable Event Publishing
* Failure Recovery
* Observability

The project is intended to be a strong portfolio/CV project demonstrating practical knowledge of:

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

The most important part of the project is the **Reliable Order Processing Workflow**.

The system should demonstrate that it can safely process business operations even when individual operations fail, messages are duplicated, consumers crash, external payment systems timeout, or asynchronous operations are temporarily unavailable.

---

# 2. Core Business Objective

The platform manages the complete e-commerce lifecycle:

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
```

The system must also correctly handle failure scenarios.

Example:

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

Another example:

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
Retry
        │
        ▼
Permanent Failure
        │
        ▼
Dead Letter Queue / Manual Intervention
```

Another example:

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
Skip Duplicate Processing
```

The system must prioritize **correctness, consistency boundaries, reliability, and recoverability** over premature optimization.

---

# 3. Architectural Vision

The initial architecture is a:

> **Modular Monolith with Clean Architecture and DDD principles, designed to support Event-Driven Architecture and future extraction of selected bounded contexts into independent services if justified.**

The system should **not start as Microservices**.

Microservices should not be introduced merely to make the project appear more complex.

The architecture must first establish strong modular boundaries inside a single deployable application.

The target architecture is:

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

This architecture is introduced progressively.

Do not build the entire architecture in Phase 0.

---

# 4. Architectural Principles

The following principles are mandatory.

## 4.1 Modular Monolith First

The backend must initially be deployed as one application.

Modules must have explicit boundaries.

A module must own its own:

* Domain logic
* Application use cases
* Persistence logic
* Business state

Modules must communicate through well-defined contracts.

Avoid direct access to another module's internal implementation.

For example:

```text
Orders
    ❌ Must not directly access Inventory DbContext internals

Orders
    ✅ Communicates with Inventory through application contracts
       or integration events
```

---

## 4.2 Module Ownership

Each module owns its own business state.

Examples:

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

The Order Saga may coordinate these modules, but it must not own their internal state.

The Saga coordinates.

The modules own their state.

---

## 4.3 No Cross-Module Direct Database Writes

A module must never directly modify another module's database state.

Incorrect:

```text
Order Module
    │
    ▼
InventoryDbContext
    │
    ▼
Update Reservation
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
Update Inventory State
```

---

## 4.4 Eventual Consistency

The system accepts eventual consistency between modules where asynchronous processing is appropriate.

Do not attempt to create one giant database transaction across:

* Order
* Inventory
* Payment
* External Stripe

Instead, use:

* Integration Events
* Outbox
* Message Broker
* Saga Orchestration
* Compensating Actions

---

## 4.5 At-Least-Once Delivery

The messaging architecture assumes:

```text
At-Least-Once Delivery
```

Exactly-once delivery must not be assumed.

Therefore, every message consumer that performs business state changes must be designed to be idempotent.

---

## 4.6 Idempotency

Idempotency must exist at multiple levels.

### API Idempotency

Important operations such as Checkout and Payment creation should support idempotency where appropriate.

Example:

```text
POST /checkout

Idempotency-Key: ABC123
```

Repeated requests using the same key must not create duplicate Orders.

### Message Idempotency

Consumers must safely handle duplicate messages.

### Webhook Idempotency

Stripe Webhooks must be processed idempotently.

### Payment Intent Idempotency

The system must prevent creating multiple Payment Intents for the same Order when the operation is retried.

---

# 5. Main Modules

The planned modules are:

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

# 6. Module Responsibilities

## 6.1 Identity & Access

Responsibilities:

* Customer registration
* Admin registration/management
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

Identity is a **Platform Capability**.

Commerce modules should not directly depend on Identity implementation details.

They should depend on abstractions such as:

```text
CurrentUser
UserId
Roles
```

---

## 6.2 Catalog

Responsibilities:

* Products
* Categories
* Product Variants
* SKU
* Product Images
* Dynamic Attributes/Specifications
* Product lifecycle
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

Do not introduce Elasticsearch in the initial Catalog implementation.

---

## 6.3 Product Search

Search will initially use SQL-based querying.

Future architecture:

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
Elasticsearch
```

Elasticsearch is a later phase.

---

## 6.4 Cart

Responsibilities:

* Guest Cart
* Authenticated User Cart
* Add Item
* Update Quantity
* Remove Item
* Clear Cart
* Cart Merge after Login
* Cart Expiration

Redis is the primary storage for Cart state.

Flow:

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

Cart price is **not** the final source of truth for Orders.

At Checkout:

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

## 6.5 Pricing

Responsibilities:

* Base Price
* Tax
* Currency
* Discounts
* Price Calculation
* Price History

Pricing must be designed around a clear monetary model.

Prefer a Money concept:

```text
Money
├── Amount
└── Currency
```

Avoid duplicating inconsistent money logic across entities.

---

## 6.6 Promotions & Coupons

Supported concepts:

* Percentage Discount
* Fixed Discount
* Free Shipping
* Expiration
* Minimum Order Value
* Category Restrictions
* Product Restrictions
* Global Usage Limits
* Per-User Usage Limits
* Stacking Rules

This module should be introduced after the Core Order Processing workflow is stable.

---

## 6.7 Orders

Responsibilities:

* Checkout
* Order Creation
* Order Snapshot
* Order Lifecycle
* Order History
* Cancellation
* Customer Order Queries

The Order must store immutable business snapshots.

The Order must not rely on live Catalog data after creation.

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

The Order should preserve the commercial state at the time of purchase.

---

## 6.8 Inventory

Responsibilities:

* Stock
* Available Quantity
* Reserved Quantity
* Inventory Reservation
* Release
* Commit
* Expiration
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

Inventory must handle concurrency correctly.

Example:

```text
Available = 1

Customer A → Reserve 1
Customer B → Reserve 1
```

Only one reservation may succeed.

The system must prevent overselling.

Use appropriate concurrency techniques such as:

* Atomic Conditional Updates
* Optimistic Concurrency
* Transactional boundaries

The exact mechanism must be chosen based on the implemented persistence model.

---

## 6.9 Payments

Responsibilities:

* Payment Intent
* Payment Attempts
* Payment Transactions
* Payment Status
* Stripe Integration
* Webhook Processing
* Refunds

Payment must be independently modeled.

The Payment module may reference:

```text
OrderId
```

but should not directly own Order state.

---

## 6.10 Shipping

Responsibilities:

* Shipping Methods
* Shipping Rates
* Address Validation
* Tracking
* Shipping Status

Shipping is introduced after the Core Order Processing workflow.

---

## 6.11 Reviews

Rules:

* Customer must have purchased the product.
* Review should be allowed only after Delivery.
* Reviews may require moderation.
* Admin can approve/reject reviews.

---

## 6.12 Notifications

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

Potential future channels:

```text
SMS
Push Notifications
```

---

## 6.13 Analytics

Admin analytics include:

* Revenue
* Orders
* Best Selling Products
* Conversion Funnel
* Abandoned Cart Rate
* Low Stock
* Operational Metrics

Analytics should favor read models and asynchronous processing rather than adding expensive analytical queries to transactional workloads.

---

# 7. Core Order Processing Workflow

The primary workflow is:

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
    │   Fail/Cancel Order
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

> **Inventory Reservation must happen before Payment processing.**

This simplifies compensation.

Example:

```text
Inventory Reserved
        ↓
Payment Failed
        ↓
Release Inventory
        ↓
Cancel Order
```

---

# 8. Transactional Outbox

The Outbox is used for reliable event publication.

Correct architecture:

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

The Outbox is **not** the Message Broker.

Responsibilities:

```text
Outbox
→ Reliable Event Publication

RabbitMQ
→ Asynchronous Message Delivery

Consumer
→ Idempotent Processing
```

---

# 9. Messaging

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

Messages should contain sufficient metadata for reliable processing, including appropriate identifiers and correlation information.

Messaging must support:

* Retry
* Manual ACK
* Dead Letter Exchange
* Dead Letter Queue
* Idempotent Consumers
* Correlation IDs

---

# 10. Saga Orchestration

The Order Processing Saga is owned conceptually by the Orders module.

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

The Saga orchestrates.

It does not directly modify other modules' state.

Each module owns its state:

```text
Orders
→ Order State

Inventory
→ Reservation State

Payments
→ Payment State
```

Communication occurs through:

* Commands
* Integration Events
* Message Broker
* Outbox

depending on the specific workflow and boundary.

---

# 11. Payment Failure and Compensation

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
Dead Letter / Manual Intervention
```

Do not automatically refund immediately without defining an explicit business recovery policy.

If a permanent failure requires compensation:

```text
Payment Success
        ↓
Refund
        ↓
Release Inventory if applicable
        ↓
Cancel Order
```

The exact policy must be explicit and implemented as a business workflow.

---

# 12. Reservation Expiration

Inventory owns Reservation expiration.

Worker:

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

The Inventory Worker must **not directly modify Order state**.

---

# 13. Observability

The system must support:

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

Example trace:

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

The goal is to trace a business operation across asynchronous boundaries.

---

# 14. Implementation Phases

The implementation must follow this order.

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

Completed so far: **Phase 0** (Architecture Foundation — `docs/completed/PHASE_0_COMPLETION_REPORT.md`), **Phase 1** (Identity & Access — `docs/completed/PHASE_1_COMPLETION_REPORT.md`), plus the companion phases **Phase 1.5** (Runtime Preparation — `docs/completed/PHASE_1_5_COMPLETION_REPORT.md`) and **Phase 1.6** (Internationalization Foundation — `docs/completed/PHASE_1_COMPLETION_REPORT.md` §14, summarized in §14.1–§14.2 below), **Phase 2** (Catalog Module — `docs/completed/PHASE_2_COMPLETION_REPORT.md`), **Phase 3** (Cart Module — `docs/completed/PHASE_3_COMPLETION_REPORT.md`), **Phase 4** (Orders & Checkout — `docs/completed/PHASE_4_COMPLETION_REPORT.md`), and **Phase 5** (Inventory + Concurrency — `docs/completed/PHASE_5_COMPLETION_REPORT.md`), and **Phase 6** (Outbox + RabbitMQ — `docs/completed/PHASE_6_COMPLETION_REPORT.md`, summarized in §14.5 below).

### 14.1 Phase 1.6 — Internationalization (i18n) Foundation — Implemented & Verified

The core MVP roadmap is extended by the Internationalization Foundation (Phase 1.6), completed as subphases 1.6.1–1.6.10.

**Scope.** Shared cross-cutting localization infrastructure used by every module — not multilingual business data (Products, Categories, Brands, etc.). Business data remains single-language; only user-facing strings (validation messages, ProblemDetails, identity/auth errors, transactional emails, API docs) are localized.

**Subphases completed.**

| SubPhase | Scope | Status |
|----------|-------|--------|
| 1.6.1 | Localization architecture (culture registry, resolution strategy, resource organization) | Implemented |
| 1.6.2 | ASP.NET Core localization infrastructure (middleware, options, services) | Implemented |
| 1.6.3 | Shared resource system (neutral + `fr`/`de`/`es`/`ar` satellites) | Implemented |
| 1.6.4 | Localization abstractions (`ILocalizer`, `ICurrentCulture`) | Implemented |
| 1.6.5 | Validation localization | Implemented |
| 1.6.6 | ProblemDetails localization | Implemented |
| 1.6.7 | Identity localization | Implemented |
| 1.6.8 | Email localization | Implemented |
| 1.6.9 | API documentation localization support | Implemented |
| 1.6.10 | Final verification & phase closure (this entry) | Implemented |

**Key decisions (localization architecture).**

- Abstractions-only rule: Domain/Application layers depend only on `ILocalizer`/`ICurrentCulture` (BuildingBlocks.Localization), never on ASP.NET Core localization types.
- Single culture registry `LocalizationConfiguration.SupportedCultures` (`en`, `fr`, `de`, `es`, `ar`, default `en`); no `ResourcesPath` (resources resolved by type namespace).
- Single shared resource repository (`SharedResources` + satellites, 75 keys each, full parity).
- Stable `Error.Code` unchanged; `IErrorCodeLocalizer` maps code → resource key only at the presentation boundary (API host); status codes and ProblemDetails shape unchanged.

**Source of truth.** The full i18n subphase breakdown, decisions, deliverables and verification results are recorded in `docs/completed/PHASE_1_COMPLETION_REPORT.md` (§14).

**Verification.** Full solution builds with 0 warnings/0 errors; Architecture tests 12/12, Unit tests 183/183, Integration tests 42/42.

**Deferred.** Storage-backed per-user `PreferredLanguage` provider — future subphase; current culture resolution is request-scoped (query, cookie, `Accept-Language`).

### 14.2 Phase 1.5 — Runtime Preparation & First Startup — Implemented & Verified

The runtime preparation phase (see `docs/phases/PHASE_1_5_RUNTIME_PREPARATION.md` and `docs/completed/PHASE_1_5_COMPLETION_REPORT.md`) transformed the solution into a runnable application:

- **Runtime configuration & hosting** — `Program.cs`, per-module connection strings, JWT/Email/Cache/broker settings in `appsettings.Development.json`, `http`/`https` launch profiles.
- **Swagger / OpenAPI** — Swagger UI + OpenAPI document (`v1`), XML docs, JWT bearer security definition/requirement for in-Swagger authorization.
- **Database initialization** — design-time DbContext factories for all module persistence projects; Identity migrations `InitialIdentity`, `EnrichUserProfile`, `AdjustDisplayNameLength` (3/3 applied).
- **Docker environment** — `docker-compose.yml` (SQL Server, Redis, RabbitMQ, smtp4dev) with health checks.
- **Verification** — first end-to-end runtime verification of Identity (register → verify → login → authorize → refresh → logout), health endpoints operational.

**Deferred per scope** — API versioning, rate limiting, refresh-token hashing, CI/CD, production secret management, OAuth/external login, MFA, passkeys (SMTP email provider was delivered within Phase 1 instead).

---

### 14.3 Phase 3 — Cart Module — Implemented & Verified

The Cart Module (Phase 3) delivers the shopping cart domain — the first customer-facing commerce domain. It introduces the **Cart** aggregate with **CartItem** entities, supporting both **Guest** and **Authenticated** carts, **Redis-backed persistence**, **Catalog contract integration** for variant validation and snapshots, **Guest → Authenticated merge**, a complete **REST API**, full **localization**, and a green test suite.

**Subphases completed.**

| SubPhase | Scope | Status |
|----------|-------|--------|
| 3.1 | Cart Module Foundation — Redis setup, module structure, DI, ICartStore, architecture tests | Implemented |
| 3.2 | Cart Domain Model — Cart aggregate, CartItem, ownership, quantity rules, Add/Update/Remove/Clear/Merge, domain errors | Implemented |
| 3.3 | Redis Cart Persistence — ICartStore, RedisCartStore, serialization, key naming, TTL, CAS, merge-and-consume script | Implemented |
| 3.4 | Guest Cart — Opaque CartId, create/get/add/update/remove/clear/expiration, ownership protection | Implemented |
| 3.5 | Authenticated Cart — ICurrentUser integration, UserId ownership, one active cart per user | Implemented |
| 3.6 | Cart Merge — Guest → Authenticated, duplicate variant handling, quantity aggregation, guest consumption, idempotency, concurrency | Implemented |
| 3.7 | Catalog Contract Integration — ICatalogVariantCartLookup, variant eligibility, price/snapshot data | Implemented |
| 3.8 | Cart Application Use Cases — 11 commands, 2 queries, 13 handlers, ownership, Domain/Catalog/Store coordination | Implemented |
| 3.9 | Cart API — CartController, request/response DTOs, guest/auth endpoints, ProblemDetails, Swagger | Implemented |
| 3.10 | Localization & Error Handling — 16 Cart keys, 12 error codes, ErrorCodeLocalizer, Arabic/English parity | Implemented |
| 3.11 | Testing & Hardening — 722 Cart tests (509 unit + 178 integration + 35 architecture) | Implemented |
| 3.12 | Documentation & Closure — PROJECT_CONTEXT sync, Architecture review, Swagger review, Phase 4 readiness | Implemented |

**Key decisions (Cart architecture).**

- Redis is the primary Cart persistence — no SQL schema/EF migrations for Cart state.
- Cart owns temporary shopping state; Catalog owns product state; Orders will own order state.
- Guest Cart identity is an opaque, non-sequential CartId; Authenticated Cart uses ICurrentUser.
- Cart Merge is Guest → Authenticated only; merge is idempotent, atomic (target persist + source consume), and concurrency-safe.
- Catalog integration uses explicit contract (`ICatalogVariantCartLookup`) — zero CatalogDbContext dependency.
- Cart snapshots (ProductId, SKU, ProductName, UnitPrice, Currency) come from Catalog at add-time; Cart price is a temporary snapshot, not final order price.
- Optimistic concurrency via Cart version + Redis CAS; all mutations validate expected version.
- Single batch Catalog call per add/merge (no N+1); malformed Catalog results fail safely.

**Verification.** Full solution builds with 0 warnings/0 errors; Architecture tests 74/74, Unit tests 1,259/1,259, Integration tests 178/178; Cart-specific: 509 unit + 178 integration + 35 architecture = 722 green.

**Deferred.** Cart expiration background worker (Redis TTL handles it), Inventory reservation (Phase 5), Final pricing/checkout (Phase 4), RabbitMQ/Outbox events (Phase 6), Wishlist (future phase).

---

### 14.4 Phase 4 — Orders & Checkout — Implemented & Verified

The Orders & Checkout phase (see `docs/phases/PHASE_4_ORDERS_CHECKOUT.md` and `docs/completed/PHASE_4_COMPLETION_REPORT.md`) converts an authenticated customer's Cart into a durable, immutable commercial Order. It introduces the **Orders module** (Domain/Application/Infrastructure/Contracts) with the **Order aggregate**, **OrderLineItem**, **Money**, the **Pending → Cancelled** lifecycle, **authenticated Checkout** with persistent **SQL-backed idempotency**, **Catalog revalidation** and **current-price resolution**, immutable **Order snapshots**, **Cart consumption**, customer **Order queries** (list + details), **cancellation**, full **localization**, and a green test suite.

**Subphases completed.**

| SubPhase | Scope | Status |
|----------|-------|--------|
| 4.1 | Orders module foundation & architecture (skeleton review, `OrdersDbContext`, DI, config, migrations infrastructure, architecture boundaries) | Implemented |
| 4.2 | Order domain model & monetary model (Order aggregate, OrderLineItem, Money, totals, snapshots, address) | Implemented |
| 4.3 | Order lifecycle, cancellation & domain errors (OrderStatus, Pending/Cancelled, domain errors/events) | Implemented |
| 4.4 | Orders persistence & database schema (EF configurations, monetary precision, indexes, `CheckoutIdempotency`, `InitialOrders` migration) | Implemented |
| 4.5 | Cart & Catalog checkout contracts (`ICartCheckoutSnapshotProvider`, `ICatalogVariantCheckoutLookup`, `ICartCheckoutConsumption`) | Implemented |
| 4.6 | Checkout pricing & immutable Order snapshot (current-price resolution, line totals, subtotal, snapshot builder) | Implemented |
| 4.7 | Checkout application workflow & idempotency (Checkout command, ownership, empty-cart, persistent `(Customer, Key)` idempotency, request fingerprint, atomic persistence) | Implemented |
| 4.8 | Cart consumption & checkout failure semantics (best-effort post-commit clear, safe retry, no duplicate Orders on failure) | Implemented |
| 4.9 | Order queries & REST API (`GET /orders`, `GET /orders/{orderId}`, `POST /orders/{orderId}/cancel`, pagination, ownership, Swagger) | Implemented |
| 4.10 | Hardening, testing, documentation & completion (validation/authorization/localization/Swagger/security/performance/EF reviews, docs sync, completion report) | Implemented |

**Key decisions (Orders architecture).**

- Orders owns Order state exclusively; it never touches Cart Redis, Catalog DbContext, Inventory, Payments, RabbitMQ, or Outbox. Cross-module interaction uses only the approved checkout contracts (`Cart.Contracts`, `Catalog.Contracts`).
- Checkout is authenticated-only and idempotent: the `Idempotency-Key` header participates in a unique `(UserId, IdempotencyKey)` constraint; the same request+key returns the original Order, a different request reusing the key is rejected with a conflict, and Order + idempotency record persist atomically in one `SaveChanges`.
- The Cart price snapshot is never authoritative: Checkout revalidates variants against current Catalog state (batch — no per-item calls) and builds the Order from the revalidated price, SKU, product name, and eligibility.
- The Order stores an immutable commercial snapshot (ProductId, VariantId, SKU, ProductName, UnitPrice, Quantity) and does not depend on live Catalog data after creation.
- The Order aggregate enforces lifecycle rules (`Pending → Cancelled` only) and all monetary invariants (positive amounts, ISO 4217 currency, single-currency enforcement, deterministic arithmetic, `decimal(18,6)` SQL precision).
- Customer queries and cancellation are ownership-scoped by the authenticated `ICurrentUser`; another customer's Order is indistinguishable from a missing one (404), preventing IDOR.
- Order list reads use a lightweight projection (no owned line-item materialization); the page is ordered newest-first with a two-step approach because SQLite cannot `ORDER BY` a `DateTimeOffset` column.
- Cart clearing is a best-effort step after the Order is durably committed; its failure never rolls back or duplicates the Order.

**Verification.** Full solution builds with 0 warnings/0 errors; Architecture tests 103/103, Unit tests 1,497/1,497, Integration tests 207/207. EF model in sync with the `InitialOrders` migration (no pending model changes).

**Deferred.** Inventory reservation & concurrency (Phase 5), Outbox + RabbitMQ (Phase 6), idempotent consumers (Phase 7), Order/Inventory Saga (Phase 8), Stripe Payments (Phase 9), payment compensation & refunds (Phase 10), reservation expiry (Phase 11). Order cancellation currently performs no inventory/payment compensation by design. `CheckoutResponse` is deliberately minimal; the full Order representation is served by the Phase 4.9 queries.

### 14.5 Phase 6 — Outbox + RabbitMQ — Implemented & Verified

The Outbox + RabbitMQ phase (see `docs/phases/PHASE_6_OUTBOX_RABBITMQ.md` and `docs/completed/PHASE_6_COMPLETION_REPORT.md`) establishes the reliable asynchronous messaging foundation: a module persists **Business State Change + Outbox Message** in the **same database transaction** and an Outbox Dispatcher publishes the resulting Integration Event to **RabbitMQ** asynchronously, under **At-Least-Once** delivery.

**Subphases completed.**

| SubPhase | Scope | Status |
|----------|-------|--------|
| 6.1 | Baseline & messaging architecture audit (doc intentionally removed) | Implemented |
| 6.2 | Outbox persistence model (doc intentionally removed) | Implemented |
| 6.3 | Domain Event → Outbox integration (doc intentionally removed) | Implemented |
| 6.4 | Integration Event contracts & versioning (ADR-007) | Implemented |
| 6.5 | Outbox store & atomic claiming semantics | Implemented |
| 6.6 | RabbitMQ publishing infrastructure (durable topic exchange, confirms) | Implemented |
| 6.7 | Outbox dispatcher background services (Orders + Inventory) | Implemented |
| 6.8 | Retry & failure recovery (exponential backoff, stale-claim recovery) | Implemented |
| 6.9 | Correlation/Causation & observability | Implemented |
| 6.10 | End-to-end integration tests (7 scenarios, real SQL + RabbitMQ) | Implemented |
| 6.11 | Architecture & security hardening | Implemented |
| 6.12 | Performance & operational hardening (publisher channel reuse) | Implemented |
| 6.13 | Documentation synchronization | Implemented |
| 6.14 | Final verification & completion report | Implemented |

**Key decisions (messaging architecture).**

- **Transactional Outbox, module-per-DbContext**: `OrdersDbContext` and `InventoryDbContext` each own their `OutboxMessages` table; business state + Outbox record commit atomically in one `SaveChanges`; no distributed or cross-module transaction.
- **Integration Events live in module Contracts** and derive from the contract-only `IntegrationEvent` base in `BuildingBlocks.Messaging` (ADR-007, G12) — no internal implementation details leak; Domain/Application never touch Messaging or the RabbitMQ client.
- **RabbitMQ** is a durable **topic** exchange with the event `Type` as the **routing key**; publishing uses **publisher confirms**; the publisher reuses one shared confirm-enabled channel (self-healing on closure, disposed on shutdown) to avoid per-message topology round trips.
- **Atomic claiming**: a single conditional UPDATE (CTE + `UPDLOCK, READPAST` + `OUTPUT`) transitions `Pending → InProgress`; concurrent workers claim disjoint batches; a message is only eligible once its exponential-backoff `RetryAfterUtc` has elapsed.
- **Retry/failure**: retryable failures return to `Pending` with backoff + error metadata; unknown/corrupt/exhausted messages go to the terminal `Failed` state; stale `InProgress` claims (post-crash) are recovered to `Pending`.
- **At-Least-Once**: a confirmed-but-never-marked publish is republished on recovery — duplicates are possible and consumer idempotency is explicitly Phase 7.

**Verification.** Full solution builds with 0 warnings/0 errors; Architecture tests 157/157, Unit tests 1796/1796, Integration tests 300/300 (54 Phase 6 messaging-specific) against real SQL Server + RabbitMQ, including E2E (success, broker nack + recovery, atomic rollback, stale-claim recovery, batch, duplicate boundary, cancellation). Orders and Inventory EF models are in sync with migrations (no pending changes). No Phase 7 consumers/Inbox/Saga were introduced.

**Deferred.** Idempotent consumers / Inbox / consumer idempotency (Phase 7), Order/Inventory Saga (Phase 8), Stripe Payments (Phase 9), compensation workflows (later phases).

---

# 15. Phase 0 — Detailed Foundation Scope

Phase 0 must establish only the technical foundation.

Required:

```text
Solution Structure
Modular Boundaries
ASP.NET Core API
EF Core
SQL Server
Redis
RabbitMQ
Docker Compose
Result Pattern
Domain Event Contracts
Integration Event Contracts
Outbox Contracts
Idempotency Contracts
Correlation ID
Serilog
OpenTelemetry Base Configuration
```

Do not implement full business functionality in Phase 0.

Do not implement:

```text
Catalog CRUD
Cart
Checkout
Payment
Saga
Inventory Reservation
```

Those belong to later phases.

---

# 16. Recommended Module Structure

A module should follow this conceptual structure:

```text
Modules
│
├── Catalog
│   ├── Domain
│   ├── Application
│   ├── Infrastructure
│   └── Contracts
│
├── Orders
│   ├── Domain
│   ├── Application
│   ├── Infrastructure
│   └── Contracts
│
├── Inventory
│   ├── Domain
│   ├── Application
│   ├── Infrastructure
│   └── Contracts
│
└── Payments
    ├── Domain
    ├── Application
    ├── Infrastructure
    └── Contracts
```

Shared technical capabilities should live in:

```text
BuildingBlocks
├── Domain
├── Application
├── Infrastructure
├── Messaging
├── Outbox
├── Idempotency
├── Observability
└── Resilience
```

Do not create shared abstractions simply to avoid duplicating a few lines of code.

Shared code must represent a genuinely shared concept.

---

# 17. Database Strategy

Initial architecture:

```text
One SQL Server Instance
        │
        ├── Catalog DbContext
        ├── Orders DbContext
        ├── Inventory DbContext
        ├── Payments DbContext
        └── ...
```

Prefer **Module-per-DbContext**.

Modules should have strong persistence boundaries even when they initially share the same physical SQL Server database.

The implementation should avoid direct cross-module DbContext access.

Future evolution:

```text
Modular Monolith
      │
      ▼
Extract High-Value Bounded Context
      │
      ▼
Independent Database
      │
      ▼
Microservice
```

Potential extraction candidates in the future:

```text
Orders
Inventory
Payments
```

Only extract a module when there is a real architectural reason.

---

# 18. Failure Scenarios That Must Eventually Be Tested

The project must explicitly test:

### Messaging

```text
RabbitMQ Unavailable
Consumer Crash
Message Redelivery
Duplicate Message
Poison Message
DLQ
Retry
```

### Payments

```text
Payment Failure
Payment Timeout
Duplicate Webhook
Invalid Webhook Signature
Duplicate Payment Intent
```

### Inventory

```text
Concurrent Reservations
Insufficient Stock
Reservation Expiration
Reservation Release
```

### Database

```text
Database Failure
Transaction Failure
Outbox Failure
```

### Distributed Workflow

```text
Payment Success
+
Order Confirmation Failure
```

```text
Inventory Reserved
+
Payment Failure
```

```text
Reservation Expired
+
Order Cancellation
```

The goal is to prove that the architecture handles partial failures correctly.

---

# 19. Engineering Rules for the AI Agent

The AI agent must follow these rules.

## Rule 1 — Implement Incrementally

Never implement multiple future phases at once unless explicitly requested.

---

## Rule 2 — Respect Module Boundaries

Do not directly access another module's internal:

* Entities
* DbContext
* Repositories
* Infrastructure

Use contracts and defined communication mechanisms.

---

## Rule 3 — Do Not Overengineer

Do not introduce:

* Microservices
* Elasticsearch
* Kafka
* Kubernetes
* Distributed Databases

unless explicitly required by the current phase.

The project is intentionally built as a Modular Monolith first.

---

## Rule 4 — Do Not Create Generic Abstractions Without Need

Avoid unnecessary:

```text
GenericRepository<T>
GenericService<T>
GenericController<T>
```

Business logic should remain explicit and domain-oriented.

---

## Rule 5 — Preserve Business Invariants

Business rules belong to the appropriate domain/module.

Do not move domain rules into Controllers.

Avoid anemic domain models where meaningful business invariants can be enforced in the Domain layer.

---

## Rule 6 — Do Not Use Direct Cross-Module State Mutation

Incorrect:

```text
Orders → Update Inventory Database
```

Correct:

```text
Orders
   ↓
Command/Event
   ↓
Inventory
   ↓
Inventory State Change
```

---

## Rule 7 — Assume At-Least-Once Messaging

Every asynchronous consumer that changes state must be idempotent.

---

## Rule 8 — External Integrations Are Unreliable

Treat Stripe and Message Broker operations as potentially failing.

Always consider:

```text
Timeout
Retry
Duplicate
Partial Success
Unknown Outcome
```

---

## Rule 9 — Keep Business State Ownership Explicit

```text
Order → Orders
Inventory → Inventory
Payment → Payments
Reservation → Inventory
```

---

## Rule 10 — Prefer Production-Oriented Design

Every implementation should consider:

* Security
* Validation
* Concurrency
* Idempotency
* Observability
* Failure Recovery
* Testability
* Maintainability
* Scalability

---

# 20. Definition of Done for the Core MVP

The Core MVP is considered complete after Phase 12.

The following workflow must work end-to-end:

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

And failure flows must work:

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

At this point, the project demonstrates the core engineering value intended by the architecture.

---

# 21. Project Success Criteria

The project is successful if it demonstrates more than a collection of APIs.

It must demonstrate:

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

The final system should be explainable in a technical interview through questions such as:

* Why Modular Monolith instead of Microservices?
* Why is Inventory reserved before Payment?
* How do you prevent overselling?
* What happens if Payment succeeds but Order confirmation fails?
* What happens if RabbitMQ publishes a message twice?
* How do you handle duplicate Stripe Webhooks?
* Why do you need Outbox?
* What happens if the Consumer crashes after processing but before ACK?
* How does the Saga coordinate multiple modules?
* Who owns Order State?
* Who owns Reservation State?
* How would you extract Inventory into a Microservice later?
* How do you trace one Order across asynchronous operations?

If the architecture and implementation can answer these questions clearly through actual code and tests, the project has achieved its primary goal.

---

# 22. Final Implementation Philosophy

The project must be built as a **real system**, not as a demonstration of how many technologies can be added.

The progression should be:

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

The most important architectural principle is:

> **Build a strong Modular Monolith first, then introduce distributed-systems patterns only where they solve real reliability and scalability problems.**

The final project should remain understandable, testable, maintainable, and capable of evolving into independently deployable services when justified.

---

## Recommended AI Agent Execution Protocol

When asked to implement a new phase, the AI agent should always follow this sequence:

```text
1. Read this Master Project Specification.
        ↓
2. Identify the current implementation phase.
        ↓
3. Inspect the existing codebase.
        ↓
4. Identify existing architecture and conventions.
        ↓
5. Do not rewrite unrelated modules.
        ↓
6. Define the domain model for the requested feature.
        ↓
7. Define module boundaries and dependencies.
        ↓
8. Implement Domain layer.
        ↓
9. Implement Application layer.
        ↓
10. Implement Infrastructure/Persistence.
        ↓
11. Implement API endpoints.
        ↓
12. Add integration with existing modules only through approved contracts.
        ↓
13. Add validation and error handling.
        ↓
14. Add tests.
        ↓
15. Verify architectural boundaries.
        ↓
16. Verify failure and concurrency behavior where applicable.
        ↓
17. Document important architectural decisions.
```

**The AI agent must never assume that a feature should be implemented immediately just because it appears in the overall project scope. The current Phase determines what is allowed to be implemented.**

---

```text
/docs
    PROJECT_CONTEXT.md          (this document — master specification)
    ARCHITECTURE.md             (architecture specification)
    adr/
        001-modular-monolith.md
        002-database-boundaries.md
        003-building-blocks.md
        004-messaging-reliability.md
        005-observability.md
    phases/
        PHASE_0_ARCHITECTURE_FOUNDATION.md
        PHASE_1_IDENTITY_ACCESS.md
        PHASE_1_5_RUNTIME_PREPARATION.md
        PHASE_2_CATALOG_MODULE.md
        PHASE_3_CART_MODULE.md
        PHASE_4_ORDERS_CHECKOUT.md
        PHASE_5_INVENTORY_CONCURRENCY.md
    completed/
        PHASE_0_COMPLETION_REPORT.md
        PHASE_1_COMPLETION_REPORT.md
        PHASE_1_5_COMPLETION_REPORT.md
        PHASE_2_COMPLETION_REPORT.md
        PHASE_3_COMPLETION_REPORT.md
        PHASE_4_COMPLETION_REPORT.md
        PHASE_5_COMPLETION_REPORT.md
    current/
        EF_CORE_COMMANDS.md
        LOCAL_DEVELOPMENT.md
```
