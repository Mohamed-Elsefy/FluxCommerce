# FluxCommerce — DTO Reference for Frontend

> Exact request/response shapes per module, copied from `*.Contracts` records.
> C#: `PascalCase` → wire: `camelCase` via ASP.NET Core default serialization (no explicit `[JsonPropertyName]`).
> `DateTime` = Identity + Catalog timestamps. `DateTimeOffset` = Cart + Orders + Inventory timestamps (ISO-8601 strings on wire).
> `DataAnnotations`: **Identity = none**, **Catalog = none** (Fluent/domain only), **Cart / Orders / Inventory = yes** (see attributes below).
> Envelopes: success `ApiResponse{isSuccess,message}` / `ApiResponse<T>{isSuccess,message,data}` (default message `"Operation completed successfully."`); list paging `PagedResult<T>{items,totalCount,pageNumber,pageSize}`; errors `ProblemDetail{type,title,status,detail,instance,traceId,extensions}` / `ValidationProblemDetail{...,errors:{field:[msg]}}`.

See `API_CONTRACT.md` for endpoint × method × auth × status codes; this file is shapes only.

---

## 0. Shared

```csharp
// BuildingBlocks.Application.Results
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int PageNumber, int PageSize);
// BuildingBlocks.Web.Responses
public class ApiResponse { bool IsSuccess; string? Message; }
public class ApiResponse<T> : ApiResponse { T? Data; }
// BuildingBlocks.Web.ProblemDetails / Responses (exact defaults)
public sealed class ProblemDetail { string Type = "about:blank"; string Title = ""; int Status; string Detail = ""; string? Instance; string? TraceId; Dictionary<string,object?> Extensions = []; }
public sealed class ValidationProblemDetail { string Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"; string Title = ""; int Status = 400; string Detail = ""; string? Instance; string? TraceId; Dictionary<string,string[]> Errors = []; }
```

```ts
interface ApiResponse<T> { isSuccess: boolean; message?: string | null; data?: T | null }
interface PagedResult<T> { items: T[]; totalCount: number; pageNumber: number; pageSize: number }
interface ProblemDetail { type: string; title: string; status: number; detail: string; instance?: string | null; traceId?: string | null; extensions?: Record<string, unknown> }
interface ValidationProblemDetail extends ProblemDetail { errors: Record<string, string[]> }
```

```json
// ApiResponse<PagedResult<CatalogProductListItem>>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "items": [], "totalCount": 0, "pageNumber": 1, "pageSize": 20 } }
// 404 ProblemDetail
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4", "title": "Not Found", "status": 404, "detail": "…localized…", "instance": "/orders/…", "traceId": "…", "extensions": {} }
// 400 ValidationProblemDetail
{ "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1", "title": "Validation Failed", "status": 400, "detail": "…", "instance": "/cart/guest/…/items", "traceId": "…", "errors": { "quantity": ["Quantity must be at least 1."] } }
```

---

## 1. Identity (`FluxCommerce.Modules.Identity.Contracts`)

No `DataAnnotations` on any request/response. All request strings are non-nullable `string`.

### 1.1 Requests

```csharp
sealed record RegisterCustomerRequest(string Email, string Password, string FirstName, string LastName);
sealed record RegisterAdminRequest(string Email, string Password, string FirstName, string LastName);
sealed record LoginRequest(string Email, string Password);
sealed record RefreshRequest(string RefreshToken);
sealed record LogoutRequest(string RefreshToken);
sealed record VerifyEmailRequest(string Token);
sealed record ResendVerificationRequest(string Email);
sealed record ForgotPasswordRequest(string Email);
sealed record ResetPasswordRequest(string Token, string NewPassword);
sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
sealed record AssignRoleRequest(string Role); // id comes from route POST /admin/users/{id}/roles, NOT body
sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, string? AvatarUrl, string? PreferredLanguage, string? TimeZone); // all nullable: null keeps, empty clears optional
sealed record GrantPermissionRequest(string Permission); // id comes from route POST /admin/users/{id}/permissions, NOT body
```

```ts
interface RegisterRequest { email: string; password: string; firstName: string; lastName: string }
interface LoginRequest { email: string; password: string }
interface RefreshRequest { refreshToken: string }
interface LogoutRequest { refreshToken: string }
interface VerifyEmailRequest { token: string }
interface ResendVerificationRequest { email: string }
interface ForgotPasswordRequest { email: string }
interface ResetPasswordRequest { token: string; newPassword: string }
interface ChangePasswordRequest { currentPassword: string; newPassword: string; confirmPassword: string }
interface AssignRoleRequest { role: string } // "Customer" | "Admin" | "SuperAdmin"
interface UpdateProfileRequest { firstName?: string | null; lastName?: string | null; phoneNumber?: string | null; avatarUrl?: string | null; preferredLanguage?: string | null; timeZone?: string | null }
interface GrantPermissionRequest { permission: string } // one catalog value, see §0.5 in API_CONTRACT.md
```

Validation (Fluent, → `400`): email format/max 254/lowercased; password `8..128` + upper+lower+digit+special; names required/trimmed; tokens required; `confirmPassword == newPassword`; `role` in known roles.

```json
// POST /auth/register
{ "email": "ada@flux.local", "password": "Test123!", "firstName": "Ada", "lastName": "Lovelace" }
// POST /auth/login
{ "email": "ada@flux.local", "password": "Test123!" }
// POST /admin/users/{id}/roles
{ "role": "Admin" }
```

### 1.2 Responses

```csharp
sealed record LoginResponse(Guid UserId, string Email, IReadOnlyCollection<string> Roles, string AccessToken, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);
sealed record UserResponse(Guid Id, string Email, IReadOnlyCollection<string> Roles, bool IsEmailVerified, DateTime RegisteredAtUtc, string FirstName, string LastName, string? DisplayName, string? PhoneNumber, bool IsPhoneVerified, string? AvatarUrl, string PreferredLanguage, string TimeZone, UserStatus Status, IReadOnlyCollection<string> Permissions);
sealed record UserAdminResponse(Guid Id, string Email, IReadOnlyCollection<string> Roles, bool IsEmailVerified, DateTime RegisteredAtUtc, string FirstName, string LastName, string? DisplayName, string? PhoneNumber, bool IsPhoneVerified, string? AvatarUrl, string PreferredLanguage, string TimeZone, UserStatus Status, int FailedAccessAttemptCount, DateTime? LockoutEndUtc, bool IsLocked, IReadOnlyCollection<string> Permissions);
sealed record UserAdminCountsResponse(int Total, int Pending, int Active, int Suspended, int Locked, int Deleted, int Customers, int Admins, int SuperAdmins, int Unverified, int LockedOut);
sealed record ResendVerificationResponse(bool EmailSent, DateTime? ExpiresAtUtc);
enum UserStatus { Pending = 0, Active = 1, Suspended = 2, Locked = 3, Deleted = 4 }
```

```ts
type UserStatus = "Pending" | "Active" | "Suspended" | "Locked" | "Deleted";
interface LoginResponse { userId: string; email: string; roles: string[]; accessToken: string; refreshToken: string; refreshTokenExpiresAtUtc: string }
interface UserResponse { id: string; email: string; roles: string[]; isEmailVerified: boolean; registeredAtUtc: string; firstName: string; lastName: string; displayName?: string | null; phoneNumber?: string | null; isPhoneVerified: boolean; avatarUrl?: string | null; preferredLanguage: string; timeZone: string; status: UserStatus; permissions: string[] }
interface UserAdminResponse extends UserResponse { failedAccessAttemptCount: number; lockoutEndUtc?: string | null; isLocked: boolean }
interface UserAdminCountsResponse { total: number; pending: number; active: number; suspended: number; locked: number; deleted: number; customers: number; admins: number; superAdmins: number; unverified: number; lockedOut: number }
interface ResendVerificationResponse { emailSent: boolean; expiresAtUtc?: string | null }
```

```json
// ApiResponse<LoginResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "email": "ada@flux.local", "roles": ["Customer"], "accessToken": "<jwt-15m>", "refreshToken": "<opaque-7d>", "refreshTokenExpiresAtUtc": "2026-09-11T10:00:00Z" } }
// ApiResponse<UserResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "id": "…", "email": "ada@flux.local", "roles": ["Customer"], "isEmailVerified": true, "registeredAtUtc": "2026-09-04T10:00:00Z", "firstName": "Ada", "lastName": "Lovelace", "displayName": "Ada Lovelace", "phoneNumber": null, "isPhoneVerified": false, "avatarUrl": null, "preferredLanguage": "en", "timeZone": "UTC", "status": "Active" } }
```

Roles constants (`Identity.Domain.Aggregates.Roles`): `Customer = "Customer"`, `Admin = "Admin"`, `SuperAdmin = "SuperAdmin"`. Permission catalog (`Identity.Domain.Aggregates.PermissionCatalog`, claim type `"permission"`): `orders.read|orders.cancel|orders.refund|payments.read|payments.stats|inventory.read|inventory.manage|catalog.manage|users.read` (`AdminBundle` = the full set, reference only — never auto-applied). JWT claims: `NameIdentifier=userId`, `Email`, `Role[]`, `permission[]`; lifetimes access `15m`, skew `5m`, refresh `7d` rotating, verify `24h`, reset `1h`. Grants are manual-only and apply on next login/refresh; `SuperAdmin` bypasses every gate.

---

## 2. Catalog (`FluxCommerce.Modules.Catalog.Contracts`)

No `DataAnnotations`. Requests use nullable `string?`; responses use non-nullable `string` for names/slugs/skus. Dates are `DateTime`/`DateTime?`.

### 2.1 Requests — product / category / variant

```csharp
sealed record CreateProductRequest(string? Name, string? Slug, string? Description, Guid CategoryId, ProductVisibility Visibility, SeoRequest? Seo);
sealed record UpdateProductRequest(string? Name, string? Slug, string? Description, Guid CategoryId, ProductVisibility Visibility, SeoRequest? Seo);
sealed record SeoRequest(string? Title, string? Description, string[]? Keywords); // nullable array
sealed record CreateCategoryRequest(string? Name, string? Slug, string? Description, Guid? ParentId);
sealed record UpdateCategoryRequest(string? Name, string? Slug, string? Description, Guid? ParentId);
sealed record CreateProductVariantRequest(string? Sku, string? Name, decimal UnitPriceAmount, string Currency); // Currency non-null
sealed record CreateProductVariantsRequest(IReadOnlyCollection<CreateProductVariantRequest>? Variants); // nullable wrapper
sealed record UpdateProductVariantRequest(string? Sku, string? Name, decimal? UnitPriceAmount = null, string? Currency = null, VariantStatus? Status = null); // price needs amount+currency together; status Active/Inactive
```

```ts
interface SeoRequest { title?: string | null; description?: string | null; keywords?: string[] | null }
interface CreateProductRequest { name?: string | null; slug?: string | null; description?: string | null; categoryId: string; visibility: "Visible" | "Hidden"; seo?: SeoRequest | null }
interface CreateCategoryRequest { name?: string | null; slug?: string | null; description?: string | null; parentId?: string | null }
interface CreateProductVariantRequest { sku?: string | null; name?: string | null; unitPriceAmount: number; currency: string }
interface CreateProductVariantsRequest { variants?: CreateProductVariantRequest[] | null }
interface UpdateProductVariantRequest { sku?: string | null; name?: string | null; unitPriceAmount?: number | null; currency?: string | null; status?: 0 | 1 | null } // VariantStatus number on wire
```

```json
// POST /catalog/products
{ "name": "Tee", "slug": "tee", "description": null, "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "visibility": "Visible", "seo": { "title": "Tee", "description": null, "keywords": ["cotton"] } }
// POST /catalog/products/{productId}/variants
{ "sku": "TEE-BLK-M", "name": "Black / M", "unitPriceAmount": 29.99, "currency": "USD" }
```

### 2.2 Requests — attributes / images / specs

```csharp
sealed record CreateAttributeDefinitionRequest(string? Name, string? Slug, string? Description, AttributeDataType DataType, bool IsRequired, bool IsFilterable, bool IsSearchable, int SortOrder, Guid? CategoryId);
sealed record UpdateAttributeDefinitionRequest(string? Name, string? Slug, string? Description, AttributeDataType DataType, bool IsRequired, bool IsFilterable, bool IsSearchable, int SortOrder, Guid? CategoryId);
sealed record AssignProductAttributeValueRequest(Guid AttributeDefinitionId, string? Value);
sealed record UpdateProductAttributeValueRequest(string? Value); // single field only
sealed record AddProductImageRequest(string? ImageUrl, string? AltText, bool IsPrimary, int DisplayOrder, Dictionary<string,string>? Metadata); // exact string->string dict
sealed record UpdateProductImageRequest(string? ImageUrl, string? AltText, int DisplayOrder, Dictionary<string,string>? Metadata); // no IsPrimary
sealed record ReorderProductImagesRequest(IReadOnlyCollection<Guid>? OrderedImageIds);
sealed record CreateProductSpecificationRequest(string? Name, string? Value);
sealed record UpdateProductSpecificationRequest(string? Name, string? Value);
```

```ts
interface CreateAttributeDefinitionRequest { name?: string | null; slug?: string | null; description?: string | null; dataType: "Text" | "Number" | "Decimal" | "Boolean" | "Date" | "DateTime"; isRequired: boolean; isFilterable: boolean; isSearchable: boolean; sortOrder: number; categoryId?: string | null }
interface AssignProductAttributeValueRequest { attributeDefinitionId: string; value?: string | null }
interface UpdateProductAttributeValueRequest { value?: string | null }
interface AddProductImageRequest { imageUrl?: string | null; altText?: string | null; isPrimary: boolean; displayOrder: number; metadata?: Record<string,string> | null }
interface UpdateProductImageRequest { imageUrl?: string | null; altText?: string | null; displayOrder: number; metadata?: Record<string,string> | null }
interface ReorderProductImagesRequest { orderedImageIds?: string[] | null }
```

### 2.3 Responses

```csharp
sealed record ProductResponse(Guid Id, string Name, string Slug, string? Description, SeoResponse? Seo, ProductStatus Status, ProductVisibility Visibility, Guid CategoryId, bool IsDeleted, DateTime CreatedAtUtc, DateTime? DeletedAtUtc);
sealed record SeoResponse(string? Title, string? Description, IReadOnlyList<string> Keywords); // non-null list
sealed record CategoryResponse(Guid Id, string Name, string Slug, string? Description, Guid? ParentId, CategoryStatus Status, bool IsDeleted, DateTime CreatedAtUtc, DateTime? DeletedAtUtc);
sealed record VariantResponse(Guid Id, Guid ProductId, string Sku, string Name, decimal UnitPriceAmount, string Currency, VariantStatus Status, bool IsDeleted, DateTime CreatedAtUtc, DateTime? DeletedAtUtc);
sealed record AttributeDefinitionResponse(Guid Id, string Name, string Slug, string? Description, AttributeDataType DataType, bool IsRequired, bool IsFilterable, bool IsSearchable, int SortOrder, Guid? CategoryId, AttributeStatus Status, bool IsDeleted, DateTime CreatedAtUtc, DateTime? DeletedAtUtc);
sealed record ProductAttributeValueResponse(Guid Id, Guid ProductId, Guid AttributeDefinitionId, string AttributeName, string AttributeSlug, AttributeDataType DataType, string Value); // Value non-null
sealed record ProductImageResponse(Guid Id, Guid ProductId, string ImageUrl, string? AltText, int DisplayOrder, bool IsPrimary, Dictionary<string,string>? Metadata, DateTime CreatedAtUtc);
sealed record ProductSpecificationResponse(Guid Id, Guid ProductId, string Name, string Value); // both non-null
```

### 2.4 ReadModels (storefront search, never POST bodies)

```csharp
sealed record CatalogProductListItem(Guid Id, string Name, string Slug, string? Description, Guid CategoryId, CatalogImageSummary? PrimaryImage);
sealed record CatalogImageSummary(Guid Id, string ImageUrl, string? AltText, int DisplayOrder, bool IsPrimary);
sealed record CatalogVariantSummary(Guid Id, string Sku, string Name, decimal UnitPriceAmount, string Currency);
sealed record CatalogAttributeSummary(Guid AttributeDefinitionId, string Name, string Slug, AttributeDataType DataType, string Value);
sealed record CatalogSpecificationSummary(Guid Id, string Name, string Value);
sealed record CatalogProductDetails(Guid Id, string Name, string Slug, string? Description, SeoResponse? Seo, Guid CategoryId, IReadOnlyCollection<CatalogImageSummary> Images, IReadOnlyCollection<CatalogVariantSummary> Variants, IReadOnlyCollection<CatalogAttributeSummary> Attributes, IReadOnlyCollection<CatalogSpecificationSummary> Specifications);
sealed record PaymentInitializationResponse(Guid PaymentId, Guid OrderId, string ClientSecret, string? PaymentReference, string Status); // GET /api/payments/by-order/{orderId}; ClientSecret runtime-only, never store
sealed record CatalogCategoryTree(Guid Id, string Name, string Slug, string? Description, Guid? ParentId, IReadOnlyCollection<CatalogCategoryTree> Children);
```

### 2.5 Enums

```csharp
enum ProductStatus { Draft = 0, Active = 1, Inactive = 2, Archived = 3 }
enum ProductVisibility { Visible = 0, Hidden = 1 }
enum CategoryStatus { Active = 0, Inactive = 1 }
enum VariantStatus { Active = 0, Inactive = 1 }
enum AttributeStatus { Active = 0, Inactive = 1 }
enum AttributeDataType { Text = 0, Number = 1, Decimal = 2, Boolean = 3, Date = 4, DateTime = 5 }
enum CatalogProductSortBy { Name = 0, CreatedAtUtc = 1 }
enum CatalogProductSortDirection { Ascending = 0, Descending = 1 }
enum CatalogVariantEligibilityStatus { Available = 0, Missing = 1, Unavailable = 2 }
```

### 2.6 Query shapes (Application layer — what the query string maps to)

Not `*.Contracts` but required to build frontend query strings. All `int?` nullable; handler defaults `pageNumber 1`, `pageSize 20`; validate only if present: `pageNumber>=1`, `1<=pageSize<=100` (`PaginationRules`: `MinPageNumber=1, MinPageSize=1, MaxPageSize=100`).

```csharp
sealed record SearchCatalogProductsQuery(string SearchTerm, CatalogProductFilter? Filter = null, CatalogProductSortBy? SortBy = null, CatalogProductSortDirection? SortDirection = null, int? PageNumber = null, int? PageSize = null);
sealed record CatalogProductFilter(Guid? CategoryId = null, ProductStatus? Status = null, ProductVisibility? Visibility = null, bool? IsDeleted = null);
sealed record ListProductsQuery(int? PageNumber = null, int? PageSize = null);
sealed record ListCategoriesQuery(int? PageNumber = null, int? PageSize = null);
sealed record ListProductVariantsQuery(Guid ProductId); // no pagination
sealed record ListAttributeDefinitionsQuery(); // no params, no pagination
```

```ts
interface CatalogSearchQuery { searchTerm?: string; categoryId?: string; status?: "Draft" | "Active" | "Inactive" | "Archived"; visibility?: "Visible" | "Hidden"; sortBy?: "Name" | "CreatedAtUtc"; sortDirection?: "Ascending" | "Descending"; pageNumber?: number; pageSize?: number }
interface CatalogImageSummary { id: string; imageUrl: string; altText?: string | null; displayOrder: number; isPrimary: boolean }
interface CatalogProductListItem { id: string; name: string; slug: string; description?: string | null; categoryId: string; primaryImage?: CatalogImageSummary | null }
```

```json
// GET /catalog?searchTerm=tee&sortBy=Name&sortDirection=Ascending&pageNumber=1&pageSize=20
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "items": [{ "id": "…", "name": "Tee", "slug": "tee", "description": null, "categoryId": "…", "primaryImage": { "id": "…", "imageUrl": "https://cdn/tee.jpg", "altText": "Tee", "displayOrder": 0, "isPrimary": true } }], "totalCount": 1, "pageNumber": 1, "pageSize": 20 } }
// GET /catalog/products/{id}
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "id": "…", "name": "Tee", "slug": "tee", "description": null, "seo": { "title": "Tee", "description": null, "keywords": ["cotton"] }, "status": "Active", "visibility": "Visible", "categoryId": "…", "isDeleted": false, "createdAtUtc": "2026-09-04T10:00:00Z", "deletedAtUtc": null } }
```

---

## 3. Cart (`FluxCommerce.Modules.Cart.Contracts`)

`DataAnnotations` present — see attributes. Timestamps are `DateTimeOffset`. `UserId: Guid?` null = guest.

### 3.1 Requests

```csharp
sealed record AddGuestCartItemRequest([Required] Guid VariantId, [Required][Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal UnitPriceAmount, [Required][StringLength(3, MinimumLength = 3)] string Currency, [Required][Range(1, int.MaxValue)] int Quantity);
sealed record AddAuthenticatedCartItemRequest([Required] Guid VariantId, [Required][Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal UnitPriceAmount, [Required][StringLength(3, MinimumLength = 3)] string Currency, [Required][Range(1, int.MaxValue)] int Quantity);
sealed record UpdateGuestCartItemQuantityRequest([Required] Guid VariantId, [Required][Range(1, int.MaxValue)] int Quantity); // no price/currency
sealed record UpdateAuthenticatedCartItemQuantityRequest([Required] Guid VariantId, [Required][Range(1, int.MaxValue)] int Quantity); // no price/currency
sealed record RemoveGuestCartItemRequest([Required] Guid VariantId);
sealed record RemoveAuthenticatedCartItemRequest([Required] Guid VariantId);
sealed record MergeCartRequest([Required] Guid GuestCartId);
sealed record CreateGuestCartRequest; // empty
```

```ts
interface AddCartItemRequest { variantId: string; unitPriceAmount: number; currency: string; quantity: number }
interface UpdateCartItemQuantityRequest { variantId: string; quantity: number }
interface MergeCartRequest { guestCartId: string }
```

```json
// POST /cart/guest/{cartId}/items  (same for POST /cart/items)
{ "variantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "unitPriceAmount": 29.99, "currency": "USD", "quantity": 2 }
// PUT /cart/guest/{cartId}/items/{variantId}
{ "variantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "quantity": 3 }
// POST /cart/merge
{ "guestCartId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

### 3.2 Responses

```csharp
sealed record CartItemResponse(Guid ProductId, Guid VariantId, string Sku, string ProductName, decimal UnitPriceAmount, string Currency, int Quantity); // no LineTotal
sealed record CartResponse(Guid CartId, Guid? UserId, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, IReadOnlyList<CartItemResponse> Items, bool IsGuest, bool IsAuthenticated);
```

```ts
interface CartItemResponse { productId: string; variantId: string; sku: string; productName: string; unitPriceAmount: number; currency: string; quantity: number }
interface CartResponse { cartId: string; userId?: string | null; version: number; createdAtUtc: string; updatedAtUtc: string; items: CartItemResponse[]; isGuest: boolean; isAuthenticated: boolean }
```

```json
// ApiResponse<CartResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "cartId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "userId": null, "version": 3, "createdAtUtc": "2026-09-04T10:00:00Z", "updatedAtUtc": "2026-09-04T10:05:00Z", "items": [{ "productId": "…", "variantId": "…", "sku": "TEE-BLK-M", "productName": "Tee", "unitPriceAmount": 29.99, "currency": "USD", "quantity": 2 }], "isGuest": true, "isAuthenticated": false } }
```

---

## 4. Orders (`FluxCommerce.Modules.Orders.Contracts`)

`DataAnnotations` present. `Idempotency-Key` header (not body). Timestamps `DateTimeOffset`. `Status: string` (not enum).

### 4.1 Requests

```csharp
sealed record CheckoutAddressRequest([Required][StringLength(100)] string RecipientName, [Required][StringLength(200)] string Line1, [StringLength(200)] string? Line2, [Required][StringLength(100)] string City, [Required][StringLength(100)] string Region, [Required][StringLength(20)] string PostalCode, [Required][StringLength(2, MinimumLength = 2)] string CountryCode);
sealed record CheckoutRequest([Required] Guid CartId, [Required] CheckoutAddressRequest ShippingAddress, [Required] CheckoutAddressRequest BillingAddress);
sealed record RefundOrderRequest([Required][StringLength(500, MinimumLength = 1)] string Reason); // admin refund only
```

```ts
interface CheckoutAddressRequest { recipientName: string; line1: string; line2?: string | null; city: string; region: string; postalCode: string; countryCode: string }
interface CheckoutRequest { cartId: string; shippingAddress: CheckoutAddressRequest; billingAddress: CheckoutAddressRequest }
// + header: { "Idempotency-Key": string } // 1..128 chars, required
```

```json
// POST /orders/checkout + header Idempotency-Key: <uuid>
{ "cartId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "shippingAddress": { "recipientName": "Ada Lovelace", "line1": "1 Flux St", "line2": null, "city": "Berlin", "region": "BE", "postalCode": "10115", "countryCode": "DE" }, "billingAddress": { "recipientName": "Ada Lovelace", "line1": "1 Flux St", "line2": null, "city": "Berlin", "region": "BE", "postalCode": "10115", "countryCode": "DE" } }
```

### 4.2 Responses

```csharp
sealed record CheckoutResponse(Guid OrderId, string OrderNumber, string Status, DateTimeOffset CreatedAtUtc);
sealed record OrderMoneyResponse(decimal Amount, string Currency);
sealed record OrderAddressResponse(string RecipientName, string Line1, string? Line2, string City, string Region, string PostalCode, string CountryCode);
sealed record OrderLineItemResponse(string ProductName, string Sku, int Quantity, decimal UnitPrice, string UnitPriceCurrency, decimal LineTotal, string LineTotalCurrency);
sealed record OrderSummaryResponse(Guid OrderId, string OrderNumber, string Status, OrderMoneyResponse GrandTotal, int ItemCount, DateTimeOffset CreatedAtUtc);
sealed record OrderDetailsResponse(Guid OrderId, string OrderNumber, string Status, OrderMoneyResponse Subtotal, OrderMoneyResponse ShippingCost, OrderMoneyResponse Discount, OrderMoneyResponse GrandTotal, int ItemCount, DateTimeOffset CreatedAtUtc, OrderAddressResponse ShippingAddress, OrderAddressResponse BillingAddress, IReadOnlyList<OrderLineItemResponse> LineItems);
sealed record AdminOrderCountsResponse(int Total, int Pending, int Confirmed, int Cancelled);
```

```ts
interface OrderMoneyResponse { amount: number; currency: string }
interface OrderAddressResponse { recipientName: string; line1: string; line2?: string | null; city: string; region: string; postalCode: string; countryCode: string }
interface OrderLineItemResponse { productName: string; sku: string; quantity: number; unitPrice: number; unitPriceCurrency: string; lineTotal: number; lineTotalCurrency: string }
interface CheckoutResponse { orderId: string; orderNumber: string; status: string; createdAtUtc: string }
interface OrderSummaryResponse { orderId: string; orderNumber: string; status: string; grandTotal: OrderMoneyResponse; itemCount: number; createdAtUtc: string }
interface OrderDetailsResponse { orderId: string; orderNumber: string; status: string; subtotal: OrderMoneyResponse; shippingCost: OrderMoneyResponse; discount: OrderMoneyResponse; grandTotal: OrderMoneyResponse; itemCount: number; createdAtUtc: string; shippingAddress: OrderAddressResponse; billingAddress: OrderAddressResponse; lineItems: OrderLineItemResponse[] }
interface AdminOrderCountsResponse { total: number; pending: number; confirmed: number; cancelled: number }
interface RefundOrderRequest { reason: string } // 1..500 chars, required
```

Query shapes (Application layer): `GetMyOrdersQuery(int? PageNumber, int? PageSize, string? Status = null)` → handler defaults via `OrderPaginationRules`: `DefaultPageNumber=1, DefaultPageSize=20, MinPageNumber=1, MinPageSize=1, MaxPageSize=100` (`pageNumber>=1, 1<=pageSize<=100` else `Error.Validation("Order.ValidationFailed")` → `400`; unknown status → same). `Status: string` values: `Pending|Cancelled|Confirmed` (`OrderStatus.ToString()`).

```json
// POST /orders/checkout → ApiResponse<CheckoutResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "orderId": "…", "orderNumber": "ORD-ABC123DEF456", "status": "Pending", "createdAtUtc": "2026-09-04T10:00:00Z" } }
// GET /orders/{id} → ApiResponse<OrderDetailsResponse> (abbrev.)
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "orderId": "…", "orderNumber": "ORD-ABC123DEF456", "status": "Pending", "subtotal": { "amount": 59.98, "currency": "USD" }, "shippingCost": { "amount": 0, "currency": "USD" }, "discount": { "amount": 0, "currency": "USD" }, "grandTotal": { "amount": 59.98, "currency": "USD" }, "itemCount": 2, "createdAtUtc": "2026-09-04T10:00:00Z", "shippingAddress": { "recipientName": "Ada Lovelace", "line1": "1 Flux St", "line2": null, "city": "Berlin", "region": "BE", "postalCode": "10115", "countryCode": "DE" }, "billingAddress": { "…": "…" }, "lineItems": [{ "productName": "Tee", "sku": "TEE-BLK-M", "quantity": 2, "unitPrice": 29.99, "unitPriceCurrency": "USD", "lineTotal": 59.98, "lineTotalCurrency": "USD" }] } }
```

---

## 5. Inventory (`FluxCommerce.Modules.Inventory.Contracts`)

`DataAnnotations` present. Timestamps `DateTimeOffset`. `ReservationId` (not `Id`).

### 5.1 Requests / responses

```csharp
sealed record CreateInventoryItemRequest([Required(AllowEmptyStrings = false), StringLength(64)] string Sku, [Range(0, int.MaxValue)] int OnHandQuantity);
sealed record AdjustStockRequest([Range(1, int.MaxValue)] int Quantity); // additive only
sealed record InventoryItemResponse(string Sku, int OnHandQuantity, int ReservedQuantity, int AvailableQuantity, DateTimeOffset UpdatedAtUtc); // Available = OnHand - Reserved, computed
sealed record InventoryReservationResponse(Guid ReservationId, Guid OrderId, string Sku, int Quantity, InventoryReservationStatus Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
sealed record InventoryCountsResponse(int TotalItems, int TotalOnHand, int TotalReserved, int LowStockItems, int PendingReservations, int CommittedReservations, int ReleasedReservations, int ExpiredReservations);
enum InventoryReservationStatus { Pending = 0, Committed = 1, Released = 2, Expired = 3 }
```

```ts
interface CreateInventoryItemRequest { sku: string; onHandQuantity: number }
interface AdjustStockRequest { quantity: number }
interface InventoryItemResponse { sku: string; onHandQuantity: number; reservedQuantity: number; availableQuantity: number; updatedAtUtc: string }
interface InventoryReservationResponse { reservationId: string; orderId: string; sku: string; quantity: number; status: "Pending" | "Committed" | "Released" | "Expired"; expiresAt: string; createdAtUtc: string; updatedAtUtc: string }
interface InventoryCountsResponse { totalItems: number; totalOnHand: number; totalReserved: number; lowStockItems: number; pendingReservations: number; committedReservations: number; releasedReservations: number; expiredReservations: number }
```

```json
// POST /inventory
{ "sku": "TEE-BLK-M", "onHandQuantity": 100 }
// PATCH /inventory/{sku}
{ "quantity": 25 }
// GET /inventory/{sku} → ApiResponse<InventoryItemResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "sku": "TEE-BLK-M", "onHandQuantity": 100, "reservedQuantity": 2, "availableQuantity": 98, "updatedAtUtc": "2026-09-04T10:00:00Z" } }
// GET /inventory/reservations/{id} → ApiResponse<InventoryReservationResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "reservationId": "…", "orderId": "…", "sku": "TEE-BLK-M", "quantity": 2, "status": "Pending", "expiresAt": "2026-09-04T10:30:00Z", "createdAtUtc": "2026-09-04T10:00:00Z", "updatedAtUtc": "2026-09-04T10:00:00Z" } }
```

Backend-only read models (Application layer — same fields as responses **plus `Version: long`**; HTTP responses omit `Version`):

```csharp
sealed record InventoryItemReadModel(string Sku, int OnHandQuantity, int ReservedQuantity, int AvailableQuantity, long Version, DateTimeOffset UpdatedAtUtc);
sealed record InventoryReservationReadModel(Guid ReservationId, Guid OrderId, string Sku, int Quantity, InventoryReservationStatus Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version);
```

---

## 6. Payments (`FluxCommerce.Modules.Payments.Contracts.Responses` for admin/customer shapes)

`GET /api/payments/{id}` returns an anonymous object (not a named record); webhook is raw-body, no envelope. The admin/customer list views use the explicit `PaymentAdminResponse` contract below.

```csharp
// GET /api/payments/{paymentId} → ApiResponse<object>:
new { Id: Guid, OrderId: Guid, CustomerId: Guid, Amount: decimal /* >0 */, Currency: string /* UPPER */, Status: string /* Pending|Processing|Succeeded|Failed|Cancelled|Refunded */, ProviderPaymentIntentId: string? /* pi_* */, Provider: string? /* stripe */, CreatedAtUtc: DateTimeOffset, UpdatedAtUtc: DateTimeOffset?, Version: long }
// POST /api/payments/webhook: raw string payload + Stripe-Signature header → 200 empty / 400 empty
// Domain (Payments.Domain.Aggregates.Payment, Payments.Domain.Enums.PaymentStatus) — backend source of the above:
enum PaymentStatus { Pending = 0, Processing = 1, Succeeded = 2, Failed = 3, Cancelled = 4, Refunded = 5 }
// Payment { PaymentId(=Id): Guid, OrderId: Guid, CustomerId: Guid (Guid.Empty if none), Currency: string (UpperInvariant), Amount: decimal, ProviderPaymentIntentId: string?, Provider: string?, ProviderRefundId: string? (set on Refund), CreatedAtUtc: DateTimeOffset, UpdatedAtUtc: DateTimeOffset?, Version: long, Status: PaymentStatus }
// Transitions: Process(Pending→Processing), Succeed(→Succeeded), Fail(→Failed), Cancel(→Cancelled), Refund(Succeeded→Refunded only), CanBePaid() = Pending|Processing
```

```ts
interface PaymentResponse { id: string; orderId: string; customerId: string; amount: number; currency: string; status: "Pending" | "Processing" | "Succeeded" | "Failed" | "Cancelled" | "Refunded"; providerPaymentIntentId?: string | null; provider?: string | null; createdAtUtc: string; updatedAtUtc?: string | null; version: number }
```

```csharp
// GET /api/payments/mine + GET /admin/payments* → ApiResponse<PagedResult<PaymentAdminResponse>>:
sealed record PaymentAdminResponse(Guid Id, Guid OrderId, Guid CustomerId, decimal Amount, string Currency, string Status, string? ProviderPaymentIntentId, string? Provider, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, long Version);
// GET /admin/payments/stats:
sealed record PaymentCurrencyTotalResponse(string Currency, int SucceededCount, decimal SucceededTotalAmount);
sealed record PaymentAdminStatsResponse(int Total, int Pending, int Processing, int Succeeded, int Failed, int Cancelled, int Refunded, IReadOnlyList<PaymentCurrencyTotalResponse> TotalsByCurrency, IReadOnlyList<PaymentCurrencyTotalResponse> RefundedTotalsByCurrency);
```

```ts
interface PaymentAdminResponse { id: string; orderId: string; customerId: string; amount: number; currency: string; status: "Pending" | "Processing" | "Succeeded" | "Failed" | "Cancelled" | "Refunded"; providerPaymentIntentId?: string | null; provider?: string | null; createdAtUtc: string; updatedAtUtc?: string | null; version: number }
interface PaymentCurrencyTotalResponse { currency: string; succeededCount: number; succeededTotalAmount: number }
interface PaymentAdminStatsResponse { total: number; pending: number; processing: number; succeeded: number; failed: number; cancelled: number; refunded: number; totalsByCurrency: PaymentCurrencyTotalResponse[]; refundedTotalsByCurrency: PaymentCurrencyTotalResponse[] }
```

```json
// ApiResponse<PaymentResponse>
{ "isSuccess": true, "message": "Operation completed successfully.", "data": { "id": "…", "orderId": "…", "customerId": "…", "amount": 59.98, "currency": "USD", "status": "Succeeded", "providerPaymentIntentId": "pi_123", "provider": "stripe", "createdAtUtc": "2026-09-04T10:00:00Z", "updatedAtUtc": "2026-09-04T10:01:00Z", "version": 3 } }
```

---

## 7. Headers / query params (not JSON bodies)

```ts
// Checkout idempotency (required on POST /orders/checkout)
interface IdempotencyKeyHeader { "Idempotency-Key": string } // 1..128 chars, trimmed; same key+body = replay, same key+different body = 409
// Auth
interface AuthHeader { Authorization: "Bearer <accessToken-15m>" }
// Localization (every op, optional): providers ?culture= > cookie > Accept-Language
interface LocaleInput { "Accept-Language"?: "en" | "fr" | "de" | "es" | "ar"; culture?: string }
// Tracing (optional in, always echoed out)
interface CorrelationHeaders { "X-Correlation-ID"?: string; } // response echoes X-Correlation-ID + X-Request-ID (both Guid:N if generated)
// Paging (catalog list/search + GET /orders + admin lists + GET /api/payments/mine)
interface PagingQuery { pageNumber?: number; pageSize?: number } // >=1, 1..100; defaults 1/20
// Dashboard fixed windows (NOT PagedResult — see §8): recents are fixed newest-5 full arrays,
// dailySeries is a days-windowed full array (no pageNumber/pageSize on either)
interface DashboardQuery { lowStockThreshold?: number; days?: number } // threshold default 5 (>=0); days default 30, 1..90
// Health (no envelope)
interface HealthResponse { status: string; duration: number; components: { name: string; status: string; description: string | null; duration: number }[] }
```

---

## 8. Dashboard (`FluxCommerce.Api.Models` — host composition, no new persistence)

`GET /admin/dashboard?lowStockThreshold=&days=` composes the per-module contracts above. Section
shapes come from their module sections (§1/§4/§5/§6) **except** `CatalogCountsResponse`,
`DailySeriesPointResponse`/`DailyRevenueResponse`, which are host-local (defined below).
`dailySeries.revenue` counts **Succeeded payments only**, grouped per currency (refunds stay in
`payments.refundedTotalsByCurrency`, never netted server-side).

```csharp
sealed record AdminDashboardResponse(
    AdminOrderCountsResponse? Orders,
    PaymentAdminStatsResponse? Payments,
    InventoryCountsResponse? Inventory,
    CatalogCountsResponse? Catalog,
    UserAdminCountsResponse? Users,
    IReadOnlyList<OrderSummaryResponse> RecentOrders, // newest 5, empty when unauthorized
    IReadOnlyList<PaymentAdminResponse> RecentPayments, // newest 5, empty without payments.read (stats-only viewers get stats but no list)
    IReadOnlyList<UserAdminResponse> RecentUsers, // newest 5, empty without users.read
    IReadOnlyList<DailySeriesPointResponse> DailySeries); // trailing days, oldest-first, zero-filled; empty unless orders.read or payments.read/stats
// Daily series (host composition, Phase 23 F3):
sealed record DailyRevenueResponse(string Currency, decimal Amount);
sealed record DailySeriesPointResponse(string Date, int Orders, IReadOnlyList<DailyRevenueResponse> Revenue); // Date "yyyy-MM-dd" (UTC)
```

```ts
interface AdminDashboardResponse { orders?: AdminOrderCountsResponse | null; payments?: PaymentAdminStatsResponse | null; inventory?: InventoryCountsResponse | null; catalog?: CatalogCountsResponse | null; users?: UserAdminCountsResponse | null; recentOrders: OrderSummaryResponse[]; recentPayments: PaymentAdminResponse[]; recentUsers: UserAdminResponse[]; dailySeries: DailySeriesPointResponse[] }
interface DailyRevenueResponse { currency: string; amount: number }
interface DailySeriesPointResponse { date: string; orders: number; revenue: DailyRevenueResponse[] }
```

```csharp
// GET /admin/dashboard catalog section:
sealed record CatalogCountsResponse(int TotalProducts, int ActiveProducts, int TotalCategories, int TotalVariants);
```

```ts
interface CatalogCountsResponse { totalProducts: number; activeProducts: number; totalCategories: number; totalVariants: number }
```
