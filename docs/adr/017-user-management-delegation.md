# ADR-017: Delegated User Management (`users.*`) with Anti-Escalation Guards

## Status

Accepted (2026-10-04) — User-confirmed: new `users.read` / `users.manage` / `admins.create`
permissions; SuperAdmin-only catalog document; escalation guards as specified below.
Amends ADR-016 (user management is no longer exclusively SuperAdmin; D5 "no auto-grants" stands).

## Context

ADR-016 D5 (as amended) keeps all user/permission administration behind `SuperAdminPolicy`:
`AdminController` (roles, unlock, users list, grant/revoke, catalog) and `register-admin` are
SuperAdmin-only. Operational reality now requires delegation: support staff must *view* users,
trusted operators must manage non-privileged accounts, and new-admin creation must be grantable —
without opening a privilege-escalation path to SuperAdmin-equivalence.

The dashboard (Phase 22 E1) additionally needs a readable users section for non-SuperAdmin holders,
which is only coherent if user reads are delegatable.

## Decision

### D1 — Three new permissions (catalog 8 → 11 after the `dashboard.read` deletion)

*   `users.read` — `GET /admin/users`, `/counts`, `/{id}`, `/{id}/permissions` (a user's values).
*   `users.manage` — `POST /{id}/unlock`, `POST /{id}/roles`, `POST /{id}/permissions`,
    `DELETE /{id}/permissions/{permission}`.
*   `admins.create` — `POST /auth/register-admin` (creates permission-less accounts, unchanged).
*   `GET /admin/users/permissions` (catalog document) stays `SuperAdmin`-only by explicit user
    decision: `users.read` holders see granted *values*, which are self-descriptive.

### D2 — Enforcement moves to method level on `AdminController`

The class-level `SuperAdminPolicy` is split per action (same technique as the Phase D inventory
split): reads → `users.read`, writes → `users.manage`. `register-admin` moves to `admins.create`.
The `AdminController_ShouldRequire_SuperAdminPolicy` architecture test is narrowed to the
remaining SuperAdmin-only actions (catalog document) or re-pointed accordingly at implementation.

### D3 — Anti-escalation guards (SuperAdmin exempt from all three)

Enforced inside the command handlers via `ICurrentUser` (gains `Permissions` in Phase 22 E1):

1. Assigning the `SuperAdmin` role as non-SuperAdmin → `403 Identity.Forbidden`.
2. Mutating a `SuperAdmin`-held account (roles/grant/revoke/unlock) as non-SuperAdmin → `403`.
3. Granting a permission the granter does not hold → `403` (grant-what-you-hold).

Rationale: without (3), `admins.create` + `users.manage` would mint arbitrary privilege
(`create empty admin → grant everything`), i.e. de-facto SuperAdmin. With (1)–(3), delegated
power is capped at the granter's own rights, and `register-admin`'s permission-less creation
keeps the mint inert until a rights-holder grants within their own set.

### D4 — Error and contract surface

*   New code `Identity.Forbidden` (403, inline English fallback — one message, no resx churn,
    following the Payments precedent).
*   No response-shape changes: `UserResponse`/`UserAdminResponse` already carry `Permissions`.
*   `PermissionCatalog.AdminBundle` follows `All` automatically, so a "full admin" includes the
    new family without further code.

## Consequences

Positive:
*   Support (`users.read`) and operator (`users.manage`, `admins.create`) personas become
    expressible; the dashboard users section gains a coherent gate.
*   Guards (1)–(3) make delegation provably non-escalating beyond the granter's rights.
*   `SuperAdmin` path is untouched (supremacy + exact policy preserved).

Negative / Trade-offs:
*   Three policies + handler branches + ~12 new tests + rewiring of the users endpoints and their
    existing tests (all `SuperAdmin`-minted users tests keep passing; Admin-minted ones are
    re-scoped).
*   `users.manage` + `admins.create` held jointly is intentionally powerful (can onboard + fully
    empower within one's own rights) — must be granted as a pair only to trusted operators
    (rollout note in the Phase 22 plan §9).
*   Dead `dashboard.read` grants possibly stored from Phase 21 remain inert rows (accepted;
    validation only checks new grants against the catalog).

## Alternatives Considered

*   **Keep all user management SuperAdmin-only** — rejected per user decision; blocks support delegation.
*   **Single `users.manage` covering reads too** — rejected: forces a read/write bundle where a
    read-only support role is the primary ask.
*   **Per-request database permission check instead of grant-what-you-hold via claims** — rejected:
    adds a DB round-trip and couples handlers to live role tables; claims + the refresh boundary
    (ADR-016 D4) already deliver current-enough rights (≤15 min window, accepted).
*   **Allow delegating the catalog document too** — rejected per explicit user decision
    (not important for holders; values suffice).
*   **Separate `UserPermissions` join table for the wider catalog** — still deferred (11 fixed
    strings; same rationale as ADR-015/016).

## References

*   `docs/adr/016-permission-based-authorization-and-superadmin-supremacy.md` (D5 manual-only,
    supremacy, refresh boundary — all stand)
*   `docs/phases/PHASE_22_ADAPTIVE_DASHBOARD_AND_USER_DELEGATION.md` (§4–§6, §8–§9)
*   `docs/phases/PHASE_21_PERMISSION_BASED_AUTHORIZATION.md` (§4 catalog, §6.4 surface)
