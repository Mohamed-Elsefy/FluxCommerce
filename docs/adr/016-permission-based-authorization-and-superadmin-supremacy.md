# ADR-016: Permission-Based Authorization & SuperAdmin Supremacy

## Status

Accepted (2026-10-01) — Decisions locked by user on 4 points (action-level granularity,
manual grants, SuperAdmin-only management, no backfill).

**Amendment (2026-10-04):** D5 re-decided by user — no automatic grants at all (see amended D5).
All other decisions stand.

## Context

Phase 1 + ADR-015 delivered role-based authorization as a platform capability
(`PROJECT_CONTEXT.md:6.1` `Customer/Admin/SuperAdmin`, `ARCHITECTURE.md:74` `ICurrentUser`) with:

*   Three exact policies in `Api/Extensions/AuthenticationConfiguration.cs:54-59`
    (`Customer`/`Admin`/`SuperAdmin`, each `RequireRole(Roles.*)`). Per ADR-015 §D1-consequences,
    a `SuperAdmin` does **not** satisfy `AdminPolicy` — supremacy was explicitly deferred.
*   ~40 administrative actions gated on the single coarse `AdminPolicy` (all Catalog mutations, the
    whole `InventoryController` including the P1 lists/counts, the P0/P2 admin orders/payments
    controllers, `admin/dashboard`), verified by the 2026-10-01 controller audit.
*   JWTs carrying `Role[]` claims only (`Infrastructure/Security/JwtProvider.cs:23-43`), issued by
    `Login` + `Refresh` handlers; `CurrentUser` exposing `UserId/Email/Roles/IsAuthenticated`.
*   `User._roles: List<string>` in a single `Users.Roles` column (`UserConfiguration.cs:83-84`);
    no permission concept exists anywhere in `src/`.

The product now needs operational delegation: one admin reads sales while another manages stock,
granted per person by `SuperAdmin` — which a three-role model cannot express — plus the standing
gap that `SuperAdmin` cannot reach `AdminPolicy` endpoints without holding both roles.

## Decision

Five interlocked decisions (user-confirmed):

### D1 — `SuperAdmin` passes every role/permission policy (supremacy)

*   Introduce one requirement type consumed by all role and permission policies, evaluated by a
    single `AuthorizationHandler`: presence of the `SuperAdmin` role claim succeeds **any**
    requirement immediately; otherwise the specifically required role/permission claim must be present.
*   `CustomerPolicy` and `AdminPolicy` are re-declared through this requirement (so supremacy covers
    storefront and admin surfaces alike). `SuperAdminPolicy` keeps exact `RequireRole(SuperAdmin)`
    semantics — supremacy never needs to "prove" itself to its own gate.
*   No controller changes are required for this decision alone; it is a central policy-layer change.

### D2 — Action-level permission catalog (~12), `area.action` strings

*   New single-source-of-truth constants in the Identity Domain (mirroring `Roles`):
    `orders.read`, `orders.cancel`, `orders.refund`, `payments.read`, `payments.stats`,
    `inventory.read`, `inventory.manage`, `catalog.manage`, `dashboard.read`, `users.read`
    (full table with endpoint mapping in `docs/phases/PHASE_21_PERMISSION_BASED_AUTHORIZATION.md` §4–§5).
*   One authorization policy per permission (`RequireClaim("permission", ...)`, evaluated through
    the D1 handler so supremacy applies uniformly). Reads and mutations are split wherever the
    operational need exists (`inventory.read` vs `inventory.manage`, `payments.read` vs
    `payments.stats`); Catalog reads stay anonymous.

### D3 — Explicit per-user permission storage, no implied rights

*   `User._permissions: List<string>` persisted as a `Users.Permissions` column using the same
    primitive-collection mapping as `_roles` (deliberate duplication, same rationale as ADR-015 §D5).
*   Domain `GrantPermission`/`RevokePermission` with errors (`PermissionAlreadyGranted`,
    unknown permission rejected against the catalog); EF migration `AddUserPermissions`.
*   Enforcement reads claims only — the database is consulted at login/refresh (claim issuance),
    never per request (keeps the request path stateless, consistent with current JWT design).

### D4 — JWT `permission` claims + refresh-boundary rule

*   `IJwtProvider.CreateAccessToken` gains an **optional** `permissions = null` parameter (existing
    production + test call sites keep compiling); `Login`/`Refresh` pass `user.Permissions`.
*   Declared rule: grant/revoke takes effect on the next login/refresh. No revocation list
    (out of scope, consistent with the current opaque-refresh-token design).
*   `UserResponse` + `UserAdminResponse` gain additive `Permissions: string[]` so the frontend can
    render per-permission UI without a second call.

### D5 — No automatic grants; every permission is granted manually (amended 2026-10-04)

*   Neither `register-admin` nor `AssignRole(Admin)` grants anything: a newly created or newly
    promoted admin starts permission-less, and `SuperAdmin` grants each permission explicitly
    afterwards through the Phase C surface. (The original "Admin-as-bundle" decision is superseded.)
*   `PermissionCatalog.AdminBundle` is kept purely as the reference full set (e.g. for UIs offering
    a "grant all" shortcut and for tests minting fully-privileged tokens) — it is **not** applied
    automatically anywhere in production code.
*   Removing the `Admin` role does not strip explicitly granted permissions (explicit revoke is
    the tool). No automatic backfill: existing `Admin` accounts lose admin access at activation
    until re-granted manually (user decision; operational warning is part of the Phase 21 completion
    report, including the per-persona grant recipes).

## Consequences

Positive:
*   Least privilege becomes expressible per admin (`sales-viewer` vs `stock-manager` personas).
*   Supremacy removes the ADR-015 dual-role workaround (`[SuperAdmin,Admin]` assignment no longer
    needed for admin UI access).
*   Enforcement stays at the middleware boundary (thin controllers preserved); request path stays
    stateless (claims only); module boundaries untouched (host composes module read paths as today).

Negative / Trade-offs:
*   **Breaking by design (D5):** Phase D activation locks out current admins until manual re-grant —
    requires a maintenance window with the account list extracted beforehand.
*   ~180 existing authorization tests (`AuthorizationTests`, `CatalogAdminGatingTests`) assert
    `Admin`-role access and must be rewritten to the permission model in Phase D.
*   `Users.Permissions` duplicates the `_roles` storage pattern (accepted: same fixed-set rationale
    as ADR-015 §D5; normalization deferred until permission count justifies a join table).
*   Claim snapshot semantics: a demoted admin keeps rights until token expiry (≤15 min access token;
    refresh rotation picks up the revocation) — documented, not fixed, in this phase.

## Alternatives Considered

*   **Keep exact roles, add `SuperAdmin` to every `RequireRole`** (`RequireRole(Admin, SuperAdmin)`)
    — rejected: fixes supremacy but cannot express per-admin granularity at all.
*   **Module-level permissions (~6)** — rejected per user decision; cannot split stock-read from
    stock-write, which is an explicit requirement.
*   **Role-implies-bundle at check time (no stored permissions)** — rejected: makes per-person
    restriction impossible without revocation markers, which are strictly more complex than stored sets.
*   **Per-request database permission check (no claims)** — rejected: adds a DB round-trip to every
    admin call and couples the API host to the Identity DbContext, violating module isolation.
*   **Separate `UserPermissions` join table now** — rejected as over-engineering for ~12 fixed
    permissions; deferred with the same justification as ADR-015's `UserRoles` deferral.
*   **Automatic backfill migration for existing admins** — rejected per user decision (manual re-grant
    preferred for a conscious per-person review).

## References

*   `PROJECT_CONTEXT.md:446` §6.1 Identity & Roles; `ARCHITECTURE.md:21` Secrets, §74 `ICurrentUser`
*   `docs/adr/015-identity-roles-authorization-and-seeding.md` (D1 hierarchy, D5 lookup-table pattern)
*   `docs/phases/PHASE_21_PERMISSION_BASED_AUTHORIZATION.md` (catalog, endpoint mapping, sub-phases A–D)
*   `src/FluxCommerce.Api/Extensions/AuthenticationConfiguration.cs` (current exact policies)
*   `src/FluxCommerce.Api/Controllers/**` (~40 `AdminPolicy` actions audited 2026-10-01)
