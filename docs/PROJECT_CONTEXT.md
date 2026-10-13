# Enterprise Commerce & Order Management Platform

## Master Project Specification & AI Implementation Guide

> **Document Purpose**
>
> This document is the authoritative project context for AI agents and developers.
> Read and understand it before implementing, modifying, or refactoring any part of the system.
> Follow the architectural boundaries, domain ownership rules, and reliability principles defined here.
> Implement incrementally — the current phase (see `IMPLEMENTATION_PLAN.md`) determines what may be built.
> Do not implement future phases prematurely unless explicitly requested.

---

# 1. Project Overview

An **Enterprise Commerce & Order Management Platform**: a production-oriented,
real-world-style e-commerce system. Not a CRUD store.

It demonstrates reliable handling of orders, inventory, payments, cart, catalog,
messaging, background processing, partial failures, eventual consistency, idempotency,
reliable publication, failure recovery, and observability — and the engineering behind them:
modular architecture, DDD, outbox, idempotent consumers, sagas, compensation, concurrency
control, retries, dead-letter queues, caching, and tracing.

The centerpiece is the **Reliable Order Processing Workflow**: the system must stay correct
when operations fail, messages duplicate, consumers crash, payment providers time out, or
async steps are temporarily unavailable.

---

# 2. Core Business Objective

Customer lifecycle: browse → cart → checkout → validate → reprice → create order →
reserve inventory → initiate payment → pay → webhook → confirm → fulfill → ship → deliver.

Failure handling is first-class:

- Payment failed → release inventory → cancel order.
- Confirmation failed after payment → bounded retry → DLQ / manual intervention (explicit policy).
- Consumer crash → redelivery → idempotency check → skip duplicate.

Correctness, consistency boundaries, reliability, and recoverability outrank premature optimization.

---

# 3. Architectural Vision

> **Modular Monolith with Clean Architecture and DDD, event-driven where justified,
> extractable into services later if warranted. Never start as microservices.**

```
        Angular (Storefront / Admin)          ← future
                    \        /
              ASP.NET Core API Host
                         │
                 Modular Monolith
                         │
    ┌────────────────────┼────────────────────┐
    │                    │                    │
 Catalog / Cart       Orders              Inventory
                         │                 Payments
                         │
                   SQL Server (1 instance, N DbContexts)
                         │
           ┌─────────────┼──────────┐
        Redis         Outbox      RabbitMQ (topic: fluxcommerce.events)
                                    │
                           Background Workers → Stripe
```

Introduced progressively per `IMPLEMENTATION_PLAN.md` — never all at once.

---

# 4. Architectural Principles (mandatory)

## 4.1 Modular Monolith First

One deployable application. Each module owns its domain logic, use cases, persistence,
and business state, and exposes explicit contracts. Never access another module's internals.

## 4.2 Module Ownership

Orders → order + saga progress. Inventory → stock + reservations. Payments → payments +
webhook records. The Saga coordinates; it owns no module state.

## 4.3 No Cross-Module Direct Database Writes

State changes cross boundaries only via commands, application contracts, or integration events.

## 4.4 Eventual Consistency

No giant distributed transaction across Order/Inventory/Payment/Stripe. Use integration
events, Outbox, broker, sagas, and compensating actions instead.

## 4.5 At-Least-Once Delivery

Exactly-once is never assumed. Every state-changing consumer is idempotent.

## 4.6 Idempotency at Every Level

API (`Idempotency-Key` on checkout), message (`EventId + HandlerName`), webhook
(`StripeEventId`), payment intent (deterministic `intent_{OrderId}_{PaymentId}_{Version}`).

---

# 5. Modules

| Module | Owns | Notes |
|---|---|---|
| Identity (platform) | Users, roles, permissions, tokens | JWT (15 min) + rotating refresh (7 days), `Customer/Admin/SuperAdmin`, 9 action-level permissions granted per-admin by SuperAdmin, email verify, lockout, config-driven seeder |
| Catalog | Products, categories, variants, SKU, attributes, images, specs | SQL filter/sort/page, soft delete, `decimal(18,6)`; no search engine in MVP |
| Cart | Guest + authenticated carts, merge | Redis primary, opaque `CartId`, Guest→Auth merge, snapshots revalidated at checkout |
| Orders | Order, lines, money, saga orchestration | Immutable snapshot, `(UserId, Idempotency-Key)` unique checkout, `Pending → Confirmed/Cancelled` |
| Inventory | Stock, reservations, expiry | Atomic conditional reserve (oversell-proof), expiration worker, `Available = OnHand − Reserved` |
| Payments | Payments, webhook records, refunds | Stripe isolated to `Payments.Infrastructure`; `ProcessedWebhookEvents` dedup; Outbox `payment_succeeded/failed` |
| BuildingBlocks | Cross-cutting primitives only | Domain, Application, Messaging (contract-only), Outbox, Idempotency, Persistence, Infrastructure, Observability, Localization, Web |

Deferred until the core workflow is green: Pricing (as a module), Promotions, Wishlist,
Reviews, Shipping, Notifications, Analytics, Search (see `IMPLEMENTATION_PLAN.md` phases 13–19).

## 5.1 Identity Is a Platform Capability

Commerce modules depend only on `ICurrentUser` / UserId / roles / permission claims —
never on Identity internals or its DbContext.

## 5.2 Catalog

SQL querying only (filter/sort/page). No Elasticsearch in MVP. Catalog answers *sellable*;
Inventory answers *reservable*.

## 5.3 Cart

Redis is primary (no SQL schema). Guest identity is opaque; authenticated carts resolve from
`ICurrentUser` (one active cart per user). Merge is Guest → Authenticated, idempotent and
atomic (target persist + source consume). Cart prices are temporary snapshots — Checkout
revalidates against current Catalog state and reprices authoritatively.

## 5.4 Orders

The order stores an immutable commercial snapshot (`ProductId/VariantId/SKU/Name/UnitPrice/Qty`)
and never depends on live Catalog data after creation. Money: single-currency, decimal-only,
`decimal(18,6)`. Lifecycle `Pending → Confirmed/Cancelled`; cancellation of a `Pending` order
is customer-allowed; post-payment changes go through the saga. `Order.Version` is the optimistic
concurrency token. Reads are ownership-scoped (another customer's order reads as 404).

## 5.5 Inventory

`InventoryItem(SKU, OnHand/Reserved/Available)` + `Reservation(OrderId/SKU/Qty/Status/ExpiresAt)`.
Reservation succeeds for exactly one contender at `Available = 1` (atomic conditional update +
`Version` token). Expiry releases exactly once in Inventory, then notifies the saga via Outbox —
the saga never releases on the expiry path (no double release).

## 5.6 Payments

Payment state is independent (`Pending → Processing → Succeeded/Failed/Cancelled`, plus
`Refunded` via compensation). `Stripe.net` is referenced only by `Payments.Infrastructure`
(enforced by arch tests). Intent creation is deterministic and single per order; webhook
delivery is deduplicated (`AlreadyProcessed` + 200 stops Stripe retries); amount mismatches are
recorded without transition and without outbox.

---

# 6. Core Order Processing Workflow

Checkout → validate cart → create order (`Pending`) → reserve inventory →
`[failed → fail/cancel]` → create payment intent → customer pays → Stripe webhook →
payment succeeded → confirm order → fulfillment → shipping → delivered.

**Ordering rule: inventory is reserved before payment is processed.** Compensation then needs
only release + cancel (+ refund if money moved), never un-takeable inventory holds.

---

# 7. Transactional Outbox

```
Business Operation → DB Transaction (State + Outbox) → Outbox Dispatcher → RabbitMQ
```

The Outbox is not the broker: it guarantees publication; the broker delivers; the consumer
processes idempotently. Dispatcher claims atomically (`UPDLOCK, READPAST`), backs off
exponentially, recovers stale claims, and parks poison as terminal `Failed`. No distributed
transactions, one module per transaction.

---

# 8. Messaging

RabbitMQ durable topic exchange, event `Type` as routing key. Manual ACK (only after
Business + ProcessedMessage commit), bounded in-process retry (Fixed/Linear/Exponential,
`x-death`-seeded attempts), DLX/DLQ with enriched diagnostic headers, correlation/causation
on every hop. Unknown event types are skipped, never poison.

---

# 9. Saga Orchestration

Owned by Orders, persisted as progress-only state (`Version`-guarded), coordinated exclusively
through integration events:

`WaitingForInventory → WaitingForPayment → Completed`, with `Failed`, `Cancelled`,
`ReleasingInventory`, `Refunding` for failure/compensation paths. Duplicate, conflicting, and
out-of-order results resolve deterministically (at most one business effect); terminal states
are protected; crash recovery resumes through idempotency, never restarts blindly.

---

# 10. Payment Failure and Compensation

- Payment failed → `ReleasingInventory` → `inventory.release_requested` → release → cancel.
- Post-payment downstream failure → `Refunding` (Stripe refund) → release → cancel.
- Confirmation failure after payment success → bounded retry → DLQ / explicit manual policy.
  Never auto-refund without a defined business recovery policy.

---

# 11. Reservation Expiration

Inventory's worker expires and releases exactly once, publishes
`InventoryReservationExpired` via Outbox, and the Orders consumer moves
`WaitingForPayment → Cancelled` (the only valid expiry transition; everything else is a
benign idempotent no-op). The worker never touches Order state.

---

# 12. Observability

Serilog + OpenTelemetry (`ActivitySource` name `FluxCommerce`, single source of truth in
`BuildingBlocks.Observability`), W3C trace context across Outbox → broker → consumer,
structured logs with `CorrelationId/CausationId/EventId/Handler/Attempt/Result` (never
business payloads or secrets), health checks (`/health`, `/health/live`).

---

# 13. Implementation Phases

Authoritative phase contents, sub-phases, and verification history:
**`docs/IMPLEMENTATION_PLAN.md`**.

Order: 0 Foundation → 1 Identity (+1.5 Runtime, +1.6 i18n) → 2 Catalog → 3 Cart →
4 Orders → 5 Inventory → 6 Outbox/RabbitMQ → 7 Consumers → 8 Saga → 9 Stripe →
10 Payment Saga/Compensation → 11 Expiry → 12 Failure Testing (Core MVP complete) →
13–19 commerce extensions → 20 Hardening → 21 Permissions → 22 Dashboard/delegation →
23 Dashboard data. Refactoring cycles R1/R2/R3 harden without behavior change.

Current state: Core MVP + Phases 21/22 + R1/R2/R3 complete; Phase 23 planned.
Final verification: build 0W/0E · arch 187/187 · unit 2412/2412 · integration 558/561
(3 pre-existing email-link environment failures, reproduced on the clean tree).

---

# 14. Failure Scenarios That Must Stay Covered

Messaging (outage, crash, redelivery, duplicates, poison, DLQ, retry) · Payments (failure,
timeout, duplicate webhook/signature/intent) · Inventory (concurrent reserves, insufficient
stock, expiry, release races) · Database/transaction/outbox faults · Distributed workflows
(success + confirmation failure; reserved + payment failure; expiry + cancellation).
Proven in Phase 12 and locked as regression tests — keep them green.

---

# 15. Engineering Rules for the AI Agent

1. **Implement incrementally** — the current phase in `IMPLEMENTATION_PLAN.md` is the scope.
2. **Respect module boundaries** — no foreign entities, DbContexts, repositories, or infra.
3. **Do not overengineer** — no microservices/Elasticsearch/Kafka/K8s/distributed DBs unless phased.
4. **No needless generic abstractions** (`GenericRepository<T>` etc.). Explicit domain logic.
5. **Preserve invariants** — rules live in Domain, never in controllers; no anemic models where invariants belong.
6. **No direct cross-module state mutation** — always via command/event/contract.
7. **Assume at-least-once** — every state-changing async consumer is idempotent.
8. **External integrations are unreliable** — design for timeout/retry/duplicate/partial/unknown.
9. **Keep ownership explicit** — Order→Orders, Reservation→Inventory, Payment→Payments.
10. **Prefer production-oriented design** — security, validation, concurrency, idempotency, observability, recovery, testability, maintainability.

Plus, from the R3 hardening experience:

11. **No wire-contract breakage in refactoring** — internal moves only unless a phase says otherwise.
12. **Static initialization order across partials is not guaranteed** — defer merged static state with `Lazy<T>`.
13. **One in-flight EF operation per DbContext** — parallelize across modules/contexts, never within one.
14. **Let the arch tests adjudicate placement** — a type carrying another module's contract cannot leave the carve-out layer.
15. **Prove moves byte-faithful** (when script-splitting files) and keep `0W/0E` + full suites green per wave.

---

# 16. Definition of Done for the Core MVP (Phase 12)

End-to-end: login → browse → cart → checkout → reprice → order → reserve → intent →
pay → webhook → succeeded → confirmed. And: inventory failure → order failed; payment
failure → release → cancel; expiry → release → event → cancel; duplicate → idempotent
no-op; crash → redelivery → safe reprocessing; confirmation failure → retry → DLQ/recovery.

---

# 17. Project Success Criteria

Demonstrably answers, through code and tests: why Modular Monolith; why reserve before pay;
how overselling is prevented; payment-success + confirmation-failure; duplicate delivery;
duplicate webhooks; why Outbox; crash-after-commit-before-ACK; saga coordination and state
ownership; extraction path for a module; end-to-end tracing of one order.

---

# 18. Final Philosophy

```
Simple Correct Architecture → Strong Module Boundaries → Reliable Business Workflow
→ Concurrency → Async → Failure Handling → Observability → Hardening
```

Build a strong Modular Monolith first; introduce distributed-systems patterns only where
they solve real reliability or scalability problems. Keep the system understandable,
testable, maintainable, and extractable when justified.

---

# 19. AI Agent Execution Protocol

1. Read this spec + `ARCHITECTURE.md`, identify the current phase in `IMPLEMENTATION_PLAN.md`.
2. Inspect the codebase and its conventions; do not rewrite unrelated modules.
3. Define the domain model, boundaries, and dependencies for the requested feature.
4. Implement Domain → Application → Infrastructure/Persistence → API, integrating only
   through approved contracts, with validation, errors, localization, and tests.
5. Verify boundaries (arch tests), concurrency/failure behavior where applicable, and
   record durable decisions in `docs/adr/`.

---

# 20. Documentation Map

```
docs/
├── PROJECT_CONTEXT.md      (this file — master spec)
├── ARCHITECTURE.md         (architecture specification)
├── IMPLEMENTATION_PLAN.md  (all phases + sub-phases + refactoring cycles)
├── adr/                    (17 decision records: monolith → delegation)
├── current/                (LOCAL_DEVELOPMENT.md, EF_CORE_COMMANDS.md)
└── frontend-contract/      (API_CONTRACT, AUTH_FLOW, DTO_REFERENCE, ENUM_REFERENCE,
                             ERROR_CATALOG, PAGINATION_CONVENTION, LOCALIZATION_CONTRACT,
                             PAYMENT_INTEGRATION_NOTES — frontend source of truth)
```
