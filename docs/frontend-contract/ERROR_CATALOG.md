# FluxCommerce — Error Catalog for Frontend

> All machine-readable `Error.Code` values, their HTTP status, meaning, trigger, and localized message key.
> How to use: branch on `status` first, then human `detail` (already localized). Do NOT expect `extensions.code` on business errors (see §0). Display `detail`, never raw `code`. On `400` validation read `errors{field:[msg]}`.
> `ErrorCodeLocalizer.CodeToKey`: `code → resource key` (90 entries, Identity/Catalog/Cart/Order/Inventory only — **zero Payment keys, zero Dashboard keys, zero `Identity.InvalidRole`, zero `Cart.InvalidMergeSource`, zero inventory-lifecycle keys beyond `InvalidReservationId`**); miss → fallback raw English `description` (quoted in tables below). Cultures `en/fr/de/es/ar` via `?culture=` > cookie > `Accept-Language`. If resource value == key, fallback is used.

---

## 0. ProblemDetails shape (both envelopes) + wire reality

```json
// 404 / 409 / 401 / 403 — ProblemDetail (application/problem+json)
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4", "title": "Not Found", "status": 404, "detail": "<localized Error.Code>", "instance": "/orders/…", "traceId": "…", "extensions": {} }
// 400 validation — ValidationProblemDetail (7 props)
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1", "title": "Validation Failed", "status": 400, "detail": "<localized>", "instance": "/cart/…", "traceId": "…", "errors": { "quantity": ["…"] } }
```

Type URLs: `400 validation + 400 default → #section-6.5.1`, `401 → rfc7235#section-3.1`, `403 → #6.5.3`, `404 → #6.5.4`, `409 → #6.5.8`, `500 → #6.6.1`.
Status mapping (`ApiControllerBase.MapError`): `NotFound→404`, `Validation→400` (validation-shape), `Conflict→409`, `Unauthorized→401`, `Forbidden→403`, **`Failure→400`** (default branch — not 500). Only unhandled exceptions → `500 {title: Errors.Title.InternalServerError, detail: Errors.UnhandledException}` via `ExceptionHandler` (message hidden).

> Wire correction: `MapError` constructs `ProblemDetail{Type,Title,Status,Detail}` **without** setting `Extensions`, `Instance`, or `TraceId`, and `ValidationProblemDetail{Title,Detail}` only (Status/Type default). So on business-error responses expect `extensions: {}` and possibly absent `instance`/`traceId` — **the `Error.Code` is NOT on the wire, only the localized `detail`**. Keep the code tables for server-log correlation and `en`-detail matching during development; in production match `status` + `detail` + endpoint context.

```ts
// Do NOT rely on this — Extensions is {} on the MapError path:
function errorCodeOf(body: any): string | undefined {
  return body?.extensions?.code; // always undefined for business errors; only detail carries the signal
}
// Instead:
function isValidation400(body: any): boolean { return body?.status === 400 && !!body?.errors; }
```

Two validation paths: (a) `ApiControllerBase.Invalid(errors)` — direct `400` with `Title=Errors.Title.ValidationFailed, Detail=Errors.RequestValidation` + `errors` dict; (b) `MapError Validation` — `Detail=Localize(Error.Code)`. Both share the 7-prop shape. Additionally `[ApiController]` auto-`400` fires before handlers on `DataAnnotations` violations (Cart/Orders/Inventory `[Required]/[Range]/[StringLength]`) with framework-generated `errors{camelCaseField:[msg]}` — no `Error.Code` involved.

Validation `errors{}` field keys are C#/DTO property names (`Password`, `Email`, `RefreshToken`, `VariantId`, `Quantity`, `Currency`, `GuestCartId`, `CartId`, `pageNumber`, `pageSize`, `RecipientName`, `Line1`, `CountryCode`, …) with localized messages (`Validation.Password.Required/MinLength/MaxLength/Uppercase/Lowercase/Digit/Special`, `Validation.Email.Invalid`, `Validation.Fields.*`, `Validation.Field.Invalid/Range`).
```

---

## 1. Identity

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Identity.ValidationFailed` | 400 | One or more fields invalid; see `errors{}` | Any auth validator failure (register/login/refresh/logout/verify/resend/forgot/reset/change/assign/unlock) | `Errors.RequestValidation` |
| `Identity.InvalidEmail` | 400 | Email rejected | Register with malformed email | `Validation.Email.Invalid` |
| `Identity.InvalidRole` | 400 | Unknown role string | `POST /admin/users/{id}/roles` with role outside `Customer\|Admin\|SuperAdmin` | No key → fallback `"The role …"` (English description) |
| `Identity.InvalidPermission` | 400 | Unknown permission string | `POST /admin/users/{id}/permissions` (or `DELETE`) with value outside the §0.5 catalog | `Validation.Permission.Invalid` |
| `Identity.PermissionAlreadyGranted` | 409 | No-op grant | Grant permission user already has | No key → fallback `"The permission is already granted to this user."` |
| `Identity.DuplicateEmail` | 409 | Email taken | Register existing email | `Authentication.Register.EmailAlreadyExists` |
| `Identity.RoleAlreadyAssigned` | 409 | No-op assign | Assign role user already has | `Errors.RoleAlreadyAssigned` |
| `Identity.UserNotFound` | 404 | No such user | `me` (deleted), assign/unlock/change-password on unknown id | `Errors.NotFound` |
| `Identity.InvalidCredentials` | 401 | Wrong email/password (dummy-hash path for unknown email to avoid enumeration) | `POST /auth/login` | `Authentication.Login.InvalidCredentials` |
| `Identity.InvalidToken` | 401 | Token unknown/expired/reused | `refresh` (incl. rotated-reuse), `reset-password`, `verify-email` | `Errors.InvalidToken` |
| `Identity.Unauthenticated` | 401 | No JWT identity | `change-password`, `me` without token | `Authorization.Unauthorized` |
| `Identity.EmailNotVerified` | 403 | Login blocked until verified | `POST /auth/login` unverified (verify via `verify-email`/`resend-verification`) | `Authentication.Login.EmailNotConfirmed` |
| `Identity.AccountLocked` | 403 | Locked after 5 bad passwords, 15m | `POST /auth/login` while `LockoutEndUtc` in future; unlock via `POST /admin/users/{id}/unlock` | `Authentication.Login.LockedOut` |
| `Identity.EmailSendFailed` | 400 (`Failure` default) | Mail send failed | Register/resend/forgot mail outage (backend-only) | `Errors.EmailSendFailed` |

---

## 2. Catalog

Generic `Catalog.ValidationFailed` → 400 covers all Fluent/domain validation (`Name/Slug/Description/Sku/Price/Currency/ImageUrl/DisplayOrder/Value/...`); field details in `errors{}`. Domain-only codes below surface through it unless listed with their own HTTP type.

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Catalog.ValidationFailed` | 400 | Catalog input invalid | Any catalog validator failure; pagination `pageNumber<1` / `pageSize 1..100` violation | `Errors.RequestValidation` |
| `Catalog.CategoryNotFound` | 404 | No such category | Get/update/lifecycle category; create/update product or attribute-def with unknown `categoryId` | `Errors.NotFound` |
| `Catalog.ProductNotFound` | 404 | No such product | Get/update/lifecycle product; variant/image/spec/attribute ops on unknown `productId`; list sub-resources of missing product | `Errors.NotFound` |
| `Catalog.VariantNotFound` | 404 | No such variant | Get/update/delete variant | `Catalog.VariantNotFound` |
| `Catalog.AttributeDefinitionNotFound` | 404 | No such definition | Get/update/lifecycle definition; assign/update/remove value with unknown `attributeDefinitionId` | `Catalog.AttributeDefinitionNotFound` |
| `Catalog.ParentCategoryNotFound` | 404 | Parent missing | Create/update category with unknown `parentId` | `Catalog.ParentCategoryNotFound` |
| `Catalog.ProductAttributeValueNotFound` | 404 | Value missing | Get value never assigned | `Catalog.ProductAttributeValueNotFound` |
| `Catalog.ProductImageNotFound` | 404 | Image missing | Get/update/delete/set-primary/reorder unknown `imageId` | `Catalog.ProductImageNotFound` |
| `Catalog.ProductSpecificationNotFound` | 404 | Spec missing | Get/update/delete unknown `specificationId` | `Catalog.ProductSpecificationNotFound` |
| `Catalog.DuplicateCategoryName` / `Catalog.DuplicateCategorySlug` | 409 | Category uniqueness | Create/update category colliding name/slug | `Catalog.DuplicateCategoryName` / `Catalog.DuplicateCategorySlug` |
| `Catalog.DuplicateProductName` / `Catalog.DuplicateProductSlug` | 409 | Product uniqueness | Create/update product colliding name/slug | `Catalog.DuplicateProductName` / `Catalog.DuplicateProductSlug` |
| `Catalog.DuplicateVariantSku` | 409 | SKU taken | Create/batch/update variant colliding SKU | `Catalog.DuplicateVariantSku` |
| `Catalog.DuplicateAttributeName` / `Catalog.DuplicateAttributeSlug` | 409 | Definition uniqueness | Create/update definition colliding name/slug | `Catalog.DuplicateAttributeName` / `Catalog.DuplicateAttributeSlug` |
| `Catalog.DeletedCategoryCannotBeUsed` | 409 | Soft-deleted category referenced | Create/update product or definition with deleted category | `Catalog.DeletedCategoryCannotBeUsed` |
| `Catalog.ParentCategoryDeleted` | 409 | Parent soft-deleted | Create/update category under deleted parent | `Catalog.ParentCategoryDeleted` |
| `Catalog.DeletedCategoryCannotBeModified` | 409 | Deleted category mutated | Update/lifecycle on soft-deleted category | `Catalog.DeletedCategoryCannotBeModified` |
| `Catalog.DeletedProductCannotBeModified` | 409 | Deleted product mutated | Update/lifecycle/variant/image/spec/attribute op on soft-deleted product | `Catalog.DeletedProductCannotBeModified` |
| `Catalog.DeletedVariantCannotBeModified` | 409 | Deleted variant mutated | Update variant after delete | `Catalog.DeletedVariantCannotBeModified` |
| `Catalog.DeletedAttributeDefinitionCannotBeModified` | 409 | Deleted definition mutated | Update/lifecycle on soft-deleted definition | `Catalog.DeletedAttributeDefinitionCannotBeModified` |
| `Catalog.InvalidProductTransition` | 409 | Illegal status move | `activate/deactivate/archive/restore` from wrong `ProductStatus` | `Catalog.InvalidProductTransition` |
| `Catalog.CannotBeOwnParent` | 400 | Cycle | Update category `parentId == id` | `Catalog.CannotBeOwnParent` |
| `Catalog.InvalidSlug` / `Catalog.InvalidSku` / `Catalog.InvalidUnitPrice` / `Catalog.InvalidSeoMetadata` | 400 | Shape violation | Slug pattern, SKU pattern, money, SEO limits (via `ValidationFailed` envelope) | `Catalog.InvalidSlug` / `Catalog.InvalidSku` / _(unit-price/SEO fallback)_ |
| `Catalog.AttributeValueRequired` / `AttributeValueTooLong` / `InvalidAttributeValue` | 400 | Bad attribute value | Assign/update value empty/too long/wrong `DataType` | `Catalog.AttributeValueRequired` / `Catalog.AttributeValueTooLong` / `Catalog.InvalidAttributeValue` |
| `Catalog.DuplicateProductAttributeValue` | 409 | Already assigned | Assign value twice | `Catalog.DuplicateProductAttributeValue` |
| `Catalog.DeletedAttributeDefinitionCannotBeUsed` / `Catalog.AttributeDefinitionNotActive` / `Catalog.AttributeDefinitionCategoryMismatch` | 409 | Definition unusable | Assign value on deleted/inactive/wrong-category definition | Same keys as codes |
| `Catalog.SpecificationNameRequired` / `SpecificationNameTooLong` / `SpecificationValueRequired` / `SpecificationValueTooLong` | 400 | Bad spec | Create/update spec empty/too long | Same keys as codes |
| `Catalog.DuplicateProductSpecification` | 409 | Spec name taken | Create spec with existing name on product | `Catalog.DuplicateProductSpecification` |
| `Catalog.ImageUrlRequired` / `ImageUrlTooLong` / `AltTextTooLong` / `InvalidImageOrder` | 400 | Bad image/reorder | Add/update image, `reorder` ids mismatch | Same keys as codes |

---

## 3. Cart (`CartErrorMapper`: domain → HTTP)

Note masked cases: `CartAccessDenied→404` (not 403), `VariantUnavailable→404` (not 400), `CatalogOperationFailed (Failure)→400` (default branch).

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Cart.InvalidQuantity` | 400 | Quantity not positive | Add/update `quantity<1` (also DataAnnotations `Range`) | `Cart.InvalidQuantity` |
| `Cart.QuantityLimitExceeded` | 400 | Overflow | Summed quantity `> int.MaxValue` | `Cart.QuantityLimitExceeded` |
| `Cart.InvalidCartItem` | 400 | Item data bad | Add item fails domain guards | `Cart.InvalidCartItem` |
| `Cart.InvalidCartId` | 400 | Bad cart id | Empty/garbage `cartId`/`guestCartId` | `Cart.InvalidCartId` |
| `Cart.InvalidMergeSource` | 400 | Not a guest cart | `POST /cart/merge` with auth-cart id or missing guest cart (`CartErrorMapper` default → `Validation`) | No key → fallback `"Only a guest cart can be used as the source of a cart merge."` |
| `Cart.CatalogOperationFailed` | 400 | Variant check failed | Catalog lookup outage during add/merge | `Cart.CatalogOperationFailed` |
| `Cart.CartItemNotFound` | 404 | Item absent | Update/remove unknown `variantId` | `Cart.CartItemNotFound` |
| `Cart.CartNotFound` | 404 | Cart absent | Get/clear/items on unknown `cartId`; `GET /cart` with no active cart | `Cart.CartNotFound` |
| `Cart.CartAccessDenied` | 404 | Masked access denial | Guest id guessed or auth cart owned by another user (masked to avoid enumeration) | `Cart.CartAccessDenied` |
| `Cart.VariantNotFound` | 404 | Unknown variant | Add item with non-existent variant | `Cart.VariantNotFound` |
| `Cart.VariantUnavailable` | 404 | Ineligible variant | Add `Missing/Unavailable` variant (masked as not-found) | `Cart.VariantUnavailable` |
| `Cart.CartConcurrencyConflict` | 409 | OCC conflict | Concurrent write on same cart `Version`; refetch `GET` cart and retry | `Cart.CartConcurrencyConflict` |
| `Cart.AuthenticationRequired` | 401 | No JWT | Auth cart endpoints without token | `Cart.AuthenticationRequired` |

---

## 4. Orders

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Order.InvalidIdempotencyKey` | 400 | Bad key | `POST /orders/checkout` missing/empty/`>128` `Idempotency-Key` | `Order.InvalidIdempotencyKey` |
| `Order.CartEmpty` | 400 | Nothing to buy | Checkout on empty cart | `Order.CartEmpty` |
| `Order.InvalidAddress` | 400 | Address bad | Shipping/billing violates length/`countryCode==2` rules | `Order.InvalidAddress` |
| `Order.ValidationFailed` | 400 | List params bad | `GET /orders` `pageNumber<1` or `pageSize∉1..100` | `Errors.RequestValidation` |
| `Order.QueryAuthenticationRequired` | 401 | No JWT | `GET /orders*` anonymous | `Authorization.Unauthorized` |
| `Order.AuthenticationRequired` | 401 | No JWT | `POST /orders/checkout` anonymous | `Order.AuthenticationRequired` |
| `Order.CartNotFound` | 404 | Cart gone | Checkout unknown `cartId` | `Order.CartNotFound` |
| `Order.NotFound` | 404 | Order gone or not yours | `GET /orders/{id}` unknown **or other-customer id** (IDOR-masked); cancel unknown | `Errors.NotFound` |
| `Order.CartAccessDenied` | 403 | Wrong owner | Checkout cart owned by another user/guest | `Order.CartAccessDenied` |
| `Order.IdempotencyConflict` | 409 | Key reuse, different body | Same `Idempotency-Key` + different fingerprint; use new key. Same key+body replays original order | `Order.IdempotencyConflict` |
| `Order.VariantUnavailable` | 409 | Lost race / delisted | Checkout revalidation finds `Missing/Unavailable` variant | `Order.VariantUnavailable` |
| `Order.DuplicateVariant` | 409 | Dup line | Cart has same `variantId` twice | `Order.DuplicateVariant` |
| `Order.CurrencyMismatch` | 409 | Mixed currencies | Lines/prices span currencies | `Order.CurrencyMismatch` |
| `Order.CancelConflict` | 409 | Too late | Cancel non-`Pending` order | `Order.CancelConflict` |
| `Order.RefundConflict` | 409 | Not eligible | Refund a non-paid order (no/in-flight saga, missing payment correlation) via `POST /admin/orders/{id}/refund` | `Order.RefundConflict` |
| `OrderSaga.InvalidStateTransition` | 409/400 | Saga race | Async saga transition collision (backend-only, surfaces as 409 if `Conflict`) | _(fallback English)_ |

---

## 5. Inventory

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Inventory.InvalidSku` | 400 | Bad SKU | Empty/over-64/illegal-char SKU (also DataAnnotations) | `Inventory.InvalidSku` |
| `Inventory.InvalidQuantity` | 400 | Bad qty | `onHand<0`, `adjust<=0` (also DataAnnotations) | `Inventory.InvalidQuantity` |
| `Inventory.InvalidOrderId` | 400 | Bad order id | Reservation op with empty order id (saga path, not HTTP) | No key → fallback `"A valid order identifier is required."` |
| `Inventory.InvalidReservationId` | 400 | Bad reservation id | `GET /inventory/reservations/{id}` empty guid | `Inventory.InvalidReservationId` |
| `Inventory.ValidationFailed` | 400 | List/count params bad | Admin lists: bad pagination/status/SKU/order filter; negative low-stock threshold — including `GET /admin/dashboard?lowStockThreshold=-1` (rejected even without `inventory.read`) | `Errors.RequestValidation` |
| `Inventory.InvalidExpiration` | 400 | Bad expiry | Reservation `expiresAt <= now` (saga path) | No key → fallback `"A reservation expiration must be in the future."` |
| `Inventory.SkuNotFound` | 404 | No such SKU | `GET/PATCH /inventory/{sku}` unknown | `Errors.NotFound` |
| `Inventory.ReservationNotFound` | 404 | No such reservation | `GET /inventory/reservations/{id}` unknown | `Errors.NotFound` |
| `Inventory.DuplicateSku` | 409 | SKU exists | `POST /inventory` existing SKU (normalized upper) | `Inventory.DuplicateSku` |
| `Inventory.InsufficientStock` | 409 | Oversell blocked | Reserve `> available` | No key → fallback `"There is not enough available stock to satisfy the requested quantity."` |
| `Inventory.InvalidStateTransition` | 409 | Only `Pending` moves | Commit/release/expire non-`Pending` reservation | No key → fallback `"The reservation cannot transition to the requested state from its current state."` |
| `Inventory.ReleaseExceedsReserved` / `Inventory.CommitExceedsReserved` | 409 | Over-release/commit | Qty exceeds `reserved` (saga path) | No key → fallbacks `"A release cannot return more quantity than is currently reserved."` / `"A commit cannot consume more quantity than is currently reserved."` |
| `Inventory.ReservationLifecycleFailed` | 400 | Lifecycle failed | Generic reservation failure (`Failure` default) | No key → fallback `"The reservation operation failed."` |

---

## 6. Payments (zero `CodeToKey` entries — all fall back to English descriptions)

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Payment.ValidationFailed` | 400 | List params bad | `GET /api/payments/mine`, `GET /admin/payments` bad pagination/status/date filters | Fallback `"The payment query parameters are invalid."` |
| `Payment.InvalidRequest` | 400 | Bad intent request | Create intent validation (backend/saga path) | Fallback `"…invalid request…"` |
| `Payment.OrderIdRequired` | 400 | Missing order | Create intent without order | Fallback `"…order id required…"` |
| `Payment.StripeError` | 400 | Stripe call failed | `Failure` default → 400, retry/backoff upstream | Fallback `"…stripe…"` |
| `Payment.NotFound` | 404 | No such payment or not yours | `GET /api/payments/{id}` unknown **or other-customer id** (masked + warning log) | Fallback `"Payment not found."` |
| `Payment.Unauthorized` | 401 | No JWT | `GET /api/payments/{id}` anonymous | Fallback English |
| `Payment.Forbidden` | 403 | Wrong owner | Create-intent customer mismatch (saga path) | Fallback English |
| `Payment.DuplicateIntent` | 409 | Intent replay | Same `intent_{OrderId}_{PaymentId}_{Version}` retried | Fallback English |
| `Payment.InvalidAmount` | 400 | Amount not `>0` | Domain guard (backend-only) | Fallback `"Payment amount must be greater than zero."` |
| `Payment.InvalidCurrency` / `Payment.CurrencyMismatch` | 400 | Bad currency | Non-ISO3 / mixed currencies (backend-only) | Fallbacks `"Payment currency must be a valid ISO 4217 currency code."` / `"All payment amounts must share the same currency."` |
| `Payment.ProviderPaymentIntentIdRequired` | 400 | Missing `pi_*` | Succeed without intent id (backend-only) | Fallback `"Provider Payment Intent ID is required."` |
| `Payment.InvalidStateTransition` / `Payment.AlreadySucceeded` / `Payment.AlreadyFailed` / `Payment.AlreadyCancelled` / `Payment.AlreadyRefunded` | 409/400 | Terminal-state write | Double succeed/fail/cancel/refund (backend-only) | Fallbacks `"Payment cannot transition…"`, `"Payment has already succeeded/failed/cancelled/refunded and cannot be modified."` |
| `Payment.NotReady` | 409 | Intent not created yet | `GET /api/payments/by-order/{orderId}` before the saga stages the intent — poll/retry shortly | Fallback `"Payment is not ready yet; retry shortly."` |
| `Payment.NotPayable` | 409 | Terminal payment | `GET /api/payments/by-order/{orderId}` on `Succeeded/Failed/Cancelled/Refunded` payment | Fallback `"Payment can no longer be initialized."` |
| `Payment.ProviderUnavailable` | 400 (`Failure` default) | Stripe unreachable | `GET /api/payments/by-order/{orderId}` when the live secret fetch fails — retry, do not create a second order | Fallback `"Payment provider is temporarily unavailable."` |

Stripe webhook (`POST /api/payments/webhook`) never uses this catalog: `400 empty` on missing `Stripe-Signature`/empty payload/bad signature; `200 empty` on processed **and** duplicate (`AlreadyProcessed`).

---

## 7. Framework (no `Error.Code`) — `ExceptionHandler` + middleware

| Case | HTTP Status | Shape | Detail | User-facing message key |
|---|---|---|---|---|
| `KeyNotFoundException` | 404 | `ProblemDetail` (with `instance`, `traceId`) | `exception.Message` (not localized) | `Errors.Title.NotFound` (title) |
| `UnauthorizedAccessException` | 401 | `ProblemDetail` | `exception.Message` | `Errors.Title.Unauthorized` |
| `InvalidOperationException` | 409 | `ProblemDetail` | `exception.Message` | `Errors.Title.Conflict` |
| `ArgumentException` | 400 | `ProblemDetail` | `exception.Message` | `Errors.Title.BadRequest` |
| Unhandled exception | 500 | `ProblemDetail`, generic detail (real message hidden), `instance=path`, `traceId`, `type …#6.6.1` | `Errors.UnhandledException` | `Errors.Title.InternalServerError` / `Errors.UnhandledException` |
| Framework 401 (no/invalid JWT at middleware) | 401 | Default bearer challenge (not `ApiControllerBase` shape) | — | `Authorization.Unauthorized` (conceptual) |
| Framework 403 (policy fail at middleware) | 403 | Default forbid (not `ApiControllerBase` shape) | — | `Authorization.Forbidden` (conceptual) |

## 8. Backend-only domain codes (never serialized directly — folded into §2/§4 envelopes)

`Order.InvalidQuantity`, `Order.QuantityLimitExceeded`, `Order.LineItemNotFound`, `Order.EmptyOrder`, `Order.InvalidOrderNumber`, `Order.InvalidCustomer`, `Order.InvalidStateTransition`, `OrderSaga.InvalidStateTransition`, `Catalog.InvalidUnitPrice` (no `CodeToKey` entry → English fallback), `Catalog.InvalidSeoMetadata` (key exists). These surface as `Catalog/Order.ValidationFailed (400)` or `Conflict (409)` with the umbrella code, not their own.

## 9. Dashboard (host composition — zero `CodeToKey` entries, English fallback)

| Error Code | HTTP Status | Meaning | When Happens | User-facing message key |
|---|---|---|---|---|
| `Dashboard.ValidationFailed` | 400 | Days window out of range | `GET /admin/dashboard?days=` outside `1..90` — rejected regardless of entitlement (same as the threshold rule) | No key → fallback `"The days window must be between 1 and 90."` |
| `Inventory.ValidationFailed` | 400 | Negative threshold | `GET /admin/dashboard?lowStockThreshold=-1` — rejected regardless of entitlement (see §5) | `Errors.RequestValidation` |

First failing entitled section's error surfaces (`400/404` as mapped in §§1–6); unauthorized sections are skipped silently (`null`/`[]`, never 403).

## 10. Retry policy + examples
Retry (once, after refetch): `Cart.CartConcurrencyConflict (409)` → `GET` cart + retry; `Cart.CatalogOperationFailed (400)` → retry; `Payment.StripeError` / `Payment.ProviderUnavailable (400)` → poll, don’t double-post; `Payment.NotReady (409)` → poll `by-order` until `200`; `Inventory.ReservationLifecycleFailed (400)` → retry saga-side only. Never retry: `409 Duplicate*/IdempotencyConflict/CancelConflict/InsufficientStock/NotPayable` (change input/key), `401/403` (login/role fix), `404` masked (don’t probe ids), `400 Dashboard.ValidationFailed` (fix `days`).

```json
// 409 Cart.CartConcurrencyConflict → detail (en): "The cart was modified by another request. Reload the cart and try again."
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8", "title": "Conflict", "status": 409, "detail": "The cart was modified by another request. Reload the cart and try again.", "instance": "/cart/guest/…/items", "traceId": "…", "extensions": {} }
// 409 Order.IdempotencyConflict → generate a new Idempotency-Key
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8", "title": "Conflict", "status": 409, "detail": "The Idempotency-Key was already used for a different checkout request.", "instance": "/orders/checkout", "traceId": "…", "extensions": {} }
// 403 Identity.AccountLocked → show unlock countdown / contact admin
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3", "title": "Forbidden", "status": 403, "detail": "…locked…", "instance": "/auth/login", "traceId": "…", "extensions": {} }
```
