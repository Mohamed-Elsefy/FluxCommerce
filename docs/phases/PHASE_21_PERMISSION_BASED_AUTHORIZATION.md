# Phase 21 — Permission-Based Authorization & SuperAdmin Supremacy — Implementation Plan

**Status:** Implemented and green — Phases A–D complete (2026-10-01).
**Completion report:** `docs/completed/PHASE_21_COMPLETION_REPORT.md`
**Decision record:** `docs/adr/016-permission-based-authorization-and-superadmin-supremacy.md`
**Scope:** Identity + API authorization only. No business-logic, saga, or persistence-ownership changes.

---

## 1. Goals

1. `SuperAdmin` passes **every** endpoint (all role policies and all permission policies), except the
   `SuperAdminPolicy`-gated identity bootstrapping surface itself, which stays exact.
2. Replace the coarse `Admin`-role gate on ~40 administrative actions with **action-level permissions**
   (9) that `SuperAdmin` grants/revokes per admin (e.g. one admin reads sales, another manages stock).
3. Assigning the `Admin` role grants the full admin permission bundle explicitly (stored, not implied);
   existing `Admin` accounts start empty and are re-granted manually (user decision — see §8).

## 2. Non-Goals

- No change to `Customer` storefront authorization (`[Authorize]` + ownership masking stay as-is).
- No delegation of user/permission management itself (stays `SuperAdmin`-only).
- No time-series analytics, no frontend implementation (backend contracts only + `frontend-contract` sync).
- No change to cross-module boundaries: composition happens in the API host only.

## 3. Current State (Verified Before Planning)

| Area | Fact | Source |
|---|---|---|
| Policies | `Customer/Admin/SuperAdmin` are exact `RequireRole`; `SuperAdmin` does **not** satisfy `AdminPolicy` | `Api/Extensions/AuthenticationConfiguration.cs`, ADR-015 |
| Gated surface | ~40 `[Authorize(AdminPolicy)]` actions (all Catalog writes, all Inventory incl. P1 lists/counts, P0/P2 admin orders/payments, `admin/dashboard`); `AdminController` + `register-admin` on `SuperAdminPolicy` | controller audit 2026-10-01 |
| Token | JWT carries `Role[]` only; issued by `Login` + `Refresh` via `IJwtProvider.CreateAccessToken(userId, email, roles)` | `JwtProvider.cs`, both handlers |
| Storage | `User._roles: List<string>` single `Users.Roles` column; no permission concept anywhere | `User.cs`, `UserConfiguration.cs` |
| Tests encoding current behavior | `AuthorizationTests` (85) + `CatalogAdminGatingTests` (98) assert `Admin`-role access | test audit |

## 4. Permission Catalog (Action-Level, `area.action`)

| Permission | Grants |
|---|---|
| `orders.read` | `GET /admin/orders`, `GET /admin/orders/{id}`, `GET /admin/orders/counts` |
| `orders.cancel` | `POST /admin/orders/{id}/cancel` |
| `orders.refund` | `POST /admin/orders/{id}/refund` |
| `payments.read` | `GET /admin/payments`, `GET /admin/payments/{id}` |
| `payments.stats` | `GET /admin/payments/stats` |
| `inventory.read` | `GET /inventory`, `GET /inventory/{sku}`, `GET /inventory/reservations`, `GET /inventory/reservations/{id}`, `GET /inventory/counts` |
| `inventory.manage` | `POST /inventory`, `PATCH /inventory/{sku}` |
| `catalog.manage` | All Catalog mutations (products, categories, variants, attributes, images, specs). Reads stay anonymous |
| `dashboard.read` | `GET /admin/dashboard` |

User/permission management itself (`AdminController`, `register-admin`, user lists) stays
`SuperAdmin`-only and is therefore intentionally **not** in the catalog (9 permissions total).

The catalog lives as string constants (single source of truth) in the Identity Domain,
mirroring how `Roles` is defined today. A `GET /admin/permissions` endpoint exposes it
(SuperAdmin-only).

## 5. Endpoint → Policy Mapping (Including P0–P2 Admin Surface)

| Endpoint group | Old policy | New policy |
|---|---|---|
| Catalog mutations (7 controllers) | `AdminPolicy` | `catalog.manage` |
| `InventoryController` reads (incl. P1 lists/counts) | `AdminPolicy` | `inventory.read` |
| `InventoryController` writes (`POST /`, `PATCH /{sku}`) | `AdminPolicy` | `inventory.manage` |
| `AdminOrdersController` list/get/counts | `AdminPolicy` | `orders.read` |
| `POST /admin/orders/{id}/cancel` | `AdminPolicy` | `orders.cancel` |
| `POST /admin/orders/{id}/refund` | `AdminPolicy` | `orders.refund` |
| `AdminPaymentsController` list/get | `AdminPolicy` | `payments.read` |
| `GET /admin/payments/stats` | `AdminPolicy` | `payments.stats` |
| `GET /admin/dashboard` | `AdminPolicy` | `dashboard.read` |
| `AdminController` (roles/unlock/users/permissions) + `register-admin` | `SuperAdminPolicy` | unchanged (exact) |
| Customer + anonymous surface | unchanged | unchanged |

One policy is registered per permission plus the three existing role policies. A single
authorization handler implements the supremacy rule: a `SuperAdmin` role claim succeeds **any**
role/permission requirement; otherwise the required role/permission claim must be present.
`SuperAdminPolicy` itself keeps the exact `RequireRole(SuperAdmin)` semantics.

## 6. Design Details

### 6.1 Storage & Domain

- `User._permissions: List<string>` persisted as a `Users.Permissions` column via the same
  primitive-collection mapping used by `_roles` (`builder.Property(typeof(List<string>), "_permissions")`).
- Domain methods `GrantPermission(string)` / `RevokePermission(string)` with errors
  (`PermissionAlreadyGranted`, unknown permission rejected against the catalog).
- EF migration `AddUserPermissions`. No backfill: existing rows start empty by user decision (§8).

### 6.2 Token & Session Semantics

- New claim type `permission` (one claim per permission; ~9 extra claims — negligible size).
- `IJwtProvider.CreateAccessToken` gains an **optional** `permissions = null` parameter so the
  dozens of existing call sites (production + tests) keep compiling; `Login`/`Refresh` pass
  `user.Permissions`.
- **Declared rule:** permission changes take effect on the next login/refresh (documented in
  `AUTH_FLOW.md`). No token revocation list (out of scope, consistent with current design).

### 6.3 Role Semantics & Bundle

- Assigning `Admin` grants the full admin permission bundle **explicitly stored** on the user
  (all catalog permissions in §4 except none — the bundle is the full set; user management stays
  SuperAdmin-only and is therefore not in any bundle).
- `SuperAdmin` assignment is unchanged (role only; supremacy comes from the handler, not claims).
- Removing the `Admin` role does **not** strip explicitly granted permissions (explicit revoke is
  the SuperAdmin's tool; documented behavior).

### 6.4 Grant/Revoke Surface (SuperAdmin-Only)

| Endpoint | Effect |
|---|---|
| `POST /admin/users/{id}/permissions {permission}` | Grant (idempotent → `409 PermissionAlreadyGranted` on repeat) |
| `DELETE /admin/users/{id}/permissions/{permission}` | Revoke (idempotent success when absent) |
| `GET /admin/users/{id}/permissions` | List a user's permissions |
| `GET /admin/permissions` | List the catalog |

New error codes: `Identity.InvalidPermission` (400), `Identity.PermissionAlreadyGranted` (409).
`Identity.UserNotFound` (404) is reused.

### 6.5 Frontend Contracts

- `UserResponse` + `UserAdminResponse` gain `Permissions: string[]` (additive; numeric enums untouched).
- `frontend-contract/` sync: `API_CONTRACT.md` (new endpoints + 403 semantics per permission),
  `AUTH_FLOW.md` (permission claims + refresh rule), `ERROR_CATALOG.md` (new codes),
  `ENUM_REFERENCE.md` (no enum changes — permissions are strings).

## 7. Implementation Sub-Phases

### Phase A — Supremacy (Independent, Small)

- New requirement type + single `AuthorizationHandler` (succeeds on `SuperAdmin` role, else checks
  the required role claim); re-declare `CustomerPolicy`/`AdminPolicy` through it;
  `SuperAdminPolicy` stays exact `RequireRole`.
- Tests: SuperAdmin passes an `AdminPolicy` endpoint (each admin controller area, sampled),
  non-admin still 403, `SuperAdminPolicy` still rejects plain `Admin`.
- Gates: build `0W/0E`, arch `177/177`, unit green. No behavior change for existing roles yet.

### Phase B — Permission Model

- Catalog constants, `_permissions` + domain methods/errors, EF configuration + migration,
  JWT claim emission (optional param), `Login`/`Refresh` wiring, `me`/`UserAdmin` responses.
- Tests: domain grant/revoke unit tests, handler tests, JWT contains permission claims,
  SQLite + SQL Server translation of the new column (lesson from P1: whole-object access only).
- Gates: `has-pending-model-changes` clean for `IdentityDbContext`, suites green.

### Phase C — Management Surface

- Grant/revoke/list/catalog endpoints on `AdminController` (SuperAdmin-only), new error codes,
  `AUTH_FLOW.md` refresh-rule note, ADR-016 completion section.
- Tests: grant → login → permission claim present; revoke → absent on next login; unknown
  permission → 400; duplicate → 409; non-SuperAdmin → 403.
- Gates: `AdminControllerTests`-style integration coverage, suites green.

### Phase D — Activation (Breaking by Design)

- Re-point the ~40 actions per §5; `AssignRole(Admin)` stores the full bundle;
  update `AuthorizationTests` + `CatalogAdminGatingTests` to the permission model
  (Admin-without-permission → 403; permission-without-Admin-role → 200);
  `API_CONTRACT.md` + `ERROR_CATALOG.md` sync.
- **Operational warning (§8) is part of the completion report.**
- Gates: full `dotnet test` per-suite green; swagger review; completion report in
  `docs/completed/` per repo convention.

## 8. Rollout Warning (Binding)

By user decision there is **no automatic backfill**: the moment Phase D deploys, every existing
`Admin` account loses administrative access until `SuperAdmin` re-grants permissions
(`GET /admin/users` lists the accounts beforehand). Mitigation: deploy A–C first (no behavior
change), extract the admin list, then deploy D inside a maintenance window with the grants
scripted per person (e.g. sales-viewer ← `orders.read + payments.read + payments.stats +
dashboard.read`; stock-manager ← `inventory.read + inventory.manage`).

## 9. Effort Estimate

| Sub-phase | New files | Modified files | Tests touched |
|---|---|---|---|
| A | 2–3 (`Requirement`/`Handler`, tests) | `AuthenticationConfiguration` | +~10 |
| B | ~15 (catalog, domain, EF + migration, JWT, responses, tests) | `JwtProvider`, `Login`/`Refresh`, mappers, `UserConfiguration` + migration, `UserAdminStore` | +~20 |
| C | ~8 (4 commands/handlers, requests, tests) | `AdminController`, DI | +~12 |
| D | 0 new | ~13 controllers, `AssignRole` handler, ~180 auth tests, 3 contract docs | updates |

Total: medium-large. Order A→D is dependency-safe; each sub-phase ships green independently.
