# Phase 22 — Adaptive Dashboard & User-Management Delegation — Implementation Plan

**Status:** E1–E3 implemented and green (2026-10-04).
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
3. New `users.read` permission so user administration becomes viewable — writes stay
   SuperAdmin-only (the `users.manage` / `admins.create` delegation drafted mid-phase was rolled
   back the same day: granting stays exclusively SuperAdmin).
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

`PermissionCatalog.AdminBundle` follows `All` automatically, so a "full admin" keeps working.
`GET /admin/users/permissions` (catalog listing) stays `SuperAdmin`-only, as do all user
mutations (roles, unlock, grant, revoke) and `register-admin`.

## 5. Section → Entitlement Mapping (Dashboard)

| Dashboard section | Included iff | Notes |
|---|---|---|
| `orders` + `recentOrders` | `orders.read` | Recent list capped at 5 as today; `[]` when unauthorized |
| `payments` | `payments.read` **or** `payments.stats` | Either suffices so a stats-only viewer still gets revenue totals |
| `inventory` | `inventory.read` | Handler invoked only when entitled (saves queries); `lowStockThreshold` validated in the controller so `400` does not depend on entitlement |
| `catalog` (new, §7) | `catalog.manage` | Anonymous storefront reads stay anonymous; this is the management overview |
| `users` | `users.read` | Support viewers see counts; mutations stay SuperAdmin-only |

`AdminDashboardResponse` sections become nullable (breaking pre-release contract change, documented
in `API_CONTRACT.md` §8 and `DTO_REFERENCE.md` §8).

## 6. Anti-Escalation Guards — Removed With the Delegation Rollback (2026-10-05)

The guards drafted for delegated writes (no SuperAdmin assignment, no touching SuperAdmin-held
accounts, grant-what-you-hold, `Identity.Forbidden`) were implemented, verified green, then
deleted together with `users.manage` / `admins.create`: with all mutations back behind
`SuperAdminPolicy`, handler-level guards would be unreachable dead code. `ICurrentUser.Permissions`
remains (it powers the adaptive dashboard in E1).

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

### E3 — `users.read` Delegation (Implemented; Write Delegation Rolled Back 2026-10-05)

- `AdminController` method-level split: reads (`GetUsers`, `GetUserCounts`, `GetUser`,
  `GetUserPermissions`) → `users.read`; every mutation (roles, unlock, grant, revoke), the catalog
  document, and `register-admin` stay `SuperAdmin` (explicit method attributes; class-level gate is
  plain authentication because policies do not inherit exclusively).
- The `users.manage` / `admins.create` permissions and the `DelegationGuard` handler guards were
  implemented, verified green, then **removed the same day** on review: no party besides
  SuperAdmin should grant (see ADR-017 amendment). `UserDelegationTests` was replaced by
  `UsersReadEnforcementTests` (reads 200, writes 403, no-permission 403).
- Tests: `users.read` reads but cannot mutate; existing supremacy tests extended, not rewritten.
- Docs: all catalog tables, `AUTH_FLOW.md` matrix, `API_CONTRACT.md` §1 (auth column changes),
  `ERROR_CATALOG.md` (the `Identity.Forbidden` row added for the guards was removed again).

## 9. Rollout Notes (Binding Where Marked)

- E1 changes the dashboard contract (nullable sections) — frontend must null-check before use.
  Ship E1 first; it is behavior-safe otherwise (superset of visibility, no revocations).
- Deleted `dashboard.read` grants already stored become inert automatically; no migration needed.
  Do **not** re-grant anything for E1.
- No new escalation surface exists after the rollback: every user-mutating action requires
  SuperAdmin, so there is nothing to pair or review beyond the existing SuperAdmin discipline.
- `users.read` deliberately does **not** include the permission catalog document (user decision);
  support staff see values, not the catalog.

## 10. Effort Estimate

| Sub-phase | New files | Modified files | Tests |
|---|---|---|---|
| E1 | ~2 (handler-adjacent) + reworked dashboard tests | `PermissionCatalog`, `AuthenticationConfiguration`, dashboard controller + response, `ICurrentUser` + ~15 implementers, catalog docs | ~15 reworked/added |
| E2 | ~6 (query/handler/contract/store/test) | Dashboard composer, DI, DTO docs | +~6 |
| E3 | ~4 (policies wiring, guards) | 3 handlers, 2 controllers, auth config, contract docs, ADR-017 | +~12, ~10 reworked |

Order E1→E2→E3 is dependency-safe; each ships green independently.
