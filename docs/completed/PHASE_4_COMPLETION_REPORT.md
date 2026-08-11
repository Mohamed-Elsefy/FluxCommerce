# Phase 4 — Completion Report (Orders & Checkout)

## 1. Phase Overview

Phase 4 (**Orders & Checkout**) delivered the Orders module — the durable commercial record of the commerce platform. It introduced the **Order** aggregate with **OrderLineItem** entities and the **Money** value object, the **Pending → Cancelled** lifecycle, **authenticated Checkout** with **persistent SQL-backed idempotency**, **Catalog revalidation** with **current-price resolution**, immutable **Order snapshots**, best-effort **Cart consumption**, customer **Order queries** (list + details) and **cancellation**, full **localization**, and a green test suite.

The phase scope is defined in `docs/phases/PHASE_4_ORDERS_CHECKOUT.md` (now marked **Implemented & Verified**).

All architectural principles from `ARCHITECTURE.md` were preserved: Modular Monolith, Clean Architecture, DDD, Module-per-Database (Orders owns its SQL Server database), Module Ownership, Explicit Contracts, no cross-module persistence access, and no cross-module business logic.

## 2. Deliverables Checklist

| Deliverable (`PHASE_4_ORDERS_CHECKOUT.md`) | Status | Evidence |
|--------------------------------------------|--------|----------|
| Complete Orders Module | **Delivered** | `src/Modules/Orders` — Domain, Application, Infrastructure, Contracts, API |
| Orders Domain Model | **Delivered** | 1 aggregate root (Order), 1 entity (OrderLineItem), 8 value objects, 11 domain errors, 2 domain events |
| Money / Monetary Model | **Delivered** | OrderMoney (ISO 4217, non-negative, single-currency arithmetic, `decimal(18,6)`) |
| Order Lifecycle & Cancellation | **Delivered** | OrderStatus (Pending → Cancelled), InvalidStateTransition guard, versioned aggregate |
| Orders Persistence | **Delivered** | OrdersDbContext, EF configurations, owned types, indexes, `InitialOrders` migration, design-time factory |
| Checkout Contracts (Cart + Catalog) | **Delivered** | `ICartCheckoutSnapshotProvider`, `ICatalogVariantCheckoutLookup`, `ICartCheckoutConsumption` |
| Checkout Pricing & Snapshot | **Delivered** | `CheckoutOrderSnapshotBuilder` — authoritative Catalog prices, line totals, subtotal |
| Checkout Idempotency | **Delivered** | `(UserId, IdempotencyKey)` unique constraint, request fingerprint, atomic Order + record persistence |
| Cart Consumption | **Delivered** | Best-effort post-commit clear via `ICartCheckoutConsumption`, never throws/rolls back |
| Order Queries & REST API | **Delivered** | `GET /orders`, `GET /orders/{orderId}`, `POST /orders/{orderId}/cancel`, `POST /orders/checkout` |
| Localization | **Delivered** | `Order.*` error codes mapped in `ErrorCodeLocalizer`, `SharedResources.resx` keys, satellite fallback |
| Swagger | **Delivered** | Tags, XML docs, `ProducesResponseType`, `Idempotency-Key` header, security + Accept-Language filters |
| Security Review | **Verified** | §13 below |
| Performance Review | **Verified** | §11 below |
| EF Migration Review | **Verified** | §15 below |
| Architecture Tests | **Verified** | Orders isolation + dependency rules (29 tests) |
| Unit Tests | **Delivered** | 224 Orders/Checkout-related unit tests across domain, application, persistence, infrastructure |
| Integration Tests | **Delivered** | 37 Checkout/Order end-to-end tests (real HTTP + JWT + SQLite Identity/Orders) |
| Documentation | **Delivered** | `PHASE_4_COMPLETION_REPORT.md`, PROJECT_CONTEXT.md §14.4, docs tree updated |

## 3. Module Structure

```
src/Modules/Orders/
├── FluxCommerce.Modules.Orders.Domain/
│   ├── Aggregates/Order.cs
│   ├── Entities/OrderLineItem.cs
│   ├── Enums/OrderStatus.cs
│   ├── Errors/OrderErrors.cs  (11 errors)
│   ├── Events/OrderCreated.cs, OrderCancelled.cs
│   ├── Results/DomainResult.cs, DomainResultT.cs
│   └── ValueObjects/ (CustomerId, OrderAddress, OrderId, OrderMoney, OrderNumber, OrderProductId, OrderSku, OrderVariantId)
├── FluxCommerce.Modules.Orders.Application/
│   ├── Commands/Checkout/ (CheckoutCommand, CheckoutCommandHandler, CheckoutAddressData)
│   ├── Commands/CancelOrder/ (CancelOrderCommand, CancelOrderCommandHandler)
│   ├── Queries/GetMyOrders/, Queries/GetOrder/
│   ├── Errors/CheckoutErrors.cs, OrderQueryErrors.cs
│   ├── Interfaces/ICheckoutStore.cs, IOrderStore.cs
│   ├── Mapping/OrderResponseMapper.cs
│   ├── ReadModels/OrderSummaryReadModel.cs
│   ├── Services/CheckoutOrderSnapshotBuilder.cs, CheckoutRequestFingerprint.cs
│   └── Validation/OrderPaginationRules.cs
├── FluxCommerce.Modules.Orders.Contracts/
│   ├── Requests/CheckoutRequests.cs
│   └── Responses/CheckoutResponses.cs, OrderResponses.cs
├── FluxCommerce.Modules.Orders.Infrastructure/
│   ├── DbContexts/OrdersDbContext.cs, OrdersDesignTimeDbContextFactory.cs
│   ├── DependencyInjection.cs
│   ├── Persistence/Configurations/ (OrderConfiguration, CheckoutIdempotencyConfiguration)
│   ├── Persistence/Entities/CheckoutIdempotency.cs
│   ├── Persistence/Migrations/20260810115003_InitialOrders.cs
│   └── Persistence/Stores/ (OrderStore, CheckoutStore)
```

## 4. Architecture Boundaries

The Orders module honors the modular-monolith rules enforced by `tests/Architecture/FluxCommerce.ArchitectureTests/OrdersArchitectureTests.cs` (29 tests):

- **Orders must not reference any other module** except the single approved cross-module boundary: `Orders.Application` may depend on `Cart.Contracts` and `Catalog.Contracts` (the checkout contracts introduced in Phase 4.5).
- Orders never touches another module's DbContext, Redis, repositories, or business logic.
- **API → Application → Domain**, **Infrastructure → Application → Domain**; Domain references nothing but BuildingBlocks.Domain.
- Orders owns its database (`FluxCommerce_Orders`) through `OrdersDbContext`; no other module may reference the Orders DbContext.
- Cross-module interaction is asynchronous-at-the-boundary by contract (`ICartCheckoutConsumption` returns statuses, never throws) — the Checkout flow never depends on the Cart implementation.

## 5. Domain Model Summary

| Element | Description |
|---------|-------------|
| `Order` aggregate root | Immutable commercial snapshot: CustomerId, OrderNumber, Status, Shipping/BillingAddress, ShippingCost, Discount, Version, CreatedAtUtc, UpdatedAtUtc; computes Subtotal, GrandTotal internally |
| `OrderLineItem` entity | ProductId, VariantId (Id), Sku, ProductName (≤200), UnitPrice, Quantity; LineTotal = UnitPrice × Quantity |
| `OrderMoney` VO | Non-negative, 3-letter ISO 4217 currency (uppercased/trimmed), same-currency arithmetic, deterministic `Add/Subtract/Multiply` |
| `OrderStatus` | Pending (0), Cancelled (1) |
| Lifecycle | `Order.Place(...)` creates; `Order.Cancel(utcNow)` transitions Pending → Cancelled and increments Version; any other transition fails with `Order.InvalidStateTransition` |
| Invariants | ≥1 line item, no duplicate variants, positive quantity, same currency across all money, positive totals |
| Events | OrderCreated, OrderCancelled (captured by `DomainEventInterceptor` for future outbox work) |

The order stores a full commercial snapshot and does **not** depend on mutable Cart or Catalog state after creation.

## 6. Application Layer Summary

- **CheckoutCommandHandler** orchestrates Phase 4.7/4.8: authenticates via `ICurrentUser`, reads the Cart checkout snapshot, batch-revalidates variants via `ICatalogVariantCheckoutLookup` (never per-item), assembles the immutable Order via `CheckoutOrderSnapshotBuilder`, atomically persists Order + `(Customer, Key)` idempotency record, then clears the Cart best-effort.
- **Idempotency**: the `Idempotency-Key` header participates in a unique `(UserId, IdempotencyKey)` constraint. Same request + key → original Order returned; different request + key → `Order.IdempotencyConflict`; the fingerprint (cartId + normalized addresses, SHA-256) stored with the record distinguishes the two.
- **CancelOrderCommandHandler** is ownership-scoped: loads via `GetOwnedOrderAsync(customerId, orderId)`; a missing/foreign order → 404; a finalised order → 409 `Order.CancelConflict`; a Pending order is cancelled and persisted.
- **GetMyOrdersQueryHandler** is ownership-scoped with pagination (default 1/20, max 100) validated by `OrderPaginationRules` before querying; invalid values → `Order.ValidationFailed`.
- **GetOrderQueryHandler** is ownership-scoped; missing/foreign order → 404 `Order.NotFound`.
- All handlers return `Result<T>` / `PagedResult<T>`; controllers map errors through `ApiControllerBase.ErrorResult` → ProblemDetails.

## 7. Infrastructure & Persistence Summary

- `OrdersDbContext` (SQL Server via ADO, retry-on-failure, 30s command timeout, `__EFMigrationsHistory_Orders`) backed by `BaseDbContext` (persistence conventions: `decimal(18,6)`, `datetime2`).
- `OrderStore.GetMyOrdersPageAsync` uses a two-step portable pagination: newest-first page keys are ordered in memory (SQLite cannot `ORDER BY` a `DateTimeOffset` column), then only the page's Orders are fetched through a projection that sums owned line items without materializing them — no N+1.
- `OrderStore.GetOwnedOrderAsync` returns the tracked aggregate with `Include(LineItems)` so a cancellation can mutate and flush through the same context.
- `CheckoutStore.PersistAsync` writes Order + idempotency record in a single `SaveChangesAsync`; a unique-index violation on `(UserId, IdempotencyKey)` is caught, the change tracker cleared, and the winning record re-read so the handler can return the existing Order or report a conflict.
- Indexes: unique `OrderNumber`, `IX_Orders_CustomerId`, `IX_Orders_CreatedAtUtc`, unique `UX_CheckoutIdempotency_UserId_IdempotencyKey`, `IX_CheckoutIdempotency_OrderId`.
- Design-time factory reads `Database:ModuleConnections:Orders` for `dotnet ef` commands.

## 8. REST API & Contracts

The authenticated `OrdersController` (`/orders`) exposes:

| Endpoint | Returns | Errors |
|----------|---------|--------|
| `POST /orders/checkout` | `ApiResponse<CheckoutResponse>` (OrderId, OrderNumber, Status, CreatedAtUtc) | 400 validation, 401, 403 cart-access, 404 cart, 409 conflict/unavailable |
| `GET /orders` | `ApiResponse<PagedResult<OrderSummaryResponse>>` (newest first) | 400 pagination, 401 |
| `GET /orders/{orderId}` | `ApiResponse<OrderDetailsResponse>` (line items, addresses, money breakdown) | 401, 404 |
| `POST /orders/{orderId}/cancel` | `ApiResponse<OrderDetailsResponse>` (final state) | 401, 404, 409 conflict |

Response contracts live in `FluxCommerce.Modules.Orders.Contracts` (`OrderSummaryResponse`, `OrderDetailsResponse`, `OrderMoneyResponse`, `OrderAddressResponse`, `OrderLineItemResponse`, `CheckoutResponse`). Swagger documents every endpoint with tags, XML docs, explicit `ProducesResponseType`, the `Idempotency-Key` header, security requirements and Accept-Language support.

## 9. Cross-Module Contracts (Phase 4.5)

| Contract | Owner | Purpose |
|----------|-------|---------|
| `ICartCheckoutSnapshotProvider` | Cart module | Reads an authenticated customer's Cart as an immutable checkout snapshot |
| `ICatalogVariantCheckoutLookup` | Catalog module | Batch-resolves variants + current prices/eligibility for checkout (single request, no N+1) |
| `ICartCheckoutConsumption` | Cart module | Best-effort post-commit cart clear; returns a status enum (`Consumed`, `AlreadyEmpty`, `CartNotFound`, `AccessDenied`, `ConcurrencyConflict`, `ConsumerFailure`), never throws |

These boundaries are enforced by the Orders architecture tests (Orders.Application → `Cart.Contracts`/`Catalog.Contracts` only).

## 10. Idempotency & Failure Semantics

- Both a successful Checkout and its idempotency record are committed atomically; if the Commit fails the entire Checkout fails and can be safely retried.
- A retry with the same key + same request returns the existing Order without duplicating it.
- A retry with the same key + different request is rejected with `Order.IdempotencyConflict`.
- Only after the Order is durably committed is the Cart consumed; if the Cart clear fails the Order is returned unchanged (no rollback, no duplication). The Cart consumption contract never throws — failures surface as statuses.

## 11. Performance Review

- **No N+1**: Cart snapshot is a single read; Catalog revalidation is a batch lookup; the list page projection never materializes line items; single-order reads use one `Include`.
- **Two-step pagination** avoids the SQLite `DateTimeOffset` ORDER BY limitation at the cost of loading a customer's order keys into memory — documented trade-off, functionally verified.
- **Indexes** support ownership filters, idempotency lookups, and order-number uniqueness.
- `PagedResult<T>` carries `(Items, TotalCount, PageNumber, PageSize)` with a single COUNT query + page query.

## 12. Security Review

- **Authentication**: all four endpoints are `[Authorize]`; handlers additionally verify `ICurrentUser.IsAuthenticated` (defense in depth) and return 401 with `Order.AuthenticationRequired` / `Order.QueryAuthenticationRequired` when absent.
- **IDOR prevention**: every read and lifecycle operation is ownership-scoped by `customerId`; another customer's Order is indistinguishable from a missing one (404).
- **Validation**: request DTOs use data annotations (`[Required]`, `[StringLength]`); the command handler re-validates via the `OrderAddress` value object and pagination rules; idempotency key length ≤ 128 enforced; fingerprint is a SHA-256 hash (no reversibility / no PII storage).
- **No PII logging**: addresses are never logged.
- **Error codes**: consistent `Order.*` prefixes; no stack traces or internal detail leakage in ProblemDetails.

## 13. Localization Review

- `ErrorCodeLocalizer` maps Order error codes to SharedResources keys: `Order.NotFound` → `Errors.NotFound`, `Order.ValidationFailed` → `Errors.RequestValidation`, `Order.QueryAuthenticationRequired` → `Authorization.Unauthorized`, `Order.CancelConflict` → `Order.CancelConflict`, plus the Checkout error keys (`Order.CartEmpty`, `Order.VariantUnavailable`, `Order.DuplicateVariant`, `Order.CurrencyMismatch`, `Order.AuthenticationRequired`, `Order.CartNotFound`, `Order.CartAccessDenied`, `Order.InvalidIdempotencyKey`, `Order.IdempotencyConflict`, `Order.InvalidAddress`).
- Satellite resx files fall back to the neutral English resource for `Order.*` keys (existing pattern); `SharedResourcesTests.AllKeys` was kept in sync and passes for every culture.
- `Accept-Language` is honored on every Operation via the `AcceptLanguageFilter`.

## 14. Swagger Review

- All four Orders endpoints carry `[Tags(EndpointTags.Orders)]`, XML doc comments (included from the assembly XML), and explicit `ProducesResponseType` entries.
- `Idempotency-Key` is now a documented `[FromHeader(Name = "Idempotency-Key")]` parameter on `POST /orders/checkout` (hardening fix, see §17).
- Response types were corrected to `ApiResponse<...>` on checkout/detail/cancel (hardening fix, see §17).
- Security and Accept-Language filters apply globally; the `Bearer` scheme is documented.

## 15. EF Migrations Review

- `InitialOrders` migration (`20260810115003_InitialOrders`) matches the model: `Orders`, `OrderItems`, `CheckoutIdempotency` tables, owned-type columns (OrderNumber, addresses, money), indexes and unique constraints.
- Verified with `dotnet ef migrations has-pending-model-changes` → **No changes have been made to the model since the last migration.**
- SQL Server precision is enforced via the global `decimal(18,6)` convention.
- Design-time factory is wired and documented in `docs/current/EF_CORE_COMMANDS.md` (§ Orders).

## 16. Test Strategy & Results

### Unit tests (1,497 total; 224 Orders/Checkout-related)
- Domain: `OrderTests`, `OrderLifecycleTests`, `OrderLineItemTests` (incl. quantity/name limits), `OrderMoneyTests` (currency, add/subtract/multiply, zero, mismatches), `OrderAddressTests`, `OrderNumberTests`, `OrderIdTests`, `OrderProductIdTests`, `OrderVariantIdTests`, `OrderSkuTests`, `OrderDomainResultTests`.
- Application: `CheckoutCommandHandlerTests` (auth, cart-not-found, cart-access-denied, empty-cart, idempotency retry/conflict, variant-unavailable, atomic persistence, consumption), `CheckoutOrderSnapshotBuilderTests` (totals, duplicate variants, address validation), `CancelOrderCommandHandlerTests`, `GetMyOrdersQueryHandlerTests` (auth, defaults, validation, data), `GetOrderQueryHandlerTests`, `OrderResponseMapperTests`, plus Cart-side `CartCheckoutConsumptionTests` / `CartCheckoutSnapshotProviderTests` and Catalog-side `CatalogVariantCheckoutLookupTests`.
- Persistence: `OrdersPersistenceTests` (round-trip all properties, line items), `CheckoutStoreTests` (atomic write, duplicate-key handling), `OrdersInfrastructureRegistrationTests` (DI + SQL Server options + history table + migrations assembly).

### Integration tests (207 total; 37 Checkout/Order-related)
- `CheckoutTests`: JWT authentication, real HTTP pipeline, SQLite Identity + Orders DB, fake Cart/Catalog contracts; success, idempotent retry, idempotency conflict, cart-not-found/access-denied, empty cart, variant-unavailable, auth.
- `OrderQueryTests`: `GET /orders` (pagination, ordering, ownership), `GET /orders/{orderId}` (details, ownership 404), `POST /orders/{orderId}/cancel` (success, already-cancelled 409, ownership 404, auth).
- `CheckoutWebApplicationFactory`: SQLite Identity + Orders, `GetOrderStatus` helper, `ResetOrdersDatabase`, deterministic Cart/Catalog fakes.

### Results
```
Build:                     0 warnings, 0 errors
Unit tests:               1,497 / 1,497 passed
Architecture tests:         103 /   103 passed (29 Orders)
Integration tests:          207 /   207 passed (37 Checkout/Order)
EF model sync:             no pending model changes
```

## 17. Hardening Changes Applied in 4.10

1. **Swagger response accuracy** — `Checkout`, `GetOrder` and `CancelOrder` declared bare DTO types in `ProducesResponseType`, but every successful response is wrapped in `ApiResponse<T>`; corrected to `ApiResponse<CheckoutResponse>`, `ApiResponse<OrderDetailsResponse>`.
2. **Swagger Idempotency-Key documentation** — the header was read imperatively from `Request.Headers`, so it never appeared in OpenAPI; replaced with a documented `[FromHeader(Name = "Idempotency-Key")] string? idempotencyKey` action parameter (behavior unchanged: missing header maps to `string.Empty` and fails command validation).
3. **Error semantics** — `CheckoutCommandHandler.ResolveExistingAsync` reported `Order.CartNotFound` when an idempotency record referenced a missing Order; corrected to the accurate `Order.NotFound` (404) since the resource is the Order, not the Cart.
4. **Documentation sync** — `PROJECT_CONTEXT.md` §14.4 added (subphase table, key decisions, verification, deferrals), the "Completed so far" line and the `/docs` tree updated; `docs/current/EF_CORE_COMMANDS.md` already covered Orders.

## 18. Known Limitations / Deferred Items

- **Cancellation concurrency**: no optimistic-concurrency token is configured on `Order`; two simultaneous cancels of the same Pending order both succeed (final state converges to Cancelled). Follow-up in a later phase if required.
- **In-memory key sort on list queries**: the two-step pagination materializes a customer's order keys before sorting (SQLite `DateTimeOffset` limitation). Acceptable at current scale; a future migration to store UTC `datetime2` would allow provider-side ordering.
- **CheckoutCommandHandler returns the `Order` aggregate** (`Result<Order>`) to the controller, which maps the minimal `CheckoutResponse`; deliberate design retained (command producing an aggregate), with the full representation deferred to the 4.9 queries.
- **No compensation**: cancellation performs no inventory/payment compensation (Phases 5–11). Cart clearing is best-effort by contract.
- `Order.LineItem.ChangeQuantity` is internal/unused by the current lifecycle — retained for future phases.
- `CheckoutResponse` is deliberately minimal; clients fetch full details via `GET /orders/{orderId}`.

## 19. Final Verification Summary

- Full solution build: **0 warnings / 0 errors**.
- Unit tests: **1,497/1,497** ✅
- Architecture tests: **103/103** ✅ (29 Orders)
- Integration tests: **207/207** ✅ (37 Checkout/Order)
- EF model in sync with the `InitialOrders` migration ✅
- Orders is production-ready within Phase 4 scope; remaining items are explicitly documented above.
