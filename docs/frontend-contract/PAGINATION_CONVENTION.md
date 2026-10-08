# FluxCommerce — Pagination Convention for Frontend

> Standard envelope: `PagedResult<T> { items, totalCount, pageNumber, pageSize }` wrapped in `ApiResponse`.
> Source: `BuildingBlocks.Application.Results.PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int PageNumber, int PageSize)`.
> Wire is camelCase by framework default (`items`, `totalCount`, `pageNumber`, `pageSize`). No `totalPages` / `hasNext` / `hasPrev` from backend — compute client-side.

---

## 1. Envelope

```csharp
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int PageNumber, int PageSize);
```

```ts
interface PagedResult<T> { items: T[]; totalCount: number; pageNumber: number; pageSize: number }
interface ApiResponse<T> { isSuccess: boolean; message?: string | null; data?: T | null }
type PagedApiResponse<T> = ApiResponse<PagedResult<T>>;
```

```json
// GET /catalog/products?pageNumber=2&pageSize=20
{
  "isSuccess": true,
  "message": "Operation completed successfully.",
  "data": { "items": [], "totalCount": 95, "pageNumber": 2, "pageSize": 20 }
}
// Empty result (e.g. GET /catalog with empty searchTerm + no filter)
{
  "isSuccess": true,
  "message": "Operation completed successfully.",
  "data": { "items": [], "totalCount": 0, "pageNumber": 1, "pageSize": 20 }
}
```

Semantics: `items` = this page only (may be `[]`); `totalCount` = total matches across all pages (never page-relative); `pageNumber`/`pageSize` = effective values after defaults (echoed back, use them — not your input — for pager state).

---

## 2. Which endpoints paginate

| Endpoint | Paginated | Query | Ordering |
|---|---|---|---|
| `GET /catalog/products` | Yes | `pageNumber?, pageSize?, searchTerm?, status?, visibility?, categoryId?, sortBy?, sortDirection?` | `sortBy: Name\|CreatedAtUtc` + `sortDirection: Ascending\|Descending`; nulls = `Name` ascending (same as `GET /catalog`) |
| `GET /catalog` (search) | Yes | `searchTerm?, categoryId?, status?, visibility?, sortBy?, sortDirection?, pageNumber?, pageSize?` | `sortBy: Name\|CreatedAtUtc` + `sortDirection: Ascending\|Descending`; nulls = server default |
| `GET /catalog/categories` | Yes | `pageNumber?, pageSize?, searchTerm?, status?` | Fixed `Name` (no sort params) |
| `GET /orders` | Yes | `pageNumber?, pageSize?, status?` | Fixed newest-first (no sort param) |
| `GET /api/payments/mine` | Yes | `pageNumber?, pageSize?, status?` | Fixed newest-first (no sort param) |
| `GET /admin/orders` | Yes | `pageNumber?, pageSize?, status?, customerId?, search?, from?, to?` | Fixed newest-first (no sort param) |
| `GET /admin/payments` | Yes | `pageNumber?, pageSize?, status?, orderId?, customerId?, from?, to?` | Fixed newest-first (no sort param) |
| `GET /inventory` | Yes | `pageNumber?, pageSize?, search?, maxAvailable?` | Fixed by SKU (no sort param) |
| `GET /inventory/reservations` | Yes | `pageNumber?, pageSize?, status?, sku?, orderId?` | Fixed newest-first (no sort param) |
| `GET /admin/users` | Yes | `pageNumber?, pageSize?, status?, role?, search?` | Fixed newest-first (no sort param) |
| `GET /admin/dashboard` | No — fixed windows, full arrays | `lowStockThreshold?: int (default 5, >=0)`, `days?: int (default 30, 1..90)` | Recents fixed newest-5 (`recentOrders/recentPayments/recentUsers`); `dailySeries` trailing `days`, oldest-first, zero-filled; full paging via the list endpoints above |
| `GET /catalog/products/{id}/variants` | No | — | Unspecified collection |
| `GET /catalog/attribute-definitions` | No | — | Unspecified collection |
| `GET /catalog/products/{id}/attributes` | No | — | Unspecified collection |
| `GET /catalog/products/{id}/images` | No | — | `displayOrder` server-side |
| `GET /catalog/products/{id}/specifications` | No | — | Unspecified collection |

Single-resource `GET …/{id}` never paginates. Non-paginated lists return `ApiResponse<T[]>` directly (no `PagedResult` wrapper).

---

## 3. Request: query params, defaults, validation

- Params are optional nullable `int?`: `?pageNumber=2&pageSize=20`. Omit either/both to use defaults.
- Defaults (all three handlers + Orders): `pageNumber 1`, `pageSize 20` (`query.PageNumber ?? 1`, `query.PageSize ?? 20` in `ListProducts/Categories/SearchCatalogProducts` handlers and `GetMyOrders` via `OrderPaginationRules.DefaultPageNumber/DefaultPageSize`).
- Limits: `pageNumber >= 1`; `1 <= pageSize <= 100` (`PaginationRules.MinPageNumber=1, MinPageSize=1, MaxPageSize=100`; `OrderPaginationRules` same + defaults).
- Catalog validates only when present (`ValidatePageNumber/ValidatePageSize` add `errors{pageNumber,pageSize}` → `400 ValidationProblemDetail` with `errors` dict). Orders validates resolved values (`Validate(pageNumber,pageSize)` → `Error.Validation("Order.ValidationFailed")` → `400`).
- `400` examples: `pageNumber=0`, `pageSize=0`, `pageSize=101`. Never send `pageSize=0` to mean “all” — request `pageSize=100` repeatedly instead.
- Dashboard windows (not pagination): `days` default `30`, range `1..90` (outside → `400 Dashboard.ValidationFailed`); `lowStockThreshold` default `5`, negative → `400` — both rejected regardless of entitlement.

```ts
interface PagingQuery { pageNumber?: number; pageSize?: number } // both optional, 1-based
// Filtering on GET /catalog, GET /catalog/products, GET /catalog/categories (product status: 4 values; category status: Active|Inactive only):
interface CatalogSearchQuery extends PagingQuery { searchTerm?: string; categoryId?: string; status?: "Draft"|"Active"|"Inactive"|"Archived"; visibility?: "Visible"|"Hidden"; sortBy?: "Name"|"CreatedAtUtc"; sortDirection?: "Ascending"|"Descending" }
interface ProductListQuery extends PagingQuery { searchTerm?: string; categoryId?: string; status?: "Draft"|"Active"|"Inactive"|"Archived"; visibility?: "Visible"|"Hidden"; sortBy?: "Name"|"CreatedAtUtc"; sortDirection?: "Ascending"|"Descending" }
interface CategoryListQuery extends PagingQuery { searchTerm?: string; status?: "Active"|"Inactive" } // no sortBy/sortDirection
```

---

## 4. Client-side pager math (required — backend sends no helpers)

```ts
const totalPages = Math.ceil(totalCount / pageSize); // 0 when totalCount=0
const hasNext = pageNumber < totalPages;
const hasPrev = pageNumber > 1;
// Out-of-range page (pageNumber > totalPages): 200 with items: [], totalCount unchanged — clamp UI back to totalPages.
function nextPage(p: PagedResult<unknown>): PagingQuery | null {
  const totalPages = Math.ceil(p.totalCount / p.pageSize);
  return p.pageNumber < totalPages ? { pageNumber: p.pageNumber + 1, pageSize: p.pageSize } : null;
}
```

Rules: always render from echoed `data.pageNumber/pageSize/totalCount`, not request values; reset to `pageNumber=1` when filters/sort/search change; keep `pageSize` stable across pages (changing it mid-list skews `totalPages`).

---

## 5. Anti-patterns

- Don’t expect `totalPages/hasNext/cursors` — they don’t exist.
- Don’t treat `items: []` as error — it means empty result or out-of-range page (`200`).
- Don’t send `pageNumber=0` (1-based) or `pageSize>100` (both `400`).
- Don’t add `sortBy/sortDirection` to `GET /orders` or `GET /catalog/categories` — only the product lists (`GET /catalog`, `GET /catalog/products`) support them.
- Don’t wrap non-paginated lists (`variants`, `images`, `attributes`, `specs`, dashboard `recentOrders/recentPayments/recentUsers/dailySeries`) in pager logic — they are full `T[]`.
