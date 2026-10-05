# FluxCommerce — API Contract for Frontend

> Source of truth: `src/FluxCommerce.Api/Controllers/**/*.cs` (13 controllers, attribute routing only, no minimal APIs).
> Base URLs: `https://localhost:7119` / `http://localhost:5260`. No `/api/v1` prefix.
> OpenAPI/Swagger export: dev-only `GET /swagger/v1/swagger.json`, UI `GET /swagger`. Single doc `v1: FluxCommerce API`.
> Auth: JWT Bearer `Authorization: Bearer <accessToken>`. Swagger global Bearer requirement, cleared on `[AllowAnonymous]`.
> Global headers: optional `Accept-Language: en|fr|de|es|ar` (every operation), optional `X-Correlation-ID` (echoed + `X-Request-ID` in response).

---

## 0. Conventions (apply to all endpoints unless noted)

### 0.1 Success envelope

```json
// 200 OK — ApiResponse<T>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "...": "..." } }
// 200 OK — ApiResponse (no data, e.g. logout, forgot/reset/change password)
{ "isSuccess": true, "message": "Operation completed successfully." }
// 201 Created — ApiResponse<T> + Location header (Create* actions, POST /cart/guest, POST /cart)
{ "isSuccess": true, "message": "Operation completed successfully.", "data": "guid-or-object" }
// 204 No Content — empty body (all Catalog PUT / POST lifecycle, attribute/image/spec assign/update/delete)
```

C# is `ApiResponse { bool IsSuccess; string? Message; }` / `ApiResponse<T> : ApiResponse { T? Data; }` with default message `"Operation completed successfully."`.
Wire casing `isSuccess/message/data` comes from ASP.NET Core default camelCase serialization — no explicit `[JsonPropertyName]` / naming policy in code. Only `NullableGuidJsonConverter` is registered explicitly.

### 0.2 Error envelope

```json
// ProblemDetail — application/problem+json (ApiControllerBase.ErrorResult + ExceptionHandler)
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4", "title": "<localized>", "status": 404, "detail": "<localized Error.Code>", "instance": "/path", "traceId": "...", "extensions": {} }
// ValidationProblemDetail — 400 validation (7 properties, not just errors)
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1", "title": "<localized>", "status": 400, "detail": "<localized>", "instance": "/path", "traceId": "...", "errors": { "field": ["msg"] } }
```

C# is `ProblemDetail { Type="about:blank", Title, Status, Detail, Instance?, TraceId?, Extensions: Dictionary<string,object?> }` and `ValidationProblemDetail { Type="...#section-6.5.1", Title, Status=400, Detail, Instance?, TraceId?, Errors: Dictionary<string,string[]> }`.
`ExceptionHandler` only emits `ProblemDetail` (`Instance=request.Path`, `TraceId=traceIdentifier`, `Content-Type: application/problem+json`).

Mapping (`ApiControllerBase.MapError`): `NotFound→404`, `Validation→400 (validation-shape)`, `Conflict→409`, `Unauthorized→401`, `Forbidden→403`, default→400. Unhandled exception→500 generic localized (message hidden). Key on `extensions.code` / `detail`, display `detail` not raw code. Localization providers: `?culture=` > cookie > `Accept-Language`; response header `Content-Language` applied.

### 0.3 Common types

```ts
type Guid = string; // uuid
type DateTime = string; // ISO-8601 — Identity + Catalog timestamps use DateTime (not DateTimeOffset)
type DateTimeOffset = string; // ISO-8601 utc — Cart + Orders + Inventory timestamps use DateTimeOffset
interface PagedResult<T> { items: T[]; totalCount: number; pageNumber: number; pageSize: number; } // no totalPages/hasNext — compute client-side
interface MoneySplit { unitPriceAmount: number; currency: string; } // decimal as JSON number, never float-math; currency ISO-4217 UPPER ^[A-Z]{3}$
```

Pagination query (where supported): `?pageNumber=int>=1 &pageSize=int 1..100`. Defaults: Catalog list/search validator defaults; Orders `1/20 max 100`.

### 0.4 Auth / headers / localization (verified)

- JWT: `Issuer/Audience/SigningKey` required, `HmacSha256`, `ValidateIssuer/Audience/Lifetime/SigningKey=true`, `AccessTokenLifetimeMinutes=15`, `ClockSkewMinutes=5`. Claims: `ClaimTypes.NameIdentifier=userId (Guid)`, `ClaimTypes.Email`, `ClaimTypes.Role[]`, `permission[]` (one claim per granted action-level permission, Phase 21). Lifetimes: refresh `7d` (rotating, reuse invalid), email-verify `24h`, password-reset `1h`. Policies: `Customer`/`Admin` = role-or-SuperAdmin (SuperAdmin passes everything, Phase A); `SuperAdmin` = exact role; one permission policy per catalog value (§0.5). Grants apply on next login/refresh. Dev `Issuer/Audience=https://localhost:7119`.
- Headers: `Authorization: Bearer <accessToken>`; `Accept-Language: en|fr|de|es|ar` optional on every op (Swagger `AcceptLanguageFilter`); `X-Correlation-ID` optional inbound (reused else generated `Guid:N`), always echoed in response; `X-Request-ID` always generated `Guid:N` and echoed (`RequestLoggingMiddleware`, Serilog scope `{CorrelationId,RequestId,TraceId,SpanId}`).
- Localization: default `en`, supported `[en,fr,de,es,ar]`, `ApplyCurrentCultureToResponseHeaders=true`, providers after clear: `QueryString (?culture=) > Cookie > Accept-Language`. Display `detail`, not raw `code` (`ErrorCodeLocalizer` maps ~90 `Identity|Catalog|Cart|Order|Inventory.*` codes, fallback raw description).

### 0.5 Permission catalog (Phase 21, ADR-016)

Action-level permissions granted per admin by SuperAdmin (`area.action` strings, also returned as `permissions: string[]` on `UserResponse`/`UserAdminResponse` for menu rendering):

| Permission | Grants |
|---|---|
| `orders.read` | `GET /admin/orders`, `GET /admin/orders/{id}`, `GET /admin/orders/counts` |
| `orders.cancel` | `POST /admin/orders/{id}/cancel` |
| `orders.refund` | `POST /admin/orders/{id}/refund` |
| `payments.read` | `GET /admin/payments`, `GET /admin/payments/{id}` |
| `payments.stats` | `GET /admin/payments/stats` |
| `inventory.read` | All `GET /inventory/**` (item, reservation, lists, counts) |
| `inventory.manage` | `POST /inventory`, `PATCH /inventory/{sku}` |
| `catalog.manage` | All Catalog writes (reads stay anonymous) |
| `users.read` | `GET /admin/users`, `/counts`, `/{id}`, `/{id}/permissions` |

Assigning the `Admin` role grants nothing by itself — every permission is granted manually afterwards (amended 2026-10-04); only the permission catalog document (`GET /admin/users/permissions`) stays `SuperAdmin`-only. `SuperAdmin` passes every gate without holding permissions. Enforcement failure → `403` (no `extensions.code`, match `status` + endpoint context).

---

## 1. Identity — Tag `Identity`

> Contract DTOs have **zero `DataAnnotations`** — all strings are non-nullable `string` in C# (implicitly required). Validation is Fluent (`*CommandValidator`, `EmailRules`, `PasswordPolicy`, `NameRules`) → `400 ValidationProblemDetail`. JSON is camelCase by framework default (`email`, `refreshToken`, ...).
> Date/time is `DateTime` (not `DateTimeOffset`): `LoginResponse.refreshTokenExpiresAtUtc: DateTime`, `UserResponse.registeredAtUtc: DateTime`, `ResendVerificationResponse.expiresAtUtc: DateTime?` nullable.
> `UserStatus` enum: `Pending=0, Active=1, Suspended=2, Locked=3, Deleted=4`.

### 1.1 POST /auth/register

| Field | Value |
|---|---|
| Endpoint | `/auth/register` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `RegisterCustomerRequest { email: string, password: string, firstName: string, lastName: string }` |
| Response | `200 ApiResponse<UserResponse>` — `UserResponse { id: Guid, email: string, roles: string[], isEmailVerified: bool, registeredAtUtc: DateTime, firstName, lastName, displayName?: string, phoneNumber?: string, isPhoneVerified: bool, avatarUrl?: string, preferredLanguage: string, timeZone: string, status: UserStatus, permissions: string[] }` where `UserStatus = Pending\|Active\|Suspended\|Locked\|Deleted` |
| Status Codes | `200, 400, 409` |
| Validation | email required+format (max 254, trimmed+lowercased); password policy 8..128 + upper+lower+digit+special `!@#$%^&*()-_=+[]{};:'",.<>/?\|`+"`"+`~`; firstName/lastName required, trimmed, max 100/201 displayName |
| Errors | `400 ValidationProblemDetail`, `409 Identity.DuplicateEmail (Conflict)` |
| Pagination/Sorting/Filtering | — |

### 1.2 POST /auth/register-admin

| Field | Value |
|---|---|
| Endpoint | `/auth/register-admin` |
| HTTPMethod | `POST` |
| Authorization | `Authorize(SuperAdminPolicy)` — JWT role `SuperAdmin` required; `401` no/invalid JWT, `403` non-SuperAdmin |
| Request | Body `RegisterAdminRequest { email, password, firstName, lastName }` (separate record, same 4 non-nullable strings as 1.1) |
| Response | `201 ApiResponse<UserResponse>` (same as 1.1) via `Created()` |
| Status Codes | `201, 400, 401, 403, 409` |
| Validation | Same as 1.1 |
| Errors | Same as 1.1 + `401/403` |
| Pagination/Sorting/Filtering | — |

### 1.3 POST /auth/login

| Field | Value |
|---|---|
| Endpoint | `/auth/login` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `LoginRequest { email: string, password: string }` |
| Response | `200 ApiResponse<LoginResponse>` — `LoginResponse { userId: Guid, email: string, roles: string[], accessToken: string (JWT, 15m, claims NameIdentifier=userId, Email, Role[]), refreshToken: string (opaque, 7d rotating), refreshTokenExpiresAtUtc: DateTime }` |
| Status Codes | `200, 400, 401` |
| Validation | email required+format; password non-empty |
| Errors | `400 Validation`, `401 Identity.InvalidCredentials \| EmailNotVerified \| AccountLocked (5 fails → 15m lockout)` |
| Pagination/Sorting/Filtering | — |

### 1.4 POST /auth/refresh

| Field | Value |
|---|---|
| Endpoint | `/auth/refresh` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` (presents opaque refresh token, not JWT) |
| Request | Body `RefreshRequest { refreshToken: string (required) }` |
| Response | `200 ApiResponse<LoginResponse>` (new rotated pair; old token revoked, reuse → `401 InvalidToken`) |
| Status Codes | `200, 400, 401` |
| Validation | token required |
| Errors | `401 Identity.InvalidToken` |
| Pagination/Sorting/Filtering | — |

### 1.5 POST /auth/logout

| Field | Value |
|---|---|
| Endpoint | `/auth/logout` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `LogoutRequest { refreshToken: string }` |
| Response | `200 ApiResponse` (no data; idempotent revoke) |
| Status Codes | `200, 400` |
| Validation | token required |
| Errors | `400 Validation` |
| Pagination/Sorting/Filtering | — |

### 1.6 POST /auth/verify-email

| Field | Value |
|---|---|
| Endpoint | `/auth/verify-email` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `VerifyEmailRequest { token: string (required) }` |
| Response | `200 ApiResponse<UserResponse>` |
| Status Codes | `200, 400, 404` |
| Validation | token required |
| Errors | `400 Validation`, `404 user/token not found` |
| Pagination/Sorting/Filtering | — |

### 1.7 POST /auth/resend-verification

| Field | Value |
|---|---|
| Endpoint | `/auth/resend-verification` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `ResendVerificationRequest { email: string }` |
| Response | `200 ApiResponse<ResendVerificationResponse { emailSent: bool, expiresAtUtc?: DateTime }>` |
| Status Codes | `200, 400` |
| Validation | email required+format; only for unverified accounts |
| Errors | `400 Validation` |
| Pagination/Sorting/Filtering | — |

### 1.8 POST /auth/forgot-password

| Field | Value |
|---|---|
| Endpoint | `/auth/forgot-password` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `ForgotPasswordRequest { email: string }` |
| Response | `200 ApiResponse` (always 200 to avoid enumeration) |
| Status Codes | `200, 400` |
| Validation | email rules |
| Errors | `400 Validation` |
| Pagination/Sorting/Filtering | — |

### 1.9 POST /auth/reset-password

| Field | Value |
|---|---|
| Endpoint | `/auth/reset-password` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` |
| Request | Body `ResetPasswordRequest { token: string (required), newPassword: string (password policy) }` |
| Response | `200 ApiResponse` |
| Status Codes | `200, 400, 401` |
| Validation | token required + newPassword policy |
| Errors | `400 Validation`, `401 InvalidToken` |
| Pagination/Sorting/Filtering | — |

### 1.10 POST /auth/change-password

| Field | Value |
|---|---|
| Endpoint | `/auth/change-password` |
| HTTPMethod | `POST` |
| Authorization | `Authorize` (any authenticated JWT) |
| Request | Body `ChangePasswordRequest { currentPassword: string, newPassword: string (policy), confirmPassword: string (== newPassword) }` |
| Response | `200 ApiResponse` |
| Status Codes | `200, 400, 401` |
| Validation | current required; new policy; confirm match |
| Errors | `400 Validation`, `401` |
| Pagination/Sorting/Filtering | — |

### 1.11 GET /auth/me

| Field | Value |
|---|---|
| Endpoint | `/auth/me` |
| HTTPMethod | `GET` |
| Authorization | `Authorize` (user resolved from JWT `ICurrentUser`) |
| Request | None |
| Response | `200 ApiResponse<UserResponse>` (same shape as 1.1) |
| Status Codes | `200, 401, 404` |
| Validation | — |
| Errors | `401`, `404` user deleted |
| Pagination/Sorting/Filtering | — |

### 1.12 POST /admin/users/{id}/roles

| Field | Value |
|---|---|
| Endpoint | `/admin/users/{id:guid}/roles` |
| HTTPMethod | `POST` |
| Authorization | `Authorize(SuperAdminPolicy)` |
| Request | Body `AssignRoleRequest { role: string }` — C# `sealed record AssignRoleRequest(string Role)` only; `id` comes from route, NOT body. `Role` is `Customer|Admin|SuperAdmin`. |
| Response | `200 ApiResponse<UserResponse>` |
| Status Codes | `200, 400, 401, 403, 404, 409` |
| Validation | guid non-empty; role in known roles |
| Errors | `400 InvalidRole`, `404 UserNotFound`, `409 RoleAlreadyAssigned` |
| Pagination/Sorting/Filtering | — |

### 1.13 POST /admin/users/{id}/unlock

| Field | Value |
|---|---|
| Endpoint | `/admin/users/{id:guid}/unlock` |
| HTTPMethod | `POST` |
| Authorization | `Authorize(SuperAdminPolicy)` |
| Request | Route `id: Guid`, no body |
| Response | `200 ApiResponse<UserResponse>` |
| Status Codes | `200, 401, 403, 404` |
| Validation | guid non-empty |
| Errors | `404 UserNotFound` |
| Pagination/Sorting/Filtering | — |

### 1.14 POST /admin/users/{id}/permissions

| Field | Value |
|---|---|
| Endpoint | `/admin/users/{id:guid}/permissions` |
| HTTPMethod | `POST` |
| Authorization | `Authorize(SuperAdminPolicy)` |
| Request | Body `GrantPermissionRequest { permission: string }` — one catalog value from §0.5 |
| Response | `200 ApiResponse<UserResponse>` (with updated `permissions`) |
| Status Codes | `200, 400, 401, 403, 404, 409` |
| Validation | guid non-empty; permission non-empty and in catalog |
| Errors | `400 InvalidPermission`, `404 UserNotFound`, `409 PermissionAlreadyGranted` |
| Pagination/Sorting/Filtering | — |

### 1.15 DELETE /admin/users/{id}/permissions/{permission}

| Field | Value |
|---|---|
| Endpoint | `/admin/users/{id:guid}/permissions/{permission}` |
| HTTPMethod | `DELETE` |
| Authorization | `Authorize(SuperAdminPolicy)` |
| Request | Route `id: Guid` + `permission: string` |
| Response | `200 ApiResponse<UserResponse>` (idempotent — absent permission still succeeds) |
| Status Codes | `200, 400, 401, 403, 404` |
| Validation | guid non-empty; permission non-empty and in catalog |
| Errors | `400 InvalidPermission`, `404 UserNotFound` |
| Pagination/Sorting/Filtering | — |

### 1.16 GET /admin/users/{id}/permissions

| Field | Value |
|---|---|
| Endpoint | `/admin/users/{id:guid}/permissions` |
| HTTPMethod | `GET` |
| Authorization | `Authorize(users.read)` |
| Request | Route `id: Guid` |
| Response | `200 ApiResponse<string[]>` (granted permission values) |
| Status Codes | `200, 401, 403, 404` |
| Validation | guid non-empty |
| Errors | `404 UserNotFound` |
| Pagination/Sorting/Filtering | — |

### 1.17 GET /admin/users/permissions

| Field | Value |
|---|---|
| Endpoint | `/admin/users/permissions` |
| HTTPMethod | `GET` |
| Authorization | `Authorize(SuperAdminPolicy)` |
| Request | None |
| Response | `200 ApiResponse<string[]>` (full catalog, §0.5) |
| Status Codes | `200, 401, 403` |
| Validation | — |
| Errors | — |
| Pagination/Sorting/Filtering | — |

### 1.18 PUT /auth/profile

| Field | Value |
|---|---|
| Endpoint | `/auth/profile` |
| HTTPMethod | `PUT` |
| Authorization | `Authorize` (any authenticated JWT) |
| Request | Body `UpdateProfileRequest { firstName?: string\|null, lastName?: string\|null, phoneNumber?: string\|null, avatarUrl?: string\|null, preferredLanguage?: string\|null, timeZone?: string\|null }` — all nullable; `null` keeps current value, empty clears optional fields (phone/avatar) |
| Response | `200 ApiResponse<UserResponse>` (same shape as 1.1, plus `permissions: string[]`) |
| Status Codes | `200, 400, 401, 404` |
| Validation | names non-empty/max lengths when supplied; phone ≤30; avatar ≤2048; language ≤10; timezone ≤64 |
| Errors | `400 Validation`, `401`, `404 UserNotFound` (account deleted between login and call) |
| Pagination/Sorting/Filtering | — |

### 1.19 GET /admin/users

| Field | Value |
|---|---|
| Endpoint | `/admin/users` |
| HTTPMethod | `GET` |
| Authorization | `Authorize(users.read)` |
| Request | Query `pageNumber?: int, pageSize?: int, status?: Pending\|Active\|Suspended\|Locked\|Deleted, role?: Customer\|Admin\|SuperAdmin, search?: string (email/first/last substring)` |
| Response | `200 ApiResponse<PagedResult<UserAdminResponse>>` newest-first (`UserAdminResponse` = `UserResponse` + `failedAccessAttemptCount: int, lockoutEndUtc?: DateTime\|null, isLocked: bool`) |
| Status Codes | `200, 400, 401, 403` |
| Validation | `pageNumber>=1, 1<=pageSize<=100`; status/role names case-insensitive, unknown → `400` |
| Errors | `400 ValidationFailed/InvalidRole` |
| Pagination | Yes (defaults `1/20`, max `100`) |
| Sorting | Fixed newest-first (no param) |
| Filtering | Yes (`status`, `role`, `search`) |

### 1.20 GET /admin/users/counts

| Field | Value |
|---|---|
| Endpoint | `/admin/users/counts` |
| HTTPMethod | `GET` |
| Authorization | `Authorize(users.read)` |
| Request | None |
| Response | `200 ApiResponse<UserAdminCountsResponse { total, pending, active, suspended, locked, deleted, customers, admins, superAdmins, unverified, lockedOut: int }>` |
| Status Codes | `200, 401, 403` |
| Validation | — |
| Errors | — |
| Pagination/Sorting/Filtering | — |

### 1.21 GET /admin/users/{id}

| Field | Value |
|---|---|
| Endpoint | `/admin/users/{id:guid}` |
| HTTPMethod | `GET` |
| Authorization | `Authorize(users.read)` |
| Request | Route `id: Guid` |
| Response | `200 ApiResponse<UserAdminResponse>` (same as 1.19 items) |
| Status Codes | `200, 401, 403, 404` |
| Validation | guid non-empty |
| Errors | `404 UserNotFound` |
| Pagination/Sorting/Filtering | — |

> Assigning the `Admin` role (`1.12`) grants nothing by itself — permissions are granted explicitly via `1.14` (amended 2026-10-04). Grants apply to the access token on the user's next login/refresh.

---

## 2. Catalog — Tag `Catalog`

Controller default `[Authorize]`; all `GET` are `[AllowAnonymous]`; all writes `[Authorize(catalog.manage)]` (Phase 21 — was `AdminPolicy`).
Writes: `POST create → 201 CreatedAtAction(Get)+ApiResponse<Guid|Guid[]>` else error; `PUT / POST lifecycle / assign / reorder → 204` on success.
Reads: `200 ApiResponse<T|PagedResult<T>|IReadOnlyCollection<T>>`.

> Catalog Contracts have **zero `DataAnnotations`** (verified by grep — only `IsRequired` property-name false positives). Do not document `[Required]/[StringLength]/[Range]` for Catalog. Constraints below (lengths, patterns, money) are enforced by Fluent `*Rules` + domain invariants → `400 ValidationProblemDetail`; conflict→409, missing→404.
> Nullability is significant: **requests use `string?` nullable** (`Name/Slug/Description/Sku/Value/ImageUrl/AltText` all `string?`), **responses use non-nullable `string`** (`Name/Slug/Sku/AttributeName/AttributeSlug/ImageUrl/Spec Name/Value` are `string`). `Currency` on create-variant is non-nullable `string`. Dates are `DateTime/DateTime?` (not `DateTimeOffset`).
> Enums (distinct, do not conflate): `ProductStatus Draft=0,Active=1,Inactive=2,Archived=3`; `ProductVisibility Visible=0,Hidden=1`; `CategoryStatus Active=0,Inactive=1`; `VariantStatus Active=0,Inactive=1`; `AttributeStatus Active=0,Inactive=1`; `AttributeDataType Text=0,Number=1,Decimal=2,Boolean=3,Date=4,DateTime=5`.
> `ProductResponse` has **both** `Status: ProductStatus` + `Visibility: ProductVisibility`; create/update product has **only** `Visibility` (no `Status`). `CategoryId`: required `Guid` on product create/update/response/list-item, but optional `Guid?` on attribute-definition create/update/response. `ParentId: Guid?` exists only on category create/update/response. `SortOrder: int` exists only on attribute-definition.

### 2.1 Products — `[Route catalog/products]`

#### POST /catalog/products
| Field | Value |
|---|---|
| Endpoint | `/catalog/products` |
| HTTPMethod | `POST` |
| Authorization | `catalog.manage` |
| Request | Body `CreateProductRequest { name?: string|null, slug?: string|null, description?: string|null, categoryId: Guid, visibility: Visible\|Hidden, seo?: SeoRequest { title?: string|null, description?: string\|null, keywords?: string[]\|null } \| null }` — C# all `string?` / `SeoRequest?`; `Keywords` request shape is `string[]?` nullable array (elements non-null when present). |
| Response | `201 ApiResponse<Guid(productId)>` + `Location: /catalog/products/{id}` |
| Status Codes | `201, 400, 401, 403, 404 (category), 409 (slug conflict)` |
| Validation | Name/Slug/Description/Visibility/CategoryId rules above |
| Errors | `400 Validation`, `404/409` domain |
| Pagination/Sorting/Filtering | — |

#### PUT /catalog/products/{id}
| Field | Value |
|---|---|
| Endpoint | `/catalog/products/{id:guid}` |
| HTTPMethod | `PUT` |
| Authorization | `catalog.manage` |
| Request | Route `id` + Body `UpdateProductRequest` (same nullable shape as create: `string?` fields + `SeoRequest?`) |
| Response | `204` |
| Status Codes | `204, 400, 401, 403, 404, 409` |
| Validation | Same as create + id guid |
| Errors | Same |
| Pagination/Sorting/Filtering | — |

#### POST /catalog/products/{id}/activate, /deactivate, /archive, /soft-delete, /restore
| Field | Value |
|---|---|
| Endpoint | `/catalog/products/{id:guid}/activate` (×5: `activate\|deactivate\|archive\|soft-delete\|restore`) |
| HTTPMethod | `POST` |
| Authorization | `catalog.manage` |
| Request | Route `id` only |
| Response | `204` |
| Status Codes | `204, 400, 401, 403, 404, 409 (illegal state transition)` |
| Validation | id guid |
| Errors | `404 NotFound`, `409 Conflict` |
| Pagination/Sorting/Filtering | — |

#### GET /catalog/products/{id}
| Field | Value |
|---|---|
| Endpoint | `/catalog/products/{id:guid}` |
| HTTPMethod | `GET` |
| Authorization | Anonymous |
| Request | Route `id` |
| Response | `200 ApiResponse<ProductResponse { id, name: string, slug: string, description?: string\|null, seo?: SeoResponse { title?: string\|null, description?: string\|null, keywords: string[] (IReadOnlyList<string> non-null, never null) }\|null, status: Draft\|Active\|Inactive\|Archived, visibility: Visible\|Hidden, categoryId: Guid, isDeleted: bool, createdAtUtc: DateTime, deletedAtUtc?: DateTime\|null }>` |
| Status Codes | `200, 400, 404` |
| Validation | id guid |
| Errors | `404` |
| Pagination/Sorting/Filtering | — |

#### GET /catalog/products
| Field | Value |
|---|---|
| Endpoint | `/catalog/products` |
| HTTPMethod | `GET` |
| Authorization | Anonymous |
| Request | Query `pageNumber?: int, pageSize?: int` |
| Response | `200 ApiResponse<PagedResult<ProductResponse>>` |
| Status Codes | `200, 400` |
| Validation | `pageNumber>=1, 1<=pageSize<=100` |
| Errors | `400 Validation` |
| Pagination | Yes (`PagedResult`) |
| Sorting | No |
| Filtering | Implicit non-deleted only |

#### GET /catalog (storefront search)
| Field | Value |
|---|---|
| Endpoint | `/catalog` (override `~/catalog`) |
| HTTPMethod | `GET` |
| Authorization | Anonymous |
| Request | Query `searchTerm?: string, categoryId?: Guid, status?: Draft\|Active\|Inactive\|Archived, visibility?: Visible\|Hidden, sortBy?: Name\|CreatedAtUtc, sortDirection?: Ascending\|Descending, pageNumber?: int, pageSize?: int` |
| Response | `200 ApiResponse<PagedResult<CatalogProductListItem { id, name, slug, description?, categoryId, primaryImage?: CatalogImageSummary }>>` |
| Status Codes | `200, 400` |
| Validation | pagination rules; nulls = no filter/sort (server default) |
| Errors | `400 Validation` |
| Pagination | Yes |
| Sorting | Yes (`sortBy`, `sortDirection`) |
| Filtering | Yes (`searchTerm`, `categoryId`, `status`, `visibility`) |

### 2.2 Categories — `[Route catalog/categories]`

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/catalog/categories` | `POST` | catalog.manage | Body `CreateCategoryRequest { name?: string\|null, slug?: string\|null, description?: string\|null, parentId?: Guid\|null }` — C# `Guid? ParentId` only place `ParentId` exists | `201 ApiResponse<Guid>` + Location | `201,400,401,403,404(parent),409(slug)` | Fluent/domain only, no DataAnnotations | — |
| `/catalog/categories/{id:guid}` | `PUT` | catalog.manage | Route id + Body `UpdateCategoryRequest` (same nullable shape) | `204` | `204,400,401,403,404,409` | same | — |
| `/catalog/categories/{id:guid}/activate` | `POST` | catalog.manage | Route id | `204` | `204,400,401,403,404,409` | id guid | — |
| `/catalog/categories/{id:guid}/deactivate` | `POST` | catalog.manage | Route id | `204` | same | same | — |
| `/catalog/categories/{id:guid}/soft-delete` | `POST` | catalog.manage | Route id | `204` | same | same | — |
| `/catalog/categories/{id:guid}/restore` | `POST` | catalog.manage | Route id | `204` | same | same | — |
| `/catalog/categories/{id:guid}` | `GET` | Anonymous | Route id | `200 ApiResponse<CategoryResponse { id, name: string, slug: string, description?: string\|null, parentId?: Guid\|null, status: Active\|Inactive (no Draft/Archived), isDeleted: bool, createdAtUtc: DateTime, deletedAtUtc?: DateTime\|null }>` | `200,400,404` | — | — |
| `/catalog/categories` | `GET` | Anonymous | Query `pageNumber?, pageSize?` | `200 ApiResponse<PagedResult<CategoryResponse>>` | `200,400` | pagination rules | Pagination yes; Sorting/Filtering no |

### 2.3 Product Variants — `[Route catalog/products/{productId}/variants]`

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/catalog/products/{productId}/variants` | `POST` | catalog.manage | Route `productId` + Body `CreateProductVariantRequest { sku?: string\|null, name?: string\|null, unitPriceAmount: number (decimal non-null), currency: string (non-null) }` | `201 ApiResponse<Guid>` + Location `.../variants/{variantId}` | `201,400,401,403,404(product),409(sku)` | Fluent/domain only | — |
| `/catalog/products/{productId}/variants/batch` | `POST` | catalog.manage | Route `productId` + Body `CreateProductVariantsRequest { variants?: CreateProductVariantRequest[]\|null }` — C# `IReadOnlyCollection<CreateProductVariantRequest>?` nullable wrapper | `201 ApiResponse<Guid[]>` + Location list | same | same per-item | — |
| `/catalog/products/{productId}/variants/{variantId:guid}` | `PUT` | catalog.manage | Route ids + Body `UpdateProductVariantRequest { sku?: string\|null, name?: string\|null, unitPriceAmount?: number\|null, currency?: string\|null, status?: Active(0)\|Inactive(1)\|null }` — price needs amount+currency together; status toggles sellability | `204` | `204,400,401,403,404,409` | Fluent/domain | — |
| `/catalog/products/{productId}/variants/{variantId:guid}` | `DELETE` | catalog.manage | Route ids | `204` | `204,401,403,404` | — | — |
| `/catalog/products/{productId}/variants/{variantId:guid}` | `GET` | Anonymous | Route ids (`productId` scoping, lookup by `variantId`) | `200 ApiResponse<VariantResponse { id, productId, sku: string, name: string, unitPriceAmount: number (decimal), currency: string (ISO-4217), status: Active\|Inactive (no Draft/Archived), isDeleted: bool, createdAtUtc: DateTime, deletedAtUtc?: DateTime\|null }>` | `200,400,404` | — | — |
| `/catalog/products/{productId}/variants` | `GET` | Anonymous | Route `productId` | `200 ApiResponse<VariantResponse[]>` | `200,400,404` | — | No pagination; no sorting/filtering |

### 2.4 Attribute Definitions — `[Route catalog/attribute-definitions]`

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/catalog/attribute-definitions` | `POST` | catalog.manage | Body `CreateAttributeDefinitionRequest { name?: string\|null, slug?: string\|null, description?: string\|null, dataType: Text\|Number\|Decimal\|Boolean\|Date\|DateTime, isRequired: bool, isFilterable: bool, isSearchable: bool, sortOrder: int, categoryId?: Guid\|null }` — `SortOrder`/`CategoryId(Guid?)` only here | `201 ApiResponse<Guid>` | `201,400,401,403,409` | Fluent/domain only | — |
| `/catalog/attribute-definitions/{id:guid}` | `PUT` | catalog.manage | Route id + Body `UpdateAttributeDefinitionRequest` (same 9-field shape) | `204` | `204,400,401,403,404,409` | same | — |
| `/catalog/attribute-definitions/{id:guid}/activate` | `POST` | catalog.manage | Route id | `204` | same | — | — |
| `/catalog/attribute-definitions/{id:guid}/deactivate` | `POST` | catalog.manage | Route id | `204` | same | — | — |
| `/catalog/attribute-definitions/{id:guid}/soft-delete` | `POST` | catalog.manage | Route id | `204` | same | — | — |
| `/catalog/attribute-definitions/{id:guid}/restore` | `POST` | catalog.manage | Route id | `204` | same | — | — |
| `/catalog/attribute-definitions/{id:guid}` | `GET` | Anonymous | Route id | `200 ApiResponse<AttributeDefinitionResponse { id, name: string, slug: string, description?: string\|null, dataType, isRequired: bool, isFilterable: bool, isSearchable: bool, sortOrder: int, categoryId?: Guid\|null, status: Active\|Inactive, isDeleted: bool, createdAtUtc: DateTime, deletedAtUtc?: DateTime\|null }>` | `200,400,404` | — | — |
| `/catalog/attribute-definitions` | `GET` | Anonymous | None | `200 ApiResponse<AttributeDefinitionResponse[]>` | `200` | — | No pagination/sorting/filtering |

### 2.5 Product Attribute Values — `[Route catalog/products/{productId}/attributes]`

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/catalog/products/{productId}/attributes` | `POST` | catalog.manage | Route `productId` + Body `AssignProductAttributeValueRequest { attributeDefinitionId: Guid, value?: string\|null }` — C# `string? Value` | `204` | `204,400,401,403,404,409(already assigned)` | definition must exist; value must match DataType | — |
| `/catalog/products/{productId}/attributes/{attributeDefinitionId:guid}` | `PUT` | catalog.manage | Route ids + Body `UpdateProductAttributeValueRequest { value?: string\|null }` — **single field only; no attributeDefinitionId in body** | `204` | same | same | — |
| `/catalog/products/{productId}/attributes/{attributeDefinitionId:guid}` | `DELETE` | catalog.manage | Route ids | `204` | `204,401,403,404` | — | — |
| `/catalog/products/{productId}/attributes/{attributeDefinitionId:guid}` | `GET` | Anonymous | Route ids | `200 ApiResponse<ProductAttributeValueResponse { id, productId, attributeDefinitionId, attributeName: string (denormalized), attributeSlug: string (denormalized), dataType, value: string (non-null) }>` | `200,404` | — | — |
| `/catalog/products/{productId}/attributes` | `GET` | Anonymous | Route `productId` | `200 ApiResponse<ProductAttributeValueResponse[]>` | `200,404` | — | No pagination/sorting/filtering |

### 2.6 Product Images — `[Route catalog/products/{productId}/images]`

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/catalog/products/{productId}/images` | `POST` | catalog.manage | Route `productId` + Body `AddProductImageRequest { imageUrl?: string\|null, altText?: string\|null, isPrimary: bool, displayOrder: int, metadata?: Dictionary<string,string>\|null }` — metadata is exactly nullable string→string dict, not object | `204` | `204,400,401,403,404` | Fluent/domain only | — |
| `/catalog/products/{productId}/images/{imageId:guid}` | `PUT` | catalog.manage | Route ids + Body `UpdateProductImageRequest { imageUrl?: string\|null, altText?: string\|null, displayOrder: int, metadata?: Dictionary<string,string>\|null }` — **no isPrimary here** | `204` | same | same | — |
| `/catalog/products/{productId}/images/{imageId:guid}` | `DELETE` | catalog.manage | Route ids | `204` | `204,401,403,404` | — | — |
| `/catalog/products/{productId}/images/{imageId:guid}/set-primary` | `POST` | catalog.manage | Route ids | `204` | same | — | — |
| `/catalog/products/{productId}/images/reorder` | `PUT` | catalog.manage | Route `productId` + Body `ReorderProductImagesRequest { orderedImageIds?: Guid[]\|null }` — C# `IReadOnlyCollection<Guid>?` | `204` | `204,400,401,403,404` | ids must match existing set | — |
| `/catalog/products/{productId}/images/{imageId:guid}` | `GET` | Anonymous | Route ids | `200 ApiResponse<ProductImageResponse { id, productId, imageUrl: string, altText?: string\|null, displayOrder: int, isPrimary: bool, metadata?: Dictionary<string,string>\|null, createdAtUtc: DateTime }>` — no UpdatedAt/DeletedAt | `200,404` | — | — |
| `/catalog/products/{productId}/images` | `GET` | Anonymous | Route `productId` | `200 ApiResponse<ProductImageResponse[]>` | `200,404` | — | No pagination; order by `displayOrder` server-side |

### 2.7 Product Specifications — `[Route catalog/products/{productId}/specifications]`

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/catalog/products/{productId}/specifications` | `POST` | catalog.manage | Route `productId` + Body `CreateProductSpecificationRequest { name?: string\|null, value?: string\|null }` | `204` | `204,400,401,403,404` | Fluent/domain only | — |
| `/catalog/products/{productId}/specifications/{specificationId:guid}` | `PUT` | catalog.manage | Route ids + Body `UpdateProductSpecificationRequest { name?: string\|null, value?: string\|null }` | `204` | same | same | — |
| `/catalog/products/{productId}/specifications/{specificationId:guid}` | `DELETE` | catalog.manage | Route ids | `204` | `204,401,403,404` | — | — |
| `/catalog/products/{productId}/specifications/{specificationId:guid}` | `GET` | Anonymous | Route ids | `200 ApiResponse<ProductSpecificationResponse { id, productId, name: string, value: string }>` — both non-null | `200,404` | — | — |
| `/catalog/products/{productId}/specifications` | `GET` | Anonymous | Route `productId` | `200 ApiResponse<ProductSpecificationResponse[]>` | `200,404` | — | No pagination/sorting/filtering |

---

## 3. Cart — Tag `Cart`, `[Route cart][Authorize]`

All responses: `ApiResponse<CartResponse { cartId: Guid, userId?: Guid|null (null = guest), version: long (OCC token, starts 1, ++ on mutation; VersionConflict→refetch+retry), createdAtUtc: DateTimeOffset, updatedAtUtc: DateTimeOffset, items: CartItemResponse[{ productId: Guid, variantId: Guid, sku: string, productName: string, unitPriceAmount: number (decimal), currency: string, quantity: int }] (no LineTotal), isGuest: bool, isAuthenticated: bool }>`.
Guest = `AllowAnonymous` + opaque `cartId` in route (persist in localStorage). Authenticated = JWT user, no id in route.
Requests use `DataAnnotations`: `Add*` = `[Required]VariantId + [Required][Range(0.01,max)]UnitPriceAmount + [Required][StringLength(3,Min 3)]Currency + [Required][Range(1,max)]Quantity`; `Update*` = **only `[Required]VariantId + [Required][Range(1,max)]Quantity (no price/currency)**; `Remove*` = **only `[Required]VariantId**; `MergeCartRequest([Required]Guid GuestCartId)`; `CreateGuestCartRequest` = empty record.

### Guest (anonymous)

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/cart/guest` | `POST` | Anonymous | Body `CreateGuestCartRequest {}` (empty) | `201 ApiResponse<CartResponse>` + `Location: /cart/guest/{id}` | `201, 400` | — | — |
| `/cart/guest/{cartId:guid}` | `GET` | Anonymous | Route `cartId` | `200 ApiResponse<CartResponse>` | `200, 404` | `404 Cart.NotFound` | — |
| `/cart/guest/{cartId:guid}/items` | `POST` | Anonymous | Route `cartId` + Body `AddGuestCartItemRequest { variantId: Guid, unitPriceAmount: number (≥0.01), currency: string (len 3), quantity: int (≥1) }` | `200 ApiResponse<CartResponse>` | `200, 400, 404` | Range/StringLength →400; `VariantNotFound/Unavailable, QuantityLimitExceeded` | — |
| `/cart/guest/{cartId:guid}/items/{variantId:guid}` | `PUT` | Anonymous | Route ids + Body `UpdateGuestCartItemQuantityRequest { variantId: Guid (route wins), quantity: int ≥1 }` — no price/currency | `200 ApiResponse<CartResponse>` | `200, 400, 404` | same | — |
| `/cart/guest/{cartId:guid}/items/{variantId:guid}` | `DELETE` | Anonymous | Route ids | `200 ApiResponse<CartResponse>` | `200, 404` | `404` if cart/item missing | — |
| `/cart/guest/{cartId:guid}` | `DELETE` | Anonymous | Route `cartId` (clear) | `200 ApiResponse<CartResponse>` | `200, 404` | — | — |

### Authenticated (JWT)

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/cart` | `POST` | Authenticated | No body (server resolves user; returns existing if present) | `201 ApiResponse<CartResponse>` + `Location: /cart` | `201, 401` | `401` no JWT | — |
| `/cart` | `GET` | Authenticated | None | `200 ApiResponse<CartResponse>` | `200, 401, 404` | `404` no active cart | — |
| `/cart/items` | `POST` | Authenticated | Body `AddAuthenticatedCartItemRequest { variantId: Guid, unitPriceAmount: number ≥0.01, currency: string (len exactly 3), quantity: int ≥1 }` | `200 ApiResponse<CartResponse>` | `200, 400, 401` | DataAnnotations as above | — |
| `/cart/items/{variantId:guid}` | `PUT` | Authenticated | Route `variantId` + Body `UpdateAuthenticatedCartItemQuantityRequest { variantId: Guid, quantity: int ≥1 }` — no price/currency | `200 ApiResponse<CartResponse>` | `200, 400, 401` | same | — |
| `/cart/items/{variantId:guid}` | `DELETE` | Authenticated | Route `variantId` | `200 ApiResponse<CartResponse>` | `200, 401, 404` | — | — |
| `/cart` | `DELETE` | Authenticated | None (clear) | `200 ApiResponse<CartResponse>` | `200, 401` | — | — |
| `/cart/merge` | `POST` | Authenticated | Body `MergeCartRequest { guestCartId: Guid }` (call after login; atomic Lua merge guest→user + clear source) | `200 ApiResponse<CartResponse>` | `200, 400, 401, 404` | guest cart must exist | — |

---

## 4. Orders — Tag `Orders`, `[Route orders][Authorize]`

### POST /orders/checkout
| Field | Value |
|---|---|
| Endpoint | `/orders/checkout` |
| HTTPMethod | `POST` |
| Authorization | Authenticated (returns `401` if `ICurrentUser` absent). Cart must belong to caller else `403 CartAccessDenied`. |
| Request | Body `CheckoutRequest { cartId: Guid ([Required]), shippingAddress: CheckoutAddressRequest ([Required]), billingAddress: CheckoutAddressRequest ([Required]) }` where `CheckoutAddressRequest { recipientName: string ([Required][StringLength(100)]), line1: string ([Required][StringLength(200)]), line2?: string\|null ([StringLength(200)] only, NOT required), city: string ([Required][StringLength(100)]), region: string ([Required][StringLength(100)]), postalCode: string ([Required][StringLength(20)]), countryCode: string ([Required][StringLength(2,Min 2)] exactly 2, UPPER ISO3166) }` + Header `Idempotency-Key: string (required, trimmed, 1..128, NOT in body)` |
| Response | `200 ApiResponse<CheckoutResponse { orderId: Guid, orderNumber: string (ORD-+12 UPPER), status: string (OrderStatus.ToString: Pending\|Cancelled\|Confirmed — string, not enum), createdAtUtc: DateTimeOffset }>` — status starts `Pending`; saga moves to `Confirmed\|Cancelled` async — poll `GET /orders/{id}`. |
| Status Codes | `200, 400, 401, 403, 404, 409` |
| Validation | `Idempotency-Key` required 1..128; addresses per length rules; cart non-empty; variants revalidated vs Catalog (Missing/Unavailable rejected); single-currency enforced; duplicate variants rejected; pricing from Catalog authoritative (cart price ignored); atomic persist + best-effort cart clear |
| Errors | `400 InvalidIdempotencyKey\|InvalidAddress\|CartEmpty\|VariantUnavailable\|CurrencyMismatch (Validation)`, `401 AuthenticationRequired`, `403 CartAccessDenied (Forbidden)`, `404 CartNotFound`, `409 IdempotencyConflict (same key + different fingerprint; replay same key+body returns original order)` |
| Pagination/Sorting/Filtering | — |

### GET /orders
| Field | Value |
|---|---|
| Endpoint | `/orders` |
| HTTPMethod | `GET` |
| Authorization | Authenticated |
| Request | Query `pageNumber?: int, pageSize?: int, status?: Pending\|Cancelled\|Confirmed (case-insensitive, Phase P2)` |
| Response | `200 ApiResponse<PagedResult<OrderSummaryResponse { orderId: Guid, orderNumber: string, status: string, grandTotal: OrderMoneyResponse { amount: number, currency: string }, itemCount: int, createdAtUtc: DateTimeOffset }>>` newest-first |
| Status Codes | `200, 400, 401` |
| Validation | `pageNumber>=1, 1<=pageSize<=100` else `400 ValidationFailed`; unknown status → `400` |
| Errors | `400 Validation`, `401` |
| Pagination | Yes (defaults `1/20`, max `100`) |
| Sorting | Fixed newest-first (no param) |
| Filtering | Yes (`status`; only caller-owned) |

### GET /orders/{orderId}
| Field | Value |
|---|---|
| Endpoint | `/orders/{orderId:guid}` |
| HTTPMethod | `GET` |
| Authorization | Authenticated + ownership (other-customer id → `404` to prevent IDOR) |
| Request | Route `orderId: Guid` |
| Response | `200 ApiResponse<OrderDetailsResponse { orderId: Guid, orderNumber: string, status: string (not enum), subtotal: OrderMoneyResponse { amount: number, currency: string }, shippingCost: OrderMoneyResponse, discount: OrderMoneyResponse, grandTotal: OrderMoneyResponse, itemCount: int, createdAtUtc: DateTimeOffset, shippingAddress: OrderAddressResponse { recipientName, line1, line2?: string\|null, city, region, postalCode, countryCode }, billingAddress: OrderAddressResponse, lineItems: OrderLineItemResponse[{ productName: string, sku: string, quantity: int, unitPrice: number, unitPriceCurrency: string, lineTotal: number, lineTotalCurrency: string }][] }>` — money is split flat fields on line items, nested `OrderMoneyResponse` on totals |
| Status Codes | `200, 401, 404` |
| Validation | guid |
| Errors | `404 Order.NotFound` |
| Pagination/Sorting/Filtering | — |

### POST /orders/{orderId}/cancel
| Field | Value |
|---|---|
| Endpoint | `/orders/{orderId:guid}/cancel` |
| HTTPMethod | `POST` |
| Authorization | Authenticated + ownership |
| Request | Route `orderId`, no body |
| Response | `200 ApiResponse<OrderDetailsResponse>` (same as above) |
| Status Codes | `200, 401, 404, 409` |
| Validation | must be `Pending`; finalized rejected |
| Errors | `404`, `409 CancelConflict` |
| Pagination/Sorting/Filtering | — |

### 4.5 Admin orders — `[Route admin/orders]` (permission-gated, §0.5)

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/admin/orders` | `GET` | `orders.read` | Query `pageNumber?, pageSize?, status? (Pending\|Cancelled\|Confirmed), customerId?: Guid, search?: string (order-number substring), from?: DateTimeOffset, to?: DateTimeOffset` | `200 ApiResponse<PagedResult<OrderSummaryResponse>>` newest-first | `200,400,401,403` | pagination rules; unknown status / `from>to` → `400 ValidationFailed` | Pagination yes (`1/20 max 100`); Sorting fixed newest-first; Filtering yes |
| `/admin/orders/counts` | `GET` | `orders.read` | None | `200 ApiResponse<AdminOrderCountsResponse { total, pending, confirmed, cancelled: int }>` | `200,401,403` | — | — |
| `/admin/orders/{orderId:guid}` | `GET` | `orders.read` | Route `orderId` (no ownership scoping) | `200 ApiResponse<OrderDetailsResponse>` | `200,401,403,404` | `404 Order.NotFound` | — |
| `/admin/orders/{orderId:guid}/cancel` | `POST` | `orders.cancel` | Route `orderId`, no body (force-cancel any `Pending` order; compensation flows via `order_cancelled` as for customer cancel) | `200 ApiResponse<OrderDetailsResponse>` | `200,401,403,404,409` | must be `Pending`; `404 Order.NotFound`, `409 Order.CancelConflict` (use refund for `Confirmed`) | — |
| `/admin/orders/{orderId:guid}/refund` | `POST` | `orders.refund` | Route `orderId` + Body `RefundOrderRequest { reason: string ([Required][StringLength(500,Min 1)]) }` | `200 ApiResponse<OrderDetailsResponse>` (current order; refund completes async via saga → Stripe → `PaymentRefunded`) | `200,400,401,403,404,409` | reason required 1..500; only paid (`Completed` saga) eligible; `404 Order.NotFound`, `409 Order.RefundConflict` (incl. in-flight duplicate → still `200` idempotent) | — |

---

## 5. Inventory — Tag `Inventory`, `[Route inventory][Authorize]`

Reads require `inventory.read`, writes (`POST /`, `PATCH /{sku}`) require `inventory.manage` (Phase 21 — was a single `AdminPolicy`). SuperAdmin passes both. Inventory is the only module with `DataAnnotations` on requests: `CreateInventoryItemRequest(Sku: [Required(AllowEmptyStrings=false)][StringLength(64)], OnHandQuantity: [Range(0,max)])`, `AdjustStockRequest(Quantity: [Range(1,max)])`. SKU normalized trimmed+uppercased. Dates are `DateTimeOffset`. `InventoryReservationResponse.reservationId` is named `ReservationId` (not `Id`). Status `Pending=0,Committed=1,Released=2,Expired=3`.

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/inventory` | `POST` | `inventory.manage` | Body `CreateInventoryItemRequest { sku: string (required, non-empty, ≤64, trimmed+uppercased, ^[A-Z0-9-_.]+$), onHandQuantity: int (0..max) }` | `201 ApiResponse<InventoryItemResponse { sku, onHandQuantity: int, reservedQuantity: int, availableQuantity (=onHand-reserved, computed), updatedAtUtc }>` + `Location: /inventory/{sku}` | `201,400,401,403,409` | `400 InvalidSku/InvalidQuantity`; `409 DuplicateSku` | — |
| `/inventory/{sku}` | `GET` | `inventory.read` | Route `sku: string` (not guid) | `200 ApiResponse<InventoryItemResponse>` | `200,400,401,403,404` | `400 InvalidSku`; `404 SkuNotFound` | — |
| `/inventory/{sku}` | `PATCH` | `inventory.manage` | Route `sku` + Body `AdjustStockRequest { quantity: int (1..max) }` additive-only (no manual decrement; consumption via reservation commit) | `200 ApiResponse<InventoryItemResponse>` | `200,400,401,403,404` | same | — |
| `/inventory/reservations/{id:guid}` | `GET` | `inventory.read` | Route `id: Guid` | `200 ApiResponse<InventoryReservationResponse { reservationId: Guid, orderId: Guid, sku: string, quantity: int, status: Pending\|Committed\|Released\|Expired, expiresAt: DateTimeOffset, createdAtUtc, updatedAtUtc }>` | `200,400,401,403,404` | `400 InvalidReservationId`; `404` | — (ids never enumerable) |
| `/inventory` | `GET` | `inventory.read` | Query `pageNumber?, pageSize?, search?: string (SKU substring, case-insensitive), maxAvailable?: int (available-quantity ceiling)` | `200 ApiResponse<PagedResult<InventoryItemResponse>>` ordered by SKU | `200,400,401,403` | pagination rules; negative `maxAvailable` → `400 ValidationFailed` | Pagination yes (`1/20 max 100`); Sorting fixed by SKU; Filtering yes |
| `/inventory/counts` | `GET` | `inventory.read` | Query `lowStockThreshold?: int (default 5, inclusive available ceiling)` | `200 ApiResponse<InventoryCountsResponse { totalItems, totalOnHand, totalReserved, lowStockItems, pendingReservations, committedReservations, releasedReservations, expiredReservations: int }>` | `200,400,401,403` | negative threshold → `400 ValidationFailed` | — |
| `/inventory/reservations` | `GET` | `inventory.read` | Query `pageNumber?, pageSize?, status?: Pending\|Committed\|Released\|Expired, sku?: string (exact, normalized), orderId?: Guid` | `200 ApiResponse<PagedResult<InventoryReservationResponse>>` newest-first | `200,400,401,403` | pagination rules; unknown status / bad SKU / empty orderId → `400` | Pagination yes (`1/20 max 100`); Sorting fixed newest-first; Filtering yes |

---

## 6. Payments — Tag `Payments`, `[Route api/payments][Authorize]`

Note inconsistent `api/` prefix (other modules have none).

### GET /api/payments/{paymentId}
| Field | Value |
|---|---|
| Endpoint | `/api/payments/{paymentId:guid}` |
| HTTPMethod | `GET` |
| Authorization | Authenticated any role + ownership (`payment.CustomerId != caller → 404 masked, warning log; Guid.Empty customer skips check`) |
| Request | Route `paymentId: Guid` |
| Response | `200 ApiResponse<{ id: Guid, orderId: Guid, customerId: Guid, amount: number (>0), currency: string (UPPER), status: string (Pending\|Processing\|Succeeded\|Failed\|Cancelled\|Refunded), providerPaymentIntentId?: string (pi_*), provider?: string (stripe), createdAtUtc, updatedAtUtc, version: long }>` (anonymous object, not named DTO) |
| Status Codes | `200, 401, 404` |
| Validation | guid |
| Errors | `404 Payment.NotFound (also on ownership mismatch)` |
| Pagination/Sorting/Filtering | — |

### GET /api/payments/by-order/{orderId}
| Field | Value |
|---|---|
| Endpoint | `/api/payments/by-order/{orderId:guid}` |
| HTTPMethod | `GET` |
| Authorization | Authenticated any role + ownership (`payment.CustomerId != caller → 404 masked`, same as above) |
| Request | Route `orderId: Guid` |
| Response | `200 ApiResponse<PaymentInitializationResponse { paymentId: Guid (for GET /api/payments/{id} polling), orderId: Guid, clientSecret: string (runtime-only, never store), paymentReference?: string\|null (pi_* for support), status: string ("Pending") }>` |
| Status Codes | `200, 400, 401, 404, 409` |
| Validation | empty guid → `400 Payment.OrderIdRequired` |
| Errors | `404 Payment.NotFound` (missing payment OR stranger — masked); `409 Payment.NotReady` (intent not created yet by saga — poll/retry shortly); `409 Payment.NotPayable` (terminal payment: Succeeded/Failed/Cancelled/Refunded); `500 Payment.ProviderUnavailable` (Stripe unreachable — retry) |
| Pagination/Sorting/Filtering | — |
| Frontend note | Read-only: never creates a Stripe intent (saga owns creation). Live secret re-fetched per call — retries reuse the single intent per order. Flow: `checkout (Pending) → GET by-order (until 200) → stripe.confirmCardPayment(clientSecret) → poll GET /orders/{id} until Confirmed/Cancelled`. |

### POST /api/payments/webhook
| Field | Value |
|---|---|
| Endpoint | `/api/payments/webhook` |
| HTTPMethod | `POST` |
| Authorization | `AllowAnonymous` (Stripe servers, not JWT; verified by `Stripe-Signature`) |
| Request | Raw body `string payload` (buffered, `Request.EnableBuffering`) + Header `Stripe-Signature: string (required)` |
| Response | `200 OK empty` on processed AND duplicate (stops Stripe retries); NO `ApiResponse` envelope |
| Status Codes | `200, 400` |
| Validation | header missing →400; payload empty →400; `VerifyAndParse → {eventId,eventType,paymentIntentId,isValid}` invalid →400; `TryProcessAsync → AlreadyProcessed\|Processed` atomic + Outbox |
| Errors | `400 BadRequest empty` (no envelope) |
| Pagination/Sorting/Filtering | — |
| Frontend note | Not called by SPA. SPA uses `Stripe.PublishableKey` + `ClientSecret` (from `PaymentIntentResult`, backend-only) via Stripe.js; poll `GET /api/payments/{id}` / `GET /orders/{id}` for `Succeeded→Order Confirmed`, `Failed→compensate`, `Refunded→post-pay compensation`. |

### GET /api/payments/mine
| Field | Value |
|---|---|
| Endpoint | `/api/payments/mine` |
| HTTPMethod | `GET` |
| Authorization | Authenticated (scoped to caller — never another customer's payments) |
| Request | Query `pageNumber?: int, pageSize?: int, status?: Pending\|Processing\|Succeeded\|Failed\|Cancelled\|Refunded (case-insensitive)` |
| Response | `200 ApiResponse<PagedResult<PaymentAdminResponse { id, orderId, customerId, amount: number, currency: string, status: string, providerPaymentIntentId?: string\|null, provider?: string\|null, createdAtUtc: DateTimeOffset, updatedAtUtc?: DateTimeOffset\|null, version: long }>>` newest-first (same shape as admin view) |
| Status Codes | `200, 400, 401` |
| Validation | `pageNumber>=1, 1<=pageSize<=100`; unknown status → `400 Payment.ValidationFailed` |
| Errors | `400 Validation`, `401` |
| Pagination | Yes (defaults `1/20`, max `100`) |
| Sorting | Fixed newest-first (no param) |
| Filtering | Yes (`status`; only caller-owned) |

### 6.5 Admin payments — `[Route admin/payments]` (permission-gated, §0.5; read-only, no Stripe calls)

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes | Validation/Errors | Pagination/Sorting/Filtering |
|---|---|---|---|---|---|---|---|
| `/admin/payments` | `GET` | `payments.read` | Query `pageNumber?, pageSize?, status?, orderId?: Guid, customerId?: Guid, from?: DateTimeOffset, to?: DateTimeOffset` | `200 ApiResponse<PagedResult<PaymentAdminResponse>>` newest-first | `200,400,401,403` | pagination rules; unknown status / `from>to` → `400 Payment.ValidationFailed` | Pagination yes (`1/20 max 100`); Sorting fixed newest-first; Filtering yes |
| `/admin/payments/stats` | `GET` | `payments.stats` | None | `200 ApiResponse<PaymentAdminStatsResponse { total, pending, processing, succeeded, failed, cancelled, refunded: int, totalsByCurrency: [{ currency: string, succeededCount: int, succeededTotalAmount: number }] }>` | `200,401,403` | — | — |
| `/admin/payments/{paymentId:guid}` | `GET` | `payments.read` | Route `paymentId` (no ownership scoping) | `200 ApiResponse<PaymentAdminResponse>` | `200,401,403,404` | `404 Payment.NotFound` | — |

---

## 7. Health (no auth, no envelope)

| Endpoint | HTTPMethod | Authorization | Request | Response | Status Codes |
|---|---|---|---|---|---|
| `/health/live` | `GET` | None (predicate `tag==app`) | None | `{ status: string, duration: number (ms, property is `duration` not `durationMs`), components: [{ name, status, description, duration }] }` | `200` healthy, `503` otherwise |
| `/health` | `GET` | None (all checks: `app, sql_server[database,infrastructure], redis[cache,infrastructure], rabbitmq[messaging,infrastructure]`) | None | Same shape | `200/503` |

---

## 8. Dashboard — Tag `Dashboard`, `[Route admin/dashboard][Authorize]` (Phase 22: adaptive)

Single read-only snapshot for the post-login landing screen: any authenticated user receives `200`
(never a bare forbidden screen); every section is included only when entitled, otherwise `null`
(objects) or `[]` (recent orders). For admin landing cards + recent-orders widget; drill down via
the §4.5/§6.5 lists. Section gates: `orders`+`recentOrders` ← `orders.read`; `payments` ←
`payments.read` or `payments.stats`; `inventory` ← `inventory.read` (handler skipped when
unauthorized); `catalog` ← `catalog.manage`; `users` ← `users.read`.

| Field | Value |
|---|---|
| Endpoint | `/admin/dashboard` |
| HTTPMethod | `GET` |
| Authorization | Any authenticated JWT (`401` without token) |
| Request | Query `lowStockThreshold?: int (default 5, inclusive available ceiling for the inventory section)` |
| Response | `200 ApiResponse<AdminDashboardResponse { orders?: AdminOrderCountsResponse\|null, payments?: PaymentAdminStatsResponse\|null, inventory?: InventoryCountsResponse\|null, catalog?: CatalogCountsResponse\|null, users?: UserAdminCountsResponse\|null, recentOrders: OrderSummaryResponse[5] (newest-first, empty when unauthorized) }>` |
| Status Codes | `200, 400, 401` |
| Validation | negative threshold → `400 ValidationFailed` (rejected regardless of entitlement) |
| Errors | First failing entitled section's error surfaces (`400/404` as mapped) |
| Pagination/Sorting/Filtering | Fixed (`recentOrders` newest 5; full paging via list endpoints) |

---

## Appendix A — OpenAPI/Swagger export

- `GET /swagger/v1/swagger.json` (Development only, `UseSwagger` + `UseSwaggerUI` gated on `IsDevelopment()`) — single doc `v1: FluxCommerce API`; import into Postman/Orval/NSwag for typed clients.
- `GET /swagger` — UI. Bearer: `Authorization: Bearer <token>`.
- `SecurityRequirementFilter` clears security metadata on `[AllowAnonymous]` ops; `AcceptLanguageFilter` adds optional `Accept-Language` header param on every op.
- No API versioning negotiation; only doc name `v1`. No CORS (`AddCors/UseCors` absent) — SPA on `:4200` needs proxy/gateway or `AddCors/UseCors` before browser use. No rate limiting (`RateLimiter` absent). Verified by grep in `src/FluxCommerce.Api`.
- Pipeline order: `UseExceptionHandler → UseRequestLocalization → Swagger(dev) → CorrelationId → RequestLogging → HttpsRedirection → Authentication → Authorization → MapHealthChecks → MapControllers`.

## Appendix B — Frontend workflows (recommended call order)

1. **Auth:** `POST /auth/register → POST /auth/verify-email (token from email link FrontendBaseUrl) → POST /auth/login (store access 15m in memory, refresh 7d opaque) → GET /auth/me`. Proactive refresh at ~13–14m; on `401` → `POST /auth/refresh` → retry once. `POST /auth/logout` revokes. `forgot → reset` via email token (reset `1h`). `change-password` needs JWT.
2. **Catalog (storefront):** `GET /catalog?searchTerm&categoryId&status&visibility&sortBy&sortDirection&pageNumber&pageSize` for listing; `GET /catalog/products/{id}`, `GET /catalog/products/{id}/variants`, `GET .../images` (order by `displayOrder`), `GET .../attributes|specifications` for PDP. Admin CRUD mirrors same ids with a JWT carrying `catalog.manage`; lifecycle `activate/deactivate/archive/soft-delete/restore` are `POST …→204`.
3. **Cart:** guest keeps `cartId` in localStorage (`POST /cart/guest → POST .../items → PUT/DELETE .../items/{variantId}`). After login: `POST /cart` (get-or-create auth cart) → `POST /cart/merge { guestCartId }` (atomic Lua, clears guest) → use `/cart*` auth endpoints. On `VersionConflict`/concurrency error: `GET` fresh cart and retry. Note cart `unitPriceAmount/currency` is a snapshot — checkout reprices from Catalog.
4. **Checkout → Order → Payment:** `POST /orders/checkout { cartId, shipping, billing } + Idempotency-Key: <uuid>` (generate per checkout attempt, reuse same key+body on retry/network failure; new body = new key, else `409`). Returns `Pending` order → poll `GET /orders/{id}` (or list) until `Confirmed|Cancelled`. Payment status via `GET /api/payments/{paymentId}` (paymentId comes from saga/Stripe flow, not checkout response); Stripe.js uses backend `PublishableKey` + `ClientSecret` (backend-only `PaymentIntentResult`, no HTTP contract). Webhook is Stripe→API only, never SPA.
5. **Errors/i18n:** send `Accept-Language` or `?culture=`; render `detail` (localized) + field `errors{}` map on 400; key logic on `status` (`Identity.*`, `Catalog.*`, `Cart.*`, `Order.*`, `Inventory.*`, `Payment.*` — business errors carry no `extensions.code`, only `detail`). Include `X-Correlation-ID` per flow and echo `X-Correlation-ID/X-Request-ID/traceId` in bug reports. `404` on orders/payments also means “not yours” (IDOR-masked). `403` means missing role/permission (never retry blindly; one refresh is legitimate for fresh grants, then show locked state).
6. **Admin dashboard:** `GET /admin/dashboard` after login (any authenticated user, `200` always) → render cards per non-`null` section + `recentOrders`; drill down via admin lists. Re-read `me.permissions` after any grant flow.

## Appendix C — Enum reference (wire as strings for status, ints or strings per serializer for others)

- `UserStatus`: `Pending|Active|Suspended|Locked|Deleted` (=0..4)
- `ProductStatus`: `Draft|Active|Inactive|Archived`; `ProductVisibility`: `Visible|Hidden`; `CategoryStatus`: `Active|Inactive`; `VariantStatus`: `Active|Inactive`; `AttributeStatus`: `Active|Inactive`; `AttributeDataType`: `Text|Number|Decimal|Boolean|Date|DateTime`
- `CatalogProductSortBy`: `Name|CreatedAtUtc`; `CatalogProductSortDirection`: `Ascending|Descending`
- `OrderStatus` (string): `Pending|Cancelled|Confirmed`; `InventoryReservationStatus`: `Pending|Committed|Released|Expired`; `PaymentStatus` (string): `Pending|Processing|Succeeded|Failed|Cancelled|Refunded`

```ts
// Minimal TypeScript starter (adjust codegen from /swagger/v1/swagger.json)
interface ApiResponse<T> { isSuccess: boolean; message?: string; data?: T }
interface PagedResult<T> { items: T[]; totalCount: number; pageNumber: number; pageSize: number }
interface UserResponse { id: string; email: string; roles: string[]; isEmailVerified: boolean; registeredAtUtc: string; firstName: string; lastName: string; displayName?: string|null; phoneNumber?: string|null; isPhoneVerified: boolean; avatarUrl?: string|null; preferredLanguage: string; timeZone: string; status: string; permissions: string[] }
interface CartResponse { cartId: string; userId?: string|null; version: number; createdAtUtc: string; updatedAtUtc: string; items: { productId: string; variantId: string; sku: string; productName: string; unitPriceAmount: number; currency: string; quantity: number }[]; isGuest: boolean; isAuthenticated: boolean }
```
