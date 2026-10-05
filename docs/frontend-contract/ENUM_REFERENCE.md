# FluxCommerce — Enum Reference for Frontend

> Every enum the backend can return. Critical wire rule: `AddJsonOptions` registers only `NullableGuidJsonConverter` — **no `JsonStringEnumConverter`**. So all enum-typed JSON body/query fields serialize as **numbers** (see tables). The only string statuses are `Order/Payment.Status`, which are `.ToString()` strings, not enums on the wire.
> Query-string enums (`GET /catalog?…`) bind case-insensitive **names or numbers** via model binding; JSON bodies require **numbers**. Responses always emit **numbers** for enum fields.
> Contracts mirror Domain (`Contracts.Enums` ↔ `Domain.Enums`) with identical values.

```ts
// Wire rule in one place:
type WireEnum<T extends number> = T; // send/receive numbers in JSON bodies + responses
// Query strings are lenient: ?status=Active ≡ ?status=1, ?sortBy=Name ≡ ?sortBy=0
```

---

## 1. Identity

### `UserStatus` — `UserResponse.status` (number on wire)

| Value | Number | Meaning | Frontend action |
|---|---|---|---|
| `Pending` | 0 | Registered, email unverified | Prompt `verify-email` / `resend-verification` |
| `Active` | 1 | Normal | — |
| `Suspended` | 2 | Admin-suspended | Show restricted notice |
| `Locked` | 3 | Lockout (`5 fails → 15m`) or admin lock | Show countdown / `admin unlock` |
| `Deleted` | 4 | Soft-deleted | Treat as `404` |

```ts
enum UserStatus { Pending = 0, Active = 1, Suspended = 2, Locked = 3, Deleted = 4 }
```

### Roles (strings, not an enum)

Constants `Roles`: `Customer="Customer"`, `Admin="Admin"`, `SuperAdmin="SuperAdmin"`. Wire: `LoginResponse.roles: string[]`, `UserResponse.roles: string[]`, JWT `Role[]` claims, `AssignRoleRequest.role: string`. Role policies are role-or-SuperAdmin (SuperAdmin passes all); `SuperAdmin` policy stays exact.

### Permissions (strings, not an enum)

Catalog `PermissionCatalog`: `orders.read|orders.cancel|orders.refund|payments.read|payments.stats|inventory.read|inventory.manage|catalog.manage|users.read`. Wire: JWT `permission[]` claims, `UserResponse.permissions: string[]`, `GrantPermissionRequest.permission: string`. Assigning `Admin` grants nothing — every permission is granted manually; grants apply on next login/refresh (`AdminBundle` = reference full set only).

---

## 2. Catalog (all numbers on wire)

### `ProductStatus` — `ProductResponse.status`; `GET /catalog?status=` filter

| Value | Number | Meaning |
|---|---|---|
| `Draft` | 0 | Created, not sellable |
| `Active` | 1 | Sellable |
| `Inactive` | 2 | Hidden from sale, editable |
| `Archived` | 3 | Terminal, read-only |

```ts
enum ProductStatus { Draft = 0, Active = 1, Inactive = 2, Archived = 3 }
```

### `ProductVisibility` — `Create/UpdateProductRequest.visibility`, `ProductResponse.visibility`; `GET /catalog?visibility=`

| Value | Number | Meaning |
|---|---|---|
| `Visible` | 0 | Storefront-visible |
| `Hidden` | 1 | Admin-only |

```ts
enum ProductVisibility { Visible = 0, Hidden = 1 }
```

### `CategoryStatus` — `CategoryResponse.status` (no Draft/Archived)

| Value | Number |
|---|---|
| `Active` | 0 |
| `Inactive` | 1 |

```ts
enum CategoryStatus { Active = 0, Inactive = 1 }
```

### `VariantStatus` — `VariantResponse.status` (no Draft/Archived)

| Value | Number |
|---|---|
| `Active` | 0 |
| `Inactive` | 1 |

```ts
enum VariantStatus { Active = 0, Inactive = 1 }
```

### `AttributeStatus` — `AttributeDefinitionResponse.status`

| Value | Number |
|---|---|
| `Active` | 0 |
| `Inactive` | 1 |

```ts
enum AttributeStatus { Active = 0, Inactive = 1 }
```

### `AttributeDataType` — `Create/UpdateAttributeDefinitionRequest.dataType`, `AttributeDefinitionResponse.dataType`, `ProductAttributeValueResponse.dataType`

| Value | Number | Value shape |
|---|---|---|
| `Text` | 0 | free string |
| `Number` | 1 | `long` string |
| `Decimal` | 2 | `decimal` string |
| `Boolean` | 3 | `true/false` string |
| `Date` | 4 | date string |
| `DateTime` | 5 | datetime string |

```ts
enum AttributeDataType { Text = 0, Number = 1, Decimal = 2, Boolean = 3, Date = 4, DateTime = 5 }
```

### `CatalogProductSortBy` / `CatalogProductSortDirection` — `GET /catalog?sortBy=&sortDirection=` only

| Enum | Values |
|---|---|
| `CatalogProductSortBy` | `Name=0`, `CreatedAtUtc=1` |
| `CatalogProductSortDirection` | `Ascending=0`, `Descending=1` |

```ts
enum CatalogProductSortBy { Name = 0, CreatedAtUtc = 1 }
enum CatalogProductSortDirection { Ascending = 0, Descending = 1 }
// ?sortBy=Name&sortDirection=Ascending ≡ ?sortBy=0&sortDirection=0
```

### `CatalogVariantEligibilityStatus` — backend-only (Cart/Checkout lookups, never HTTP)

`Available=0`, `Missing=1`, `Unavailable=2`. Frontend sees its effect as `Cart.VariantUnavailable (404)` / `Order.VariantUnavailable (409)`, never the enum.

---

## 3. Orders (strings on wire — exception to the number rule)

`CheckoutResponse.status`, `OrderSummaryResponse.status`, `OrderDetailsResponse.status` are **`string`** (`OrderStatus.ToString()`), never the `OrderStatus` enum.

### `OrderStatus` (domain; wire as string)

| Value | Number (domain) | Wire | Meaning |
|---|---|---|---|
| `Pending` | 0 | `"Pending"` | Placed, saga running — poll `GET /orders/{id}` |
| `Cancelled` | 1 | `"Cancelled"` | Terminal failure/compensation |
| `Confirmed` | 2 | `"Confirmed"` | Terminal success (payment succeeded) |

```ts
type OrderStatus = "Pending" | "Cancelled" | "Confirmed";
```

### `OrderSagaState` — backend-only (never serialized; saga orchestration)

`Started=0`, `WaitingForInventory=1`, `Completed=2`, `Failed=3`, `WaitingForPayment=4`, `ReleasingInventory=5`, `Cancelled=6`, `Refunding=7`. Happy path `Started→WaitingForInventory→WaitingForPayment→Completed`; pay-fail `WaitingForPayment→ReleasingInventory→Cancelled→Completed`; post-pay refund `Completed→Refunding→ReleasingInventory→Cancelled`; expiry `WaitingForPayment→Cancelled`. Frontend infers progress from `OrderStatus` only.

---

## 4. Inventory (number on wire)

### `InventoryReservationStatus` — `InventoryReservationResponse.status` (Admin)

| Value | Number | Meaning |
|---|---|---|
| `Pending` | 0 | Active hold |
| `Committed` | 1 | Consumed by order |
| `Released` | 2 | Returned (cancel/pay-fail compensation) |
| `Expired` | 3 | Returned (30s worker deadline) |

```ts
enum InventoryReservationStatus { Pending = 0, Committed = 1, Released = 2, Expired = 3 }
```

---

## 5. Payments (string on wire — exception to the number rule)

`GET /api/payments/{id}.status` is **`string`** (`PaymentStatus.ToString()`).

### `PaymentStatus` (domain; wire as string)

| Value | Number (domain) | Wire | Meaning |
|---|---|---|---|
| `Pending` | 0 | `"Pending"` | Created, awaiting Stripe |
| `Processing` | 1 | `"Processing"` | Stripe processing |
| `Succeeded` | 2 | `"Succeeded"` | → order `Confirmed` |
| `Failed` | 3 | `"Failed"` | → release inventory, cancel order |
| `Cancelled` | 4 | `"Cancelled"` | Canceled intent |
| `Refunded` | 5 | `"Refunded"` | Post-pay compensation refunded |

```ts
type PaymentStatus = "Pending" | "Processing" | "Succeeded" | "Failed" | "Cancelled" | "Refunded";
```

### Sellability rule: `Status` × `Visibility`

`ProductStatus` is lifecycle, `ProductVisibility` is exposure — both gates apply. Sellable storefront product = `status == Active (1)` **and** `visibility == Visible (0)`. `Inactive/Archived/Draft` or `Hidden (1)` → hide buy actions even if the other gate passes. Same split does not exist for categories/variants/attributes (single `Active/Inactive` each).

Backend-only: `PaymentWebhookAction (Ignore=0,Succeed=1,Fail=2,Cancel=3,Process=4)`, `WebhookProcessResult (Processed=0,AlreadyProcessed=1)` — webhook `200` in both cases, never surfaced with these names.

### Stripe → webhook-action map (interpreter, backend-only reference)

| Stripe `eventType` | `PaymentWebhookAction` | Domain transition |
|---|---|---|
| `payment_intent.succeeded` | `Succeed (1)` | `Payment.Succeed` → wire `"Succeeded"` |
| `payment_intent.payment_failed` | `Fail (2)` | `Payment.Fail` → `"Failed"` |
| `payment_intent.canceled` | `Cancel (3)` | `Payment.Cancel` → `"Cancelled"` |
| `payment_intent.processing` | `Process (4)` | `Payment.Process` → `"Processing"` |
| anything else | `Ignore (0)` | no state change, still recorded → webhook `200` |

---

## 6. Backend-only (never over HTTP — do not model in frontend)

`ErrorType (None=0,NotFound=1,Validation=2,Conflict=3,Failure=4,Unauthorized=5,Forbidden=6)` → frontend sees HTTP status, not the enum. Values verified in source:

- `CartSaveStatus`: `Success=0, VersionConflict=1, CartMissing=2, DuplicateActiveCart=3` (Redis LUA outcomes; frontend sees `CartResponse` or `Cart.CartConcurrencyConflict 409` / `Cart.CartNotFound 404`)
- `CheckoutPersistStatus`: `Created=0, Duplicate=1` (implicit values, no explicit assignment; idempotent-replay signal, frontend sees `CheckoutResponse` or `Order.IdempotencyConflict 409`)
- `CartCheckoutConsumptionStatus`: `Consumed=0, AlreadyEmpty=1, CartNotFound=2, AccessDenied=3, ConcurrencyConflict=4, Failed=5` (best-effort post-checkout cart clear; checkout still succeeds)
- `OrderSagaTransitionOutcome`: `Applied=0, Duplicate=1, Conflicting=2, Invalid=3, Ignored=4` (implicit; reservation-expiry on terminal saga → `Ignored`)
- `RefundRequestOutcome`: `Applied=0, Duplicate=1, NotEligible=2, OrderNotFound=3` (explicit; admin refund saga decision, frontend sees `OrderDetailsResponse` or `404/409`)
- `OutboxStatus`: `Pending=0, InProgress=1, Published=2, Failed=3`
- `ConsumerMessageAction`: `Ack=0, Requeue=1, Reject=2, DeadLetter=3`; `ConsumerFailureClassification`: `Retryable=0, Permanent=1`; `ConsumerBackoffType` (Fixed/Linear/Exponential + `MaxDelay`) — broker internals, no UI mapping.

---

## 7. Quick-wire examples

```json
// ProductResponse — enums as NUMBERS:
{ "id": "…", "name": "Tee", "slug": "tee", "status": 1, "visibility": 0, "categoryId": "…" }
// CategoryResponse / VariantResponse / AttributeDefinitionResponse — NUMBERS:
{ "id": "…", "status": 0 }
{ "id": "…", "productId": "…", "sku": "TEE-BLK-M", "status": 0 }
{ "id": "…", "dataType": 0, "status": 0, "isRequired": false, "sortOrder": 0 }
// ProductAttributeValueResponse — dataType NUMBER, value string:
{ "id": "…", "dataType": 0, "value": "cotton" }
// UserResponse — status as NUMBER, roles/permissions as STRINGS:
{ "id": "…", "roles": ["Customer"], "permissions": ["orders.read"], "status": 1 }
// CheckoutResponse / OrderDetailsResponse — status as STRING:
{ "orderId": "…", "orderNumber": "ORD-ABC123DEF456", "status": "Pending" }
// PaymentResponse — status as STRING:
{ "id": "…", "status": "Succeeded" }
// InventoryReservationResponse — status as NUMBER:
{ "reservationId": "…", "status": 0 }
// GET /catalog?status=Active&visibility=Visible&sortBy=Name&sortDirection=Ascending
// ≡ ?status=1&visibility=0&sortBy=0&sortDirection=0
```

## 8. Forward compatibility + display helpers

- Unknown numeric enum (e.g. `"status": 99` after a backend upgrade): don’t crash — render fallback label (`"Unknown (99)"`), hide gated actions (buy/pay/cancel), log `traceId`.
- Unknown string status (e.g. `"status": "Refunding"` if ever exposed): render raw string, treat order/payment as non-terminal (keep polling).
- Never send a numeric enum as string in JSON bodies (`"visibility": "Visible"` fails without `JsonStringEnumConverter` — send `0`). Query strings accept both.

```ts
const ProductStatusLabel: Record<number, string> = { 0: "Draft", 1: "Active", 2: "Inactive", 3: "Archived" };
const labelOf = (map: Record<number, string>, v: number | string) =>
  typeof v === "number" ? (map[v] ?? `Unknown (${v})`) : String(v);
const isSellable = (status: number, visibility: number) => status === 1 && visibility === 0;
const isTerminalOrder = (s: string) => s === "Cancelled" || s === "Confirmed";
```
