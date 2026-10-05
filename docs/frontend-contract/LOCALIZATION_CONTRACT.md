# FluxCommerce — Localization Contract for Frontend

> Culture codes, passing mechanism, and translation keys. Source: `Api/Extensions/LocalizationConfiguration`, `Api/Swagger/AcceptLanguageFilter`, `BuildingBlocks.Localization` (`ILocalizer`, `SharedResources*.resx`), `Api/Localization/ErrorCodeLocalizer`.

---

## 1. Culture codes

- Default: `en`. Supported: `en, fr, de, es, ar` (`SupportedCultures`, `SupportedUICultures`).
- `ar` is RTL — mirror layout, keep numbers/LTR for `orderNumber`, `sku`, tokens, amounts.
- `UserResponse.preferredLanguage` / `timeZone` are profile hints only — they do NOT drive response localization; only the request culture does.

## 2. Passing mechanism (priority order — providers cleared then re-added)

1. Query string — `?culture=fr` (also `?ui-culture=fr` via `QueryStringRequestCultureProvider` defaults). Best for link sharing/debugging; wins over everything.
2. Cookie — `.AspNetCore.Culture` (via `CookieRequestCultureProvider`).
3. Header — `Accept-Language: fr` (via `AcceptLanguageHeaderRequestCultureProvider`; Swagger advertises optional enum `en|fr|de|es|ar`, default `en`, on every operation).

`ApplyCurrentCultureToResponseHeaders = true` → every localized response carries `Content-Language: <resolved>`; read it to confirm what you got (may differ from what you sent on fallback).

```ts
// Recommended sender:
async function localizedFetch(url: string, init: RequestInit = {}, culture = "en") {
  const res = await fetch(url, { ...init, headers: { ...init.headers, "Accept-Language": culture } });
  const resolved = res.headers.get("Content-Language") ?? culture; // actual language of title/detail/errors
  return { res, resolved };
}
// Debug override (wins over header): GET /orders?pageNumber=1&culture=ar
```

## 3. What is localized

- `ProblemDetail.title` — `Errors.Title.*` (`ValidationFailed/NotFound/Conflict/Unauthorized/Forbidden/BadRequest/InternalServerError`).
- `ProblemDetail.detail` / `ValidationProblemDetail.detail` — `ErrorCodeLocalizer.Localize(Error.Code, englishFallback)`.
- `ValidationProblemDetail.errors{field:[msg]}` — field messages (`Validation.*`, `Validation.Field.*`, `Validation.Fields.*`).
- `Email.Verification.*` / `Email.PasswordReset.*` — backend emails (subjects/headings/bodies, validity notes); not rendered by SPA except link landing pages.
- `Common.*` — generic UI vocabulary shipped in resources for shared frontend use (`Success/Error/Warning/Save/Cancel/Confirm/Delete/Search/Language/Loading/NoResults/Unknown`).

Titles are always localized; `detail` falls back to English when the key is untranslated (see §5).

## 4. Translation keys (174 in `en`; group prefix → use)

Full key list from `SharedResources.resx` (`en` authoritative):

```
Common.Success, Common.Error, Common.Warning, Common.Save, Common.Cancel, Common.Confirm,
Common.Delete, Common.Search, Common.Language, Common.Loading, Common.NoResults, Common.Unknown
Validation.Field.Required, Validation.Field.Invalid, Validation.Field.MinLength, Validation.Field.MaxLength, Validation.Field.Range
Validation.Email.Required, Validation.Email.Invalid, Validation.Email.MaxLength
Validation.Password.Required, Validation.Password.MinLength, Validation.Password.MaxLength,
Validation.Password.Uppercase, Validation.Password.Lowercase, Validation.Password.Digit,
Validation.Password.Special, Validation.Password.TooWeak, Validation.Password.Mismatch
Validation.Fields.FirstName, Validation.Fields.LastName, Validation.Fields.Token, Validation.Fields.RefreshToken,
Validation.Fields.CurrentPassword, Validation.Fields.CategoryName, Validation.Fields.CategoryId, Validation.Fields.Slug,
Validation.Fields.Description, Validation.Fields.ProductName, Validation.Fields.ProductId, Validation.Fields.Visibility,
Validation.Fields.Sku, Validation.Fields.VariantId, Validation.Fields.VariantName, Validation.Fields.AttributeName,
Validation.Fields.AttributeId, Validation.Fields.DataType, Validation.Fields.AttributeValue, Validation.Fields.SpecificationName,
Validation.Fields.SpecificationValue, Validation.Fields.SpecificationId, Validation.Fields.ImageId, Validation.Fields.ImageUrl,
Validation.Fields.AltText, Validation.Fields.DisplayOrder, Validation.Fields.SortOrder, Validation.Fields.Images,
Validation.Fields.PageNumber, Validation.Fields.PageSize, Validation.Fields.Quantity, Validation.Fields.CartId,
Validation.Fields.Currency, Validation.Fields.UnitPriceAmount, Validation.Fields.Permission, Validation.Fields.UserId,
Validation.Permission.Invalid
Authentication.Login.InvalidCredentials, Authentication.Login.LockedOut, Authentication.Login.EmailNotConfirmed,
Authentication.Register.EmailAlreadyExists, Authentication.VerifyEmail.InvalidToken, Authentication.VerifyEmail.Success,
Authentication.PasswordReset.InvalidToken, Authentication.PasswordReset.Success
Authorization.Unauthorized, Authorization.Forbidden, Authorization.RoleRequired, Authorization.ResourceOwner
Errors.UnhandledException, Errors.RequestValidation, Errors.NotFound, Errors.Conflict, Errors.Cancelled,
Errors.OperationFailed, Errors.InvalidToken, Errors.EmailSendFailed, Errors.RoleAlreadyAssigned
Errors.Title.ValidationFailed, Errors.Title.NotFound, Errors.Title.Conflict, Errors.Title.Unauthorized,
Errors.Title.Forbidden, Errors.Title.BadRequest, Errors.Title.InternalServerError
Email.Verification.Subject, Email.Verification.Heading, Email.Verification.Body, Email.Verification.TextIntro,
Email.Verification.ButtonText, Email.Verification.ValidityNote
Email.PasswordReset.Subject, Email.PasswordReset.Heading, Email.PasswordReset.Body, Email.PasswordReset.TextIntro,
Email.PasswordReset.ButtonText, Email.PasswordReset.ValidityNote, Email.CopyLinkInstruction
Catalog.DuplicateCategoryName, Catalog.DuplicateCategorySlug, Catalog.ParentCategoryDeleted, Catalog.ParentCategoryNotFound,
Catalog.DeletedCategoryCannotBeModified, Catalog.CannotBeOwnParent, Catalog.InvalidSlug, Catalog.DuplicateProductName,
Catalog.DuplicateProductSlug, Catalog.DeletedCategoryCannotBeUsed, Catalog.DeletedProductCannotBeModified,
Catalog.InvalidProductTransition, Catalog.InvalidSeoMetadata, Catalog.VariantNotFound, Catalog.DuplicateVariantSku,
Catalog.InvalidSku, Catalog.DeletedVariantCannotBeModified, Catalog.AttributeDefinitionNotFound, Catalog.DuplicateAttributeName,
Catalog.DuplicateAttributeSlug, Catalog.DeletedAttributeDefinitionCannotBeModified, Catalog.AttributeValueRequired,
Catalog.AttributeValueTooLong, Catalog.InvalidAttributeValue, Catalog.DuplicateProductAttributeValue,
Catalog.ProductAttributeValueNotFound, Catalog.DeletedAttributeDefinitionCannotBeUsed, Catalog.AttributeDefinitionNotActive,
Catalog.AttributeDefinitionCategoryMismatch, Catalog.SpecificationNameRequired, Catalog.SpecificationNameTooLong,
Catalog.SpecificationValueRequired, Catalog.SpecificationValueTooLong, Catalog.DuplicateProductSpecification,
Catalog.ProductSpecificationNotFound, Catalog.ImageUrlRequired, Catalog.ImageUrlTooLong, Catalog.AltTextTooLong,
Catalog.ProductImageNotFound, Catalog.InvalidImageOrder
Cart.InvalidQuantity, Cart.QuantityLimitExceeded, Cart.CartItemNotFound, Cart.CartNotFound, Cart.CartAccessDenied,
Cart.CartConcurrencyConflict, Cart.InvalidCartItem, Cart.InvalidCartId, Cart.AuthenticationRequired,
Cart.VariantNotFound, Cart.VariantUnavailable, Cart.CatalogOperationFailed
Order.CartEmpty, Order.VariantUnavailable, Order.DuplicateVariant, Order.CurrencyMismatch, Order.AuthenticationRequired,
Order.CartNotFound, Order.CartAccessDenied, Order.InvalidIdempotencyKey, Order.IdempotencyConflict,
Order.InvalidAddress, Order.CancelConflict
Inventory.InvalidSku, Inventory.InvalidQuantity, Inventory.DuplicateSku, Inventory.InvalidReservationId
```

`ErrorCodeLocalizer.CodeToKey` maps ~90 `Error.Code → key` (Identity/Catalog/Cart/Order/Inventory; **zero Payment keys**). Notable remaps: `Identity.EmailNotVerified → Authentication.Login.EmailNotConfirmed`, `Identity.AccountLocked → Authentication.Login.LockedOut`, `*.NotFound/SkuNotFound/ReservationNotFound → Errors.NotFound`, `Order.QueryAuthenticationRequired → Authorization.Unauthorized`.

## 5. Fallback chain (why you sometimes get English)

- Resource counts: `en` 174, `ar` 159, `fr/de/es` 143 — translations are incomplete (e.g. `fr` localizes `Errors.Title.NotFound → "Introuvable"` and login errors but lacks keys like `Cart.CartConcurrencyConflict`).
- `ILocalizer`: miss returns the key itself (`localizer[key] == key`, `Get(key) == key`).
- `ErrorCodeLocalizer.Localize(code, fallback)`: code not in `CodeToKey` → English `description`; value == key (untranslated) → English `description`.
- Net effect: request `fr` for an untranslated cart error → `detail` arrives in English, `title` in French, `Content-Language: fr`. Frontend must accept mixed-language payloads and always render `detail` as-is (never map `detail` back to a key).

Placeholders: messages use `{0}, {1}` (`MinLength/MaxLength/Range`, e.g. page-size range) formatted in current culture — render with `Intl.NumberFormat(resolved)`.

## 6. Examples

```http
GET /orders/invalid-id HTTP/1.1
Accept-Language: fr
```
```json
// Content-Language: fr — title localized, NotFound remapped to Errors.NotFound
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4", "title": "Introuvable", "status": 404, "detail": "…", "instance": "/orders/…", "traceId": "…", "extensions": {} }
```

```http
POST /cart/guest/…/items HTTP/1.1
Accept-Language: de
Content-Type: application/json
{ "variantId": "…", "unitPriceAmount": 29.99, "currency": "USD", "quantity": 0 }
```
```json
// 400 ValidationProblemDetail — errors{} in German where translated, else English fallback
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1", "title": "…", "status": 400, "detail": "…", "instance": "/cart/…", "traceId": "…", "errors": { "quantity": ["…"] } }
```
