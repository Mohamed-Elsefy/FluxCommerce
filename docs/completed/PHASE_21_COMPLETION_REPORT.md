# Phase 21 — Permission-Based Authorization & SuperAdmin Supremacy — Completion Report

**Date:** 2026-10-01
**Scope:** Sub-phases A (supremacy), B (permission model), C (management surface), D (activation)
**Decision record:** `docs/adr/016-permission-based-authorization-and-superadmin-supremacy.md`
**Plan:** `docs/phases/PHASE_21_PERMISSION_BASED_AUTHORIZATION.md`

---

## 1. Executive Summary

The coarse `Admin`-role gate is replaced by 9 action-level permissions granted per admin by
`SuperAdmin`, and `SuperAdmin` now passes every role/permission gate. All four sub-phases shipped
green with no behavior change outside authorization:

- **Build:** `dotnet build FluxCommerce.slnx -c Debug` → **0 warnings, 0 errors**
  (`TreatWarningsAsErrors=true`).
- **Architecture:** **178/178** (177 baseline + 1 new: no coarse `AdminPolicy` on controllers;
  `CatalogMutations` rule re-pointed to `catalog.manage`).
- **Unit:** all green, including 5 supremacy handler tests, 9 domain permission tests, 3 JWT claim
  tests, 10 grant/revoke handler tests, 2 bundle-grant tests.
- **Integration:** all green except 3 pre-existing environmental email-link failures and 1
  pre-existing SQLite concurrency flake (both reproduced on the clean tree during Phase D
  verification).
- **EF model:** `AddUserPermissions` migration; `has-pending-model-changes` clean for
  `IdentityDbContext`. No backfill by design (see §5).

## 2. What Changed (Per Sub-Phase)

### Phase A — Supremacy

- `Api/Authorization/RoleOrSuperAdminRequirement.cs` + `RoleOrSuperAdminHandler.cs`: a `SuperAdmin`
  role claim succeeds any role requirement; otherwise the required role claim must be present.
- `CustomerPolicy`/`AdminPolicy` re-declared through the requirement; `SuperAdminPolicy` stays an
  exact `RequireRole` (verified by the unchanged `AdminController_ShouldRequire_SuperAdminPolicy`
  + `RegisterAdmin_ShouldRequire_SuperAdminPolicy` arch tests).
- Tests: 5 handler unit tests + 5 `SuperAdminSupremacyTests` integration tests (later reworked to
  the permission model in Phase D).

### Phase B — Permission Model

- `Identity.Domain/Aggregates/PermissionCatalog.cs`: 9 permissions, `ClaimType = "permission"`,
  `All`/`AdminBundle`/`IsDefined` (renamed from `Permissions` after a name collision with the
  `User.Permissions` property was caught at compile time).
- `User._permissions` + `GrantPermission`/`RevokePermission` + `UserErrors.PermissionAlreadyGranted`;
  `Users.Permissions` column via the established primitive-collection mapping.
- `IJwtProvider.CreateAccessToken` optional `permissions` parameter (all existing call sites
  untouched); `Login`/`Refresh` emit claims; `UserResponse`/`UserAdminResponse` carry
  `Permissions[]`; `UserAdminStore` projects the column (translation proven on SQLite).
- 3 localization keys × 5 satellites (`Validation.Fields.Permission/UserId`,
  `Validation.Permission.Invalid`) to preserve full parity.
- `AUTH_FLOW.md`: permission claims + refresh-boundary rule.

### Phase C — Management Surface (SuperAdmin-Only)

- `POST /admin/users/{id}/permissions` (409 on repeat), `DELETE .../{permission}` (idempotent),
  `GET .../permissions`, `GET /admin/users/permissions` (catalog).
- New codes `Identity.InvalidPermission` (400) / `Identity.PermissionAlreadyGranted` (409).
- Tests: 10 handler unit tests + 7 `AdminPermissionsTests` integration tests, including
  grant → login → JWT-claim end-to-end proof of the refresh boundary.

### Phase D — Activation (Breaking by Design)

- ~40 actions re-pointed: Catalog writes → `catalog.manage`; Inventory reads/writes →
  `inventory.read`/`inventory.manage` (class-level gate split into method-level);
  admin orders → `orders.read/cancel/refund`; admin payments → `payments.read/stats`;
  dashboard → `dashboard.read`. User/permission management stays `SuperAdmin`-only.
- `AssignRole(Admin)` + `RegisterAdmin` store the full bundle explicitly (idempotent ensure).
- Test rework: `CatalogApiTestBase` mints `catalog.manage`; `CatalogAdminGatingTests` asserts the
  permission model (+2 new negative tests); `SuperAdminSupremacyTests` rewritten (8 tests incl.
  read-vs-manage split and scoped-admin proofs); `AdminOrdersListTests`/`AdminDashboardTests`
  mint the bundle (+1 scoped-admin test); 2 bundle unit tests.
- New arch test `NoController_ShouldRequire_CoarseAdminPolicy` pins the migration.
- Docs: `API_CONTRACT.md` (§0.4/§0.5/§1.14–1.17, inventory split, variant price fields),
  `ERROR_CATALOG.md` (2 new codes).

## 3. Verification Matrix

| Suite | Result | Notes |
|---|---|---|
| Build | 0W/0E | Full slnx |
| Architecture | 178/178 | +1 new pin test, 1 re-pointed |
| Unit | all green | +~30 new across A–D |
| Integration (targeted) | all green | supremacy, permissions, users, orders, dashboard, inventory, catalog gating, authz neighbors |
| Integration (full) | green except 4 pre-existing | 3 email-link env failures + 1 SQLite concurrency flake, all reproduced on clean tree |
| EF | clean | `AddUserPermissions` applied to snapshot; no pending changes |

## 4. Known Non-Defects Found Along the Way

- PowerShell bulk file rewrites strip the (absent) BOM/line-endings: the catalog re-point was
  verified byte-clean via `git diff` (attribute lines only). Lesson: prefer the `edit` tool for
  surgical replacements.
- `EF.Property<List<string>>` projection pattern and whole-object comparisons remain the only
  approved LINQ shapes for converted/collection members (P1 lesson re-confirmed; no new shapes
  introduced).

## 5. Rollout Warning (Binding, per User Decision)

**No automatic backfill was implemented.** The moment Phase D deploys, every existing `Admin`
account loses administrative access until `SuperAdmin` re-grants permissions. Required sequence:

1. Deploy A–C first (no behavior change possible).
2. Extract the admin list (`GET /admin/users`, SuperAdmin).
3. Deploy D inside a maintenance window with per-persona grants scripted, e.g.:
   - sales-viewer ← `orders.read + payments.read + payments.stats + dashboard.read`
   - stock-manager ← `inventory.read + inventory.manage`
   - catalog-manager ← `catalog.manage`
   - full-admin ← all 9 (or `POST /admin/users/{id}/roles` `Admin`, which stores the bundle)
4. Verify by logging in as each admin (permissions land on the next login/refresh).

## 6. Follow-Ups (Not in Scope)

- Time-series dashboard endpoints (provider-portable date grouping still open).
- `frontend-contract` full P0–P2 endpoint documentation (only auth-relevant deltas synced here).
- Token revocation list for instant demotion (documented as accepted behavior: ≤15 min window).
- `UserRoles`/`UserPermissions` join-table normalization if the fixed sets ever grow (same
  deferral rationale as ADR-015).
