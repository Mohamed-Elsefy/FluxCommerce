# Phase 22 — Adaptive Dashboard & User-Management Delegation — Implementation Plan

**Status:** Planned (user-confirmed decisions, 2026-10-04)
**Decision records:** `docs/adr/016-permission-based-authorization-and-superadmin-supremacy.md`
(D1/D2/D4/D5), `docs/adr/017-user-management-delegation.md` (users.* family, escalation guards)
**Prerequisite:** Phase 21 A–D complete (permission model, management surface, activation).
**Scope:** Dashboard gating remodel + catalog counts + `users.*` delegation. No saga, persistence-ownership,
or token-lifetime changes.

---

## 1. Problem

The admin dashboard (`GET /admin/dashboard`) is the post-login landing screen, but it is gated by
a single `dashboard.read` permission: users without it are bounced to a forbidden screen. The
required behavior is **open the screen for every authenticated user and filter the data by
permissions** — e.g. a `catalog.manage` holder receives catalog data, a sales viewer receives
sales data, and a permission-less user receives an empty (not forbidden) snapshot.

Separately, user management is currently `SuperAdmin`-only, with no delegatable read path. Support
work needs a readable users surface without handing out full control.

## 2. Goals

1. `GET /admin/dashboard` requires authentication only; every section is included **iff** the
   caller is entitled to it, otherwise `null` (objects) or `[]` (recent orders). No `403` for
   missing section permissions.
2. New catalog section backed by a lightweight counts query (products total/active + categories
   total), gated by `catalog.manage`.
3. New `users.*` permission family (`users.read`, `users.manage`, `admins.create`) so user
   administration itself becomes delegatable — with explicit anti-escalation guards.
4. `dashboard.read` is deleted from the catalog (9 → 8 → 11, see §4): it has no data of its own
   once its gate is gone, and keeping it would create two paths to the same data.

## 3. Non-Goals

- No change to saga, outbox, JWT lifetimes, or the refresh-boundary rule (grants still land on the
  next login/refresh).
- No time-series dashboard endpoints (still open, same provider-portability caveat as before).
- No per-entity catalog splits, no dashboard-section splits beyond §5.
- No token revocation list (accepted ≤15 min window stands).

## 4. Permission Catalog Delta

| Change | Value | Effect |
|---|---|---|
| Delete | `dashboard.read` | Gate removed with the dashboard remodel; existing stored grants become inert dead data (validation only checks new grants) |
| Add | `users.read` | View users list/counts/details, a user's permissions |
| Add | `users.manage` | Unlock, assign roles, grant/revoke permissions — under §6 guards |
| Add | `admins.create` | `POST /auth/register-admin` (creates a permission-less admin) |

`PermissionCatalog.AdminBundle` follows `All` automatically, so a "full admin" keeps working.
`GET /admin/users/permissions` (catalog listing) stays `SuperAdmin`-only: `users.read` holders see
a user's permission *values* but not the catalog document (user decision 2026-10-04; values are
self-descriptive).

## 5. Section → Entitlement Mapping (Dashboard)

| Dashboard section | Included iff | Notes |
|---|---|---|
| `orders` + `recentOrders` | `orders.read` | Recent list capped at 5 as today; `[]` when unauthorized |
| `payments` | `payments.read` **or** `payments.stats` | Either suffices so a stats-only viewer still gets revenue totals |
| `inventory` | `inventory.read` | Handler invoked only when entitled (saves queries); `lowStockThreshold` validated in the controller so `400` does not depend on entitlement |
| `catalog` (new, §7) | `catalog.manage` | Anonymous storefront reads stay anonymous; this is the management overview |
| `users` | `SuperAdmin` role directly | No new permission needed; user management stays outside the grantable set except §8 |

`AdminDashboardResponse` sections become nullable (breaking pre-release contract change, documented
in `API_CONTRACT.md` §8 and `DTO_REFERENCE.md` §8).

## 6. Anti-Escalation Guards (`users.manage` Holders; SuperAdmin Exempt)

Enforced inside the command handlers via `ICurrentUser` (which gains `Permissions` in E1):

1. Assigning the `SuperAdmin` role → `403` (`Identity.Forbidden`, new code, inline English
   fallback like the Payments precedent — no resx churn for one message).
2. Any mutation (roles/grant/revoke/unlock) targeting a `SuperAdmin`-held account → `403`.
3. Granting a permission the granter does not hold → `403` (grant-what-you-hold).
4. `register-admin` creates permission-less accounts, so `admins.create` + `users.manage` can never
   mint rights beyond the creator's own.

## 7. New Catalog Counts Query (E2)

`GetCatalogCounts` in the Catalog module: product total, active-product total, category total
(scalars + enum comparison only — the translation-safe shapes proven in P1). New read path
(`ICatalogCountsStore`-style port or repository extension per module convention — decided at
implementation from the existing `IProductRepository`/`ICategoryRepository` shapes),
`CatalogCountsResponse` contract, DI registration, SQLite + SQL Server coverage via the Catalog
factory. No new tables, no migration.

## 8. Implementation Sub-Phases

### E1 — Adaptive Dashboard Without `dashboard.read` (Independent)

- `PermissionCatalog`: delete `DashboardRead` (9→8). `AuthenticationConfiguration`: delete const +
  policy. `AdminDashboardController`: plain `[Authorize]` + `ICurrentUser` gating per §5.
- `ICurrentUser.Permissions`: `CurrentUser` reads `permission` claims; `FakeCurrentUser` settable;
  `DesignTimeCurrentUser` + ~12 `AnonymousCurrentUser` fixtures return `[]` (the design-time one
  uses the literal string — BuildingBlocks must not reference Identity.Domain).
- `AdminDashboardResponse`: nullable sections.
- Tests: rewrite `AdminDashboardTests` (customer → 200-empty, partial → own sections, SuperAdmin →
  full, bad threshold → 400); replace the two `dashboard.read` usages in JWT/grant tests; catalog
  count assertions stay auto-consistent (`All.Count`).
- Gates: build `0W/0E`, arch green, unit green, targeted integration green.

### E2 — Catalog Section

- Counts query/handler/contract/DI per §7 + `catalog.manage` gate in the dashboard composer.
- Tests: SQLite translation + values; dashboard integration shows the section for
  `catalog.manage` holders and `null` without it.
- Docs: `API_CONTRACT.md` §8, `DTO_REFERENCE.md` §8.

### E3 — `users.*` Delegation + Guards

- Catalog +3 (8→11), 3 policies, `AdminController` method-level split (reads incl. per-user
  permissions → `users.read`; writes → `users.manage`; catalog listing stays SuperAdmin),
  `register-admin` → `admins.create`, §6 handler guards + `Identity.Forbidden`.
- Tests: grant-unheld → 403, touch-SuperAdmin → 403, assign-SuperAdmin-role → 403,
  `admins.create` flow works, `users.read` reads but cannot mutate; existing supremacy tests
  extended, not rewritten.
- Docs: all catalog tables, `AUTH_FLOW.md` matrix, `API_CONTRACT.md` §1 (auth column changes),
  `ERROR_CATALOG.md` (new code), this plan's §9 rollout note, completion report.

## 9. Rollout Notes (Binding Where Marked)

- E1 changes the dashboard contract (nullable sections) — frontend must null-check before use.
  Ship E1 first; it is behavior-safe otherwise (superset of visibility, no revocations).
- Deleted `dashboard.read` grants already stored become inert automatically; no migration needed.
  Do **not** re-grant anything for E1.
- E3 introduces a new escalation surface by design: grant `users.manage` only together with
  `users.read` to trusted operators, and never alongside `admins.create` unless full delegation
  is intended (the §6 guards cap the blast radius at the granter's own rights, but review anyway).
- `users.read` deliberately does **not** include the permission catalog document (user decision);
  support staff see values, not the catalog.

## 10. Effort Estimate

| Sub-phase | New files | Modified files | Tests |
|---|---|---|---|
| E1 | ~2 (handler-adjacent) + reworked dashboard tests | `PermissionCatalog`, `AuthenticationConfiguration`, dashboard controller + response, `ICurrentUser` + ~15 implementers, catalog docs | ~15 reworked/added |
| E2 | ~6 (query/handler/contract/store/test) | Dashboard composer, DI, DTO docs | +~6 |
| E3 | ~4 (policies wiring, guards) | 3 handlers, 2 controllers, auth config, contract docs, ADR-017 | +~12, ~10 reworked |

Order E1→E2→E3 is dependency-safe; each ships green independently.
