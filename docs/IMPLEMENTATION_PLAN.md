# FluxCommerce — Implementation Plan

The single consolidated record of every implementation phase and refactoring cycle.
Per-phase plan files (`docs/phases/`), completion reports (`docs/completed/`) and
refactoring workbooks (`docs/refactoring/`) were merged here on 2026-10-13 and removed.

Status legend: ✅ Done (verified) · 🔄 Planned · ⏳ Deferred (post-MVP).

Normative architecture and module rules live in `ARCHITECTURE.md`; the master
specification and AI-agent protocol live in `PROJECT_CONTEXT.md`.

---

## Phase 0 — Architecture Foundation ✅

Verified 2026-07-31. Skeleton only — no business features.

| Sub-phase | Scope |
|---|---|
| 0.1 | Architecture decisions: Modular Monolith, Clean/DDD, ownership, messaging, reliability, observability (+ ADRs) |
| 0.2 | Solution structure: `src/` per-module Domain/Application/Infrastructure/Contracts, no shared giant domain |
| 0.3 | Module boundaries: Catalog/Cart/Orders/Inventory/Payments; Identity = platform capability; forbidden dependencies |
| 0.4 | Domain/Application BuildingBlocks: Entity, AggregateRoot, ValueObject, DomainEvent, Result/Error, Command/Query |
| 0.5 | Persistence foundation: EF Core + SQL Server, module-per-DbContext, no cross-module writes |
| 0.6 | API foundation: global exceptions, RFC 7807 ProblemDetails, validation, Correlation-ID, logging, health |
| 0.7 | Infrastructure connectivity: config/DI/health for SQL Server, Redis, RabbitMQ (no business use yet) |
| 0.8 | Messaging contracts: IntegrationEvents with EventId/Version/Correlation/Causation/OccurredAt, at-least-once |
| 0.9 | Reliability foundations: Outbox + Idempotency contracts (Business + Outbox in one transaction) |
| 0.10 | Observability readiness: structured logs, Correlation/Trace IDs, OpenTelemetry wiring |
| 0.11 | Docker dev environment: Compose for SQL Server, Redis, RabbitMQ; externalized secrets |
| 0.12 | Architecture verification: automated arch tests (Domain↛Infra/API, no cross-DbContext, BuildingBlocks↛modules) |

Verification: build 0W/0E · arch 6/6 · Docker SQL 2022 / Redis 8 / RabbitMQ 4 live.
Decisions: Modular Monolith first (never microservices now); strictly no business code in Phase 0.

---

## Phase 1 — Identity & Access ✅

Auth verified end-to-end (register → verify → login → refresh → logout).

| Sub-phase | Scope |
|---|---|
| 1.1 | Module foundation: Identity Domain/Application/Infrastructure/Contracts, `IdentityDbContext`, DI, factories, migrations |
| 1.2 | Domain model: `User` aggregate (email, hash, roles, verification, lockout, refresh tokens), token value objects, domain events/errors |
| 1.3 | Application: RegisterCustomer/Admin, Login, Refresh, Logout, VerifyEmail, Forgot/Reset/ChangePassword, ResendVerification, GetCurrentUser |
| 1.4 | Auth infrastructure: PBKDF2 hashing, HS256 JWT (15 min) + rotating opaque refresh (7 days), `ICurrentUser` |
| 1.5 | Registration/auth flows with lockout (5 fails → 15 min) and email-verification gates |
| 1.6 | Account lifecycle: expiring single-use verify/reset tokens, unlock, last-login |
| 1.7 | API + authorization: 10 auth endpoints + `GET /auth/me`; `Customer/Admin/SuperAdmin` role policies; ProblemDetails |
| 1.8 | Hardening: password policy, token rotation/revocation/expiry, registration idempotency, no secrets in logs |
| 1.9 | Testing: aggregate/token/lockout units + register→verify→login integration |

Verification: build 0W/0E · arch 12/12 · unit 183/183 · integration 42/42 · 3 Identity migrations applied.
Decisions: Identity is a platform capability; commerce modules depend only on `ICurrentUser`. Out: OAuth/MFA/passkeys/permissions (permissions arrived in Phase 21).

### Phase 1.5 — Runtime Preparation ✅

Made the solution runnable: `Program.cs`/pipeline, `launchSettings` (5260/7119), per-module connection strings, Swagger/OpenAPI `v1` + JWT security, design-time factories ×6 modules, first-boot verification, stabilization (DI/config/migration fixes only).

### Phase 1.6 — Internationalization Foundation ✅

Sub-phases 1.6.1–1.6.10. Shared cross-cutting localization for user-facing strings only (business data stays single-language): `BuildingBlocks.Localization` (`ILocalizer`, `ICurrentCulture`), cultures `en/fr/de/es/ar` (default `en`, 75 keys × 5), resolution query → cookie → `Accept-Language`, localized validators/ProblemDetails/Identity errors/emails/Swagger. Stable `Error.Code`; localization happens at the presentation boundary.

---

## Phase 2 — Catalog ✅

First commerce bounded context; owns product state only.

| Sub-phase | Scope |
|---|---|
| 2.1 | Foundation + domain: module skeleton, `CatalogDbContext`, Product/Category aggregates, value objects, statuses, events |
| 2.2 | Category management: create/update/soft-delete/restore/get/list, hierarchy, unique name/slug |
| 2.3 | Product management: full lifecycle (draft/active/inactive/archived), slug/SEO/visibility, SKU validation |
| 2.4 | Variants: create/update/delete, SKU assignment + uniqueness, product-variant integrity |
| 2.5 | Attributes & specifications: dynamic/category-specific definitions, value validation, filter metadata |
| 2.6 | Images & media: references + metadata only (primary/ordering/alt); storage is an Infra concern |
| 2.7 | Query APIs: SQL `LIKE` search + filter/sort/`PagedResult` projections, `AsNoTracking`; explicitly no Elasticsearch |
| 2.8 | Hardening: validation/security/localization/tests/docs/migrations/Swagger/perf review |

Verification: build 0W/0E · unit 728 · integration 175 · arch 39 · 6/6 migrations, model in sync.
Decisions: module-per-DbContext, no cross-module FKs; business rules in Domain, thin controllers.

---

## Phase 3 — Cart ✅

Redis-primary shopping cart (no SQL schema for cart state).

| Sub-phase | Scope |
|---|---|
| 3.1 | Foundation: Redis as primary store, DI/config, `ICartStore` abstraction, arch tests |
| 3.2 | Domain model: `Cart`/`CartItem` aggregate, ownership, quantity rules, Add/Update/Remove/Clear/Merge |
| 3.3 | Redis persistence: `RedisCartStore`, 3 Lua scripts (CAS, create-for-user, merge-consume), `cart:{id}` + `cart:user:{id}`, sliding TTL, optimistic versioning |
| 3.4 | Guest cart: opaque non-sequential CartId, full lifecycle + expiry + ownership |
| 3.5 | Authenticated cart: `ICurrentUser` integration, one active cart per user |
| 3.6 | Merge Guest→Auth: duplicate-variant quantity sum, guest consumption, idempotent + concurrency-safe |
| 3.7 | Catalog contract: explicit minimal `ICatalogVariantCartLookup` (batch eligibility + price snapshots), no Catalog DbContext |
| 3.8 | Application use cases: 11 commands + 2 queries, ownership + store coordination |
| 3.9 | API: 12 endpoints on the `/cart` controller, Swagger |
| 3.10 | Validation/localization/errors: stable codes + localized messages on the Phase 1.6 system |
| 3.11–3.12 | Hardening (509 unit + 178 integration + 35 arch = 722) + docs/closure |

Decisions: cart prices are non-authoritative snapshots (Checkout reprices from Catalog); Get extends nothing (TTL slides on mutation only).

---

## Phase 4 — Orders & Checkout ✅

Authenticated checkout producing an immutable commercial snapshot.

| Sub-phase | Scope |
|---|---|
| 4.1 | Module foundation: skeleton, `OrdersDbContext`, DI, migration infra, arch boundaries |
| 4.2 | Domain + money: `Order`/`OrderLineItem`/`Money`, snapshots, same-currency decimal arithmetic |
| 4.3 | Lifecycle: `Pending → Cancelled` only, `Cancel()` transition, errors/events, transition matrix tests |
| 4.4 | Persistence: `Orders/OrderItems/CheckoutIdempotency` tables, `decimal(18,6)`, indexes, migration; IDs-as-references, no cross-module FKs |
| 4.5 | Checkout contracts: cart snapshot provider + batch Catalog revalidation contract (no N+1, no Infra deps) |
| 4.6 | Pricing + snapshot: current-price resolution, `Line = Unit × Qty`, immutable snapshot, price-change handling |
| 4.7 | Checkout + idempotency: auth → ownership → non-empty → batch revalidate → atomic persist; SQL `(UserId, IdempotencyKey)` + request fingerprint; safe retry returns the original |
| 4.8 | Cart consumption: best-effort clear after commit only, never throws, no distributed transactions |
| 4.9 | Queries + API: `POST /orders/checkout` (`Idempotency-Key`), `GET /orders`, `GET /orders/{id}`, `POST /orders/{id}/cancel`; ownership-scoped (IDOR → 404) |
| 4.10 | Hardening, testing, documentation |

Verification: build 0W/0E · unit 1497 (224 Orders) · arch 103 (29 Orders) · integration 207 (37 Orders).
Decisions: guest must merge then check out; Catalog answers sellable, Inventory answers reservable; domain events ≠ integration events (no broker yet).

---

## Phase 5 — Inventory + Concurrency ✅

Verified 2026-08-13. Oversell-proof reservations.

| Sub-phase | Scope |
|---|---|
| 5.1 | Foundation: module skeleton, `InventoryDbContext`, DI/config, arch tests |
| 5.2 | Domain + invariants: `InventoryItem(SKU, OnHand/Reserved/Available)`, `Reservation(Pending → Committed/Released/Expired)`, `Reserved ≤ OnHand`, terminal protection |
| 5.3 | Persistence: `InventoryItems/Reservations`, unique SKU, SKU/OrderId/expiry indexes, `OrderId` as reference |
| 5.4 | Stock management: minimal Create/Get/Adjust operations only |
| 5.5 | Reservation lifecycle: Reserve/Release/Commit/Expire commands with exact quantity math |
| 5.6 | Concurrency: atomic conditional `UPDATE … WHERE Reserved+qty ≤ OnHand` (affected-rows gate), never SELECT-then-UPDATE; mandatory race suite (1-stock race → exactly one winner) |
| 5.7 | Expiry/recovery: policy + clock abstraction, idempotent repeat-safe background processing (broker/Saga wiring deferred) |
| 5.8 | Contracts + API: Reserve/Release/Commit contracts (no EF leakage); minimal admin/read REST |
| 5.9 | Hardening + closure |

Verification: unit 1675 · arch 128 · integration 246 (39 Inventory: API/auth/concurrency/expiry).
Decisions: correctness first, integration later; no Outbox/broker/saga/Stripe in this phase.

---

## Phase 6 — Outbox + RabbitMQ ✅

Verified 2026-08-15. Reliable async foundation under at-least-once delivery.

| Sub-phase | Scope |
|---|---|
| 6.1 | Baseline audit (messaging contracts, DbContext/interceptor, existing stores, correlation) |
| 6.2 | Outbox persistence: full message metadata per module boundary + migrations/indexes |
| 6.3 | Domain → Outbox: explicit mapping (candidates, never auto-publish); Business + Outbox commit/rollback together |
| 6.4 | Contracts + versioning: stable transport contracts in module Contracts (ADR-007), `EventVersion V1` |
| 6.5 | Store + atomic claiming: `Pending → Claimed → Published/Retryable` (`UPDLOCK,READPAST,OUTPUT`), crash-recoverable |
| 6.6 | Publisher: durable topic exchange `fluxcommerce.events`, `Type` as routing key, publisher confirms, shared self-healing channel |
| 6.7 | Dispatcher: background poll → claim → publish → mark (batch/poll/backoff/claim-timeout/shutdown) |
| 6.8 | Retry + recovery: broker-down stays retryable, stale-claim recovery, poison → terminal `Failed` |
| 6.9 | Correlation/observability: HTTP → Domain → Outbox → Dispatcher → RabbitMQ propagation, no sensitive payloads |
| 6.10 | E2E tests (7 scenarios on real SQL + RabbitMQ, incl. duplicate-publication tolerance) |
| 6.11–6.14 | Arch/security hardening, perf/ops, docs sync, final verification + report |

Verification: build 0W/0E · arch 157/157 · unit 1796/1796 · integration 300/300 (54 messaging).
Decisions: same-transaction Business + Outbox per module, no distributed transactions; duplicate delivery is expected — consumer idempotency is Phase 7.

---

## Phase 7 — Idempotent Consumers ✅

Verified 2026-08-17. At-least-once + idempotent = reliable.

| Sub-phase | Scope |
|---|---|
| 7.1 | Baseline audit (topology, Messaging/Idempotency blocks, no Outbox rebuild, no Saga/Payments) |
| 7.2 | Consumer contract + processing model (transport vs application split; `IIntegrationEventHandler<T>` + envelope) |
| 7.3 | Inbox persistence: `ProcessedMessage` in the consuming module's DbContext, `(Key, Handler)` PK as final arbiter |
| 7.4 | Atomic processing: `IIdempotentProcessor` commits Business + ProcessedMessage in one transaction; ACK only after commit |
| 7.5 | Consumer infrastructure: manual ACK/NACK, prefetch/concurrency, per-attempt DI scope, graceful shutdown |
| 7.6 | ACK/retry/failure handling: success + duplicate → ACK; transient → redeliver; permanent → bounded terminal |
| 7.7 | Retry + DLQ: `MaxAttempts 3`, Fixed/Linear/Exponential backoff seeded from `x-death`; DLX/DLQ with enriched diagnostic headers |
| 7.8 | Correlation/observability across the consume boundary |
| 7.9 | First real consumer: Order Summary projection (`orders.order_created` → `q.orders.summary`) |
| 7.10–7.14 | Mandatory duplicate/concurrent/crash/retry/DLQ/isolation tests, arch/security/perf hardening, docs, report |

Verification: build 0W/0E · unit 1910/1910 · arch 163/163 · integration 343/343 (47 Phase 6 regression + 40 Phase 7).
Decisions: never Receive → ACK → Process; the same message N times (incl. concurrent/crash) yields exactly one business effect.

---

## Phase 8 — Order/Inventory Saga ✅

Verified 2026-08-20. First reliable cross-module workflow (ADR-009).

| Sub-phase | Scope |
|---|---|
| 8.1 | Architecture audit: Saga ownership, states, transitions, commands, compensation, correlation |
| 8.2 | Saga state + persistence: `OrderSaga` progress-only table in Orders (never business state), `Version` concurrency |
| 8.3 | Orchestration: `order_created → WaitingForInventory → reservation_requested`, duplicate-safe, restart-resumable |
| 8.4 | Reservation command flow: Saga → Outbox → RabbitMQ → Inventory consumer under Phase 7 ACK semantics |
| 8.5 | Result events: `inventory.reserved` / `inventory.reservation_failed` contracts with correlation/causation |
| 8.6 | Order transitions: success advances, failure cancels via `Order.Cancel()`; Order + Saga staged atomically |
| 8.7 | Compensation: `inventory.release_requested` → durable idempotent release (no payment/refund here) |
| 8.8 | Idempotency/concurrency/recovery: duplicate/concurrent/conflicting results, crash-before-ACK redelivery |
| 8.9 | Observability: single W3C trace Checkout → completed (`traceparent` persisted in Outbox) |
| 8.10 | Final verification: 15 mandatory scenarios + report |

Verification: build 0W/0E · unit 2022/2022 · arch 166/166 · integration 376/376.
Decisions: the Saga coordinates, modules own state; no cross-DbContext access, no distributed transactions. Out: Stripe/refunds (Phase 10), expiry worker (Phase 11).

---

## Phase 9 — Stripe Payments ✅

Verified 2026-08-25. Production-grade Payments bounded context (ADR-010/011/012/013).

| Sub-phase | Scope |
|---|---|
| 9.1 | Baseline audit: Saga touchpoint defined without redesigning the Saga; Stripe SDK banned from Domain |
| 9.2 | Module foundation: Domain/Application/Infrastructure/Contracts, `PaymentsDbContext`, externalized Stripe options, arch tests |
| 9.3 | Domain + lifecycle: `Payment (Pending → Processing → Succeeded/Failed/Cancelled)`, terminal guards, domain events |
| 9.4 | Persistence + concurrency: filtered-unique `ProviderPaymentIntentId`, unique `OrderId`, `Version` token, `ProcessedWebhookEvents` dedup |
| 9.5 | Stripe boundary: `IStripePaymentGateway` (Application) → `StripePaymentGateway` (Infrastructure only) |
| 9.6 | Intent creation: authoritative amount/currency (never client-supplied), error mapping, no secrets in logs |
| 9.7 | Intent idempotency: deterministic `intent_{OrderId}_{PaymentId}_{Version}` + app-level unique Order |
| 9.8 | Webhook endpoint + security: `POST /api/payments/webhook` (`AllowAnonymous`, raw body, `Stripe-Signature` verify) |
| 9.9 | Webhook idempotency: `StripeEventId` unique dedup, atomic state + record commit, duplicate → 200 |
| 9.10 | Event → state mapping: explicit allow-list; unknown ignored; out-of-order never regresses terminal states |
| 9.11 | Integration events + Outbox: `payments.payment_succeeded/failed` (`CorrelationId = OrderId`) |
| 9.12–9.13 | Tests/observability/hardening (11 scenarios), final verification + report |

Verification: build 0W/0E · arch 166/166 · unit green · 5 migrations, model in sync.
Decisions: Stripe is an untrusted external reference, never the source of truth; triple idempotency (intent + webhook + consumer). Out: payment saga/refunds (Phase 10).

---

## Phase 10 — Payment Saga & Compensation ✅

Verified 2026-08-29 (scenarios A–H 8/8). Extends the Phase 8 machine (ADR-010/014).

| Sub-phase | Scope |
|---|---|
| 10.1–10.2 | Baseline audit + extended state model (`WaitingForPayment`, `ReleasingInventory`, `Refunding`, terminal matrix) |
| 10.3 | Initiation: `inventory.reserved → payments.payment_requested → Stripe intent` (Saga never calls Stripe) |
| 10.4 | Succeeded flow: correlation-validated confirm → Order `Confirmed`, Saga `Completed` |
| 10.5–10.6 | Failed flow + inventory compensation: `ReleasingInventory → release_requested → Cancelled` |
| 10.7–10.8 | Cancellation/finalization + post-payment refund chain (`Refunding → release → cancel` via Stripe refund) |
| 10.9–10.10 | Idempotency/concurrency/recovery hardening + failure scenarios A–H |
| 10.11–10.12 | W3C tracing + full E2E verification (success and fail-compensate chains) |
| 10.13–10.14 | Arch/security/perf audit, documentation + report |

Verification: build 0W/0E · arch 166/166 · unit 2044/2044 · integration 376/376.
Decisions: compensation is a business operation, not a DB rollback; no 2PC. Out: expiry (Phase 11), failure campaign (Phase 12).

---

## Phase 11 — Reservation Expiry Orchestration ✅

Verified 2026-08-30. Closes the reservation-timeout path.

| Sub-phase | Scope |
|---|---|
| 11.1 | Baseline audit (worker, domain event, Outbox conventions, Saga machine) — no worker rewrite |
| 11.2 | Integration contract: `InventoryReservationExpiredIntegrationEvent` in `Inventory.Contracts` only |
| 11.3 | Transactional state + Outbox: expiry change + Outbox insert in one transaction |
| 11.4 | Consumer registration: idempotent Orders consumer (`Orders.ApplyReservationExpired`) |
| 11.5 | Saga expiry model: `WaitingForPayment → Cancelled` is the only valid transition; all other states benign no-ops |
| 11.6–11.7 | Consumer + compensation/race handling; idempotency/concurrency/recovery safety |
| 11.8 | Tracing across the async expiry boundary |
| 11.9–11.10 | Integration/E2E coverage (10 scenarios); migrated 10 Phase 8 tests to `WaitingForPayment` |
| 11.11–11.12 | Arch/security/perf audit (N+1 batched-load hardening of the expiry processor) + report |

Verification: build 0W/0E · arch 169/169 · unit 2264/2264 · integration 381/381.
Decisions: Inventory owns release (exactly-once, no `release_requested` on the expiry path); Orders owns Order/Saga consequences; no double-release, no duplicate refund.

---

## Phase 12 — Failure Testing & Reliability ✅

Completed 2026-09-04 (12.1–12.13 execution, 12.14 audit-only). Pure validation — zero production changes.

| Sub-phase | Scope |
|---|---|
| 12.1 | Strategy + deterministic test-only fault injection (`DbFaultInterceptor`, `BrokerFaultSimulator`, crash harness) |
| 12.2 | Transactional/DB failures: pre-commit rollback, Business + Outbox `0/0 → 1/1` atomicity, no partial state |
| 12.3 | Outbox/dispatcher failures: crash-after-claim, broker outage → `Pending RetryCount=1 → Published`, stale-claim recovery |
| 12.4 | Consumer crash/redelivery: before-commit, after-commit-before-ACK, mid-process, concurrent duplicates → one effect |
| 12.5 | Retry/poison/DLQ: transient → backoff → success; permanent → bounded stop → enriched DLQ |
| 12.6 | Payment failures: provider timeout/retry, duplicate-intent prevention, duplicate/out-of-order webhooks |
| 12.7 | Saga failures: mid-step crash, mid-compensation crash, duplicate/concurrent/conflicting results, out-of-order |
| 12.8 | Inventory concurrency: 1-stock race, reserve×expiry, commit-vs-release races; `Available ≥ 0`, `Reserved ≤ OnHand` |
| 12.9 | Expiry failures across worker/transaction/Outbox/broker/consumer/Saga |
| 12.10 | E2E failure campaign A–E (retry→success, fail→compensate, crash→redeliver, outage→recover, expiry→cancel) |
| 12.11 | Observability: every failure/retry/DLQ/transition reconstructable via Correlation/Causation/Trace |
| 12.12 | Invariants + automation (single-effect, valid transitions, one-payment-per-flow, repeat-safe compensation) |
| 12.13 | Regression suite: 15 test-infra bugs locked as tests (AsyncLocal, SQLite translation, owned-entity queries, …) |
| 12.14 | Final audit + closure |

Verification: build 0W/0E · unit 2277/2277 · arch 177/177 · integration 494/494 (92 reliability + 13 regression) · fault helpers 0 hits in `src/`.
Done = detected → contained → retried/compensated → recovered, or failure → bounded retries → DLQ → observable/manual.

---

## Phases 13–20 — Planned ⏳

Post-MVP capabilities, intentionally not started: 13 Search · 14 Promotions & Coupons · 15 Wishlist · 16 Reviews · 17 Notifications · 18 Shipping · 19 Analytics · 20 Production Hardening.

---

## Phase 21 — Permission-Based Authorization ✅

Completed 2026-10-01 (amended 2026-10-04). Identity + API authorization only (ADR-016).

| Sub-phase | Scope |
|---|---|
| A — Supremacy | `SuperAdmin` passes every role/permission gate; `Customer/Admin` re-declared through the supremacy handler; `SuperAdminPolicy` stays exact |
| B — Permission model | 9-permission catalog in Identity Domain, `User._permissions` (`Users.Permissions` column), `Grant/Revoke`, `AddUserPermissions` migration (no backfill), JWT `permission` claims on login/refresh (refresh boundary) |
| C — Management surface | SuperAdmin-only grant/revoke/list/catalog endpoints; `InvalidPermission` (400), `PermissionAlreadyGranted` (409) |
| D — Activation | ~40 actions re-pointed (catalog/inventory/orders/payments/dashboard); `Admin` role grants nothing — every permission manual; auth tests rewritten to the permission model |

Permission catalog: `orders.read`, `orders.cancel`, `orders.refund`, `payments.read`, `payments.stats`, `inventory.read`, `inventory.manage`, `catalog.manage`, `dashboard.read` (later removed in Phase 22).
Verification: build 0W/0E · arch 178/178 · unit green · EF model clean. Rollout warning: no automatic grants — existing and new admins start permission-less until SuperAdmin grants.

---

## Phase 22 — Adaptive Dashboard & User Delegation ✅

Completed 2026-10-04 (E1–E3). Dashboard gating remodel + `users.read` (ADR-016/017).

| Sub-phase | Scope |
|---|---|
| E1 — Adaptive dashboard | `GET /admin/dashboard` requires authentication only; sections included iff entitled (`null`/`[]` otherwise, never 403); `dashboard.read` deleted; `AdminDashboardResponse` nullable |
| E2 — Catalog section | Lightweight `GetCatalogCounts` (products/categories), gated by `catalog.manage`; no tables/migration |
| E3 — `users.read` | User reads delegatable; all mutations + admin creation stay SuperAdmin-only (write-delegation attempt rolled back same day) |

Section gates: orders+recent ← `orders.read`; payments ← `payments.read` **or** `payments.stats` (list needs `read`); inventory ← `inventory.read`; catalog ← `catalog.manage`; users+recent ← `users.read`.

---

## Phase 23 — Dashboard Data Completion 🔄

Planned (user-confirmed 2026-10-05). Dashboard-visible data gaps only.

| Sub-phase | Scope |
|---|---|
| F1 — Refunded totals | Second `GroupBy Currency WHERE Refunded` → `RefundedTotalsByCurrency` (net revenue computable from one snapshot) |
| F2 — Recent lists | Composer-only `recentPayments` (`payments.read`) + `recentUsers` (`users.read`), newest-5 pattern |
| F3 — Daily series | Bounded projections + in-memory day bucketing (SQLite/SQL Server portable), `days` param (default 30, max 90) |

Explicitly out: broker DLQ depths + full ops-health surface (separate follow-up track).

---

## Refactoring Cycle R1 — Codebase & Architectural Hardening ✅

After Phase 5. Goal: clean baseline without behavior change; no new business.

Stages R1.1–R1.8: baseline snapshot → dependency audit → BuildingBlocks → Domain/Application → Infrastructure/persistence → API/contracts → test hardening → completion.

Before → after: build `FAILED (4E)` → `0E/0W` (after repair) · arch 128 → 153 (+25) · integration unrunnable → 246/246 on real infra.
Top changes: removed unused BuildingBlocks refs; Catalog lookup N+1 → batch; token-value indexes + repaired Identity migration chain; Domain-enum leak fix + dead-code deletion; 25 new arch tests; ADR-002 + route-prefix docs sync.
Deferred: refresh-token retention, Checkout-returns-aggregate, `ProducesResponseType` parity, outbox-phase rechecks (all later resolved in R2/W1).

## Refactoring Cycle R2 — Structural Hardening ✅

After Phase 11, before Phase 12. Goal: harden the reliability infrastructure without changing behavior.

Stages R2.1–R2.8: baseline → structural audit (57 findings) → BuildingBlocks/file placement → Domain/Application → Infrastructure/persistence → API/contracts → test hardening → completion.

Before → after: 39 → 38 projects (ghost `Resilience` removed) · arch 169 → 177 · unit 2264 → 2277 · integration 380/381 (1 flake) → 402/402 · EF models clean.
Top changes: concretes out of `Application/Interfaces` into `Models/Enums`; webhook verify/interpret split; single `IDateTimeProvider`; shared EF mapping-base helpers (per-module tables kept — KEEP duplication by design); body-less auth-cart endpoint; arch 169 → 177.
Kept by design: per-module Outbox/ProcessedMessages duplication, cohesive `OrderSaga`, indefinite `ProcessedMessage` retention.

## Refactoring Cycle R3 — Full-Application Refactoring ✅

Completed 2026-10-13 (waves W0–W5). No wire-contract breakage; every wave shipped green independently.

| Wave | Scope |
|---|---|
| W0 — Baseline & safety | Deleted ghost `Resilience` folder; removed 10 phantom `Folder Include` entries; baseline 978 `src` files, 178 arch |
| W1 — API host | `GetPayment` query (owns ownership, drops repo access); webhook endpoint service; `IDashboardComposer` (per-module parallelism, legacy failure precedence); shared enum mappers; `OrderResponseMapper.ToCheckout`; DI split into module/messaging/health sections; `ErrorCodeLocalizer` partials; `EndpointTags` moved to Api. Lessons: static-init order across partials needs `Lazy<T>`; same-DbContext EF parallelism is forbidden |
| W2 — Payments | `StripeOptions` (Domain) → `PaymentGatewayOptions` (Infrastructure); `IPaymentGateway`/`IWebhookVerifier` provider-neutral ports; `WebhookEventProcessor` split into dedup/find/apply/stage/commit stages |
| W3 — Orders/Saga | 1643-line orchestrator → 4 partials (byte-identical proof); `IOrderStore` split into aggregate/query ports (composite kept); outcome enum → Domain, options → Infrastructure (records reverted to Infrastructure after arch tests caught an `Application → Inventory.Contracts` violation); counts breakdowns moved into read-model factories ×4 modules |
| W4 — Cart/Catalog/Identity | Cart executors `Commands/` → `Services/`; consumption contract types to `Contracts/Enums+ReadModels`; 9 enum-mirror arch tests; all comment mojibake eliminated repo-wide |
| W5 — BuildingBlocks | Single ActivitySource-name truth (`ActivitySources` ← `TraceContext` ← Payments); `ProcessedMessage.Create` deterministic timestamp; shared `HttpContextItems`; Web `Services/`+`Extensions/` folders; `ValidationProblemDetail` co-located |

Final verification: build 0W/0E · arch 187/187 · unit 2412/2412 · integration 558/561 (3 pre-existing email-link environment failures, reproduced on the clean tree).
