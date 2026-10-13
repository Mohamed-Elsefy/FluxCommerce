# ADR-017: Delegated User Management (`users.*`) with Anti-Escalation Guards

## Status

Accepted (2026-10-04) — User-confirmed: `users.read` for readable user surfaces.
Amends ADR-016 (user reads are delegatable; D5 "no auto-grants" stands).

**Amendment (2026-10-05):** the `users.manage` / `admins.create` half of this ADR was rolled back
the same day it landed (never deployed): granting must stay SuperAdmin-only, so the write-side
delegation, the escalation guards, and the two permissions were removed again. What remains in
force from this ADR is `users.read` (list/counts/details/per-user permission values) plus the
`register-admin`-stays-SuperAdmin rule. The rejected-alternatives section below is kept as the
rationale trail.

## Context

ADR-016 D5 (as amended) keeps all user/permission administration behind `SuperAdminPolicy`:
`AdminController` (roles, unlock, users list, grant/revoke, catalog) and `register-admin` are
SuperAdmin-only. Operational reality now requires delegation: support staff must *view* users,
trusted operators must manage non-privileged accounts, and new-admin creation must be grantable —
without opening a privilege-escalation path to SuperAdmin-equivalence.

The dashboard (Phase 22 E1) additionally needs a readable users section for non-SuperAdmin holders,
which is only coherent if user reads are delegatable.

## Decision (Final State After the 2026-10-05 Rollback)

### D1 — One new permission: `users.read` (catalog back to 9)

*   `users.read` — `GET /admin/users`, `/counts`, `/{id}`, `/{id}/permissions` (a user's values).
*   The `users.manage` / `admins.create` permissions drafted earlier the same day were **removed
    before any deployment**: granting stays exclusively SuperAdmin, so no delegation surface —
    and no escalation surface — exists for writes.
*   `GET /admin/users/permissions` (catalog document) stays `SuperAdmin`-only, as do role
    assignment, unlock, grant/revoke, and `register-admin`.

### D2 — Method-level split retained on `AdminController`

The class-level `SuperAdminPolicy` is split per action (same technique as the Phase D inventory
split): the four reads → `users.read`; every mutation and the catalog document carry an explicit
`SuperAdminPolicy` method attribute (policies do not inherit exclusively, so the class-level gate
is plain authentication). The architecture test pins this exact mapping.

### D3 — Anti-escalation guards: removed with the delegation surface

With writes back behind `SuperAdminPolicy`, handler-level guards would be unreachable dead code
(R2 standards) and were deleted together with `DelegationGuard`. The `Identity.Forbidden` code
was removed unused. SuperAdmin remains exempt from everything by supremacy (ADR-016 D1).

### D4 — Error and contract surface

*   New code `Identity.Forbidden` (403, inline English fallback — one message, no resx churn,
    following the Payments precedent).
*   No response-shape changes: `UserResponse`/`UserAdminResponse` already carry `Permissions`.
*   `PermissionCatalog.AdminBundle` follows `All` automatically, so a "full admin" includes the
    new family without further code.

## Consequences

Positive:
*   Support (`users.read`) persona becomes expressible, and the dashboard users section gains a
    coherent gate (`users.read` instead of a SuperAdmin role check).
*   `SuperAdmin` path is untouched (supremacy + exact policy preserved).

Negative / Trade-offs:
*   One policy + four re-pointed reads + re-scoped tests (all `SuperAdmin`-minted users tests keep
    passing; Admin-minted ones are re-scoped).
*   Dead `dashboard.read` grants possibly stored from Phase 21 remain inert rows (accepted;
    validation only checks new grants against the catalog).

## Alternatives Considered

*   **Keep all user management SuperAdmin-only** — partially adopted 2026-10-05: reads are
    delegated via `users.read`, but all writes (roles, unlock, grant, revoke, admin creation,
    catalog document) stayed SuperAdmin-only after review found no party besides SuperAdmin
    should grant.
*   **Single `users.manage` covering reads too** — moot after the rollback.
*   **Per-request database permission check instead of grant-what-you-hold via claims** — moot
    after the rollback (no delegated writes remain to guard).
*   **Separate `UserPermissions` join table for the wider catalog** — still deferred (9 fixed
    strings; same rationale as ADR-015/016).

## References

*   `docs/adr/016-permission-based-authorization-and-superadmin-supremacy.md` (D5 manual-only,
    supremacy, refresh boundary — all stand)
*   `docs/IMPLEMENTATION_PLAN.md#phase-22--adaptive-dashboard--user-delegation-`
*   `docs/IMPLEMENTATION_PLAN.md#phase-21--permission-based-authorization-`
