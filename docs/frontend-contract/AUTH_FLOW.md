# FluxCommerce — Auth Flow for Frontend

> Access/refresh lifecycle, headers, claims, roles. Source: `AuthController` (`Route auth`, class `[Authorize]` + per-action overrides), `AuthenticationConfiguration`, `Identity.Infrastructure.Authentication.JwtOptions`, `Infrastructure.Security.JwtProvider`, `Application.Security.TokenLifetimes`, `Domain.ValueObjects.LockoutState`, `Application.Validation.PasswordPolicy`, `Application.Commands.{Login,Refresh,Logout}`.

---

## 1. Lifecycle overview

```
POST /auth/register ──→ Pending user + verification email (24h token, FrontendBaseUrl link)
  → POST /auth/verify-email {token} ──→ Active (+ 200 UserResponse)
  → POST /auth/login {email,password} ──→ LoginResponse {accessToken 15m JWT, refreshToken 7d opaque}
  → Authorization: Bearer <access> on every [Authorize] call; GET /auth/me to hydrate profile
  → before 15m expiry: POST /auth/refresh {refreshToken} ──→ new pair (old revoked, reuse → 401)
  → POST /auth/logout {refreshToken} ──→ revoke (idempotent)
  → forgot: POST /auth/forgot-password (always 200) → email 1h token → POST /auth/reset-password
  → authed: POST /auth/change-password {current,new,confirm}
```

Refresh never uses JWT — it presents the opaque refresh token (`AllowAnonymous`). `register/login/refresh/logout/verify-email/resend-verification/forgot/reset` are `AllowAnonymous`; `register-admin` needs `SuperAdmin`; `change-password` + `me` need any JWT; `admin/users/*` need `SuperAdmin`.

---

## 2. Headers

| Header | Send when | Format |
|---|---|---|
| `Authorization` | Every `[Authorize]` endpoint (all except the 8 anonymous auth ops + webhook + health + catalog/cart/order reads as documented in `API_CONTRACT.md`) | `Bearer <accessToken>` |
| `Accept-Language` | Optional, every op | `en\|fr\|de\|es\|ar` (or `?culture=`) |
| `X-Correlation-ID` | Optional per flow | Reused or `Guid:N`; echoed + `X-Request-ID` in response |
| `Content-Type` | POST/PUT/PATCH with body | `application/json` |
| `Idempotency-Key` | `POST /orders/checkout` only | `1..128` chars (not auth, listed for completeness) |

No cookies, no CSRF, no API key. Swagger: global Bearer requirement, cleared on `[AllowAnonymous]` (`SecurityRequirementFilter`).

---

## 3. Tokens: config, claims, lifetimes

```csharp
// JwtOptions (Section "Jwt", ValidateOnStart, all required except lifetimes)
string Issuer = ""; string Audience = ""; string SigningKey = ""; // HmacSha256, dev Issuer/Audience=https://localhost:7119
int AccessTokenLifetimeMinutes = 15; int ClockSkewMinutes = 5;
// TokenValidation: ValidateIssuer/Audience/Lifetime/IssuerSigningKey=true, ClockSkew=5m
// JwtProvider.CreateAccessToken(userId,email,roles):
Claim(ClaimTypes.NameIdentifier, userId.ToString()) + Claim(ClaimTypes.Email, email) + roles.Select(r => Claim(ClaimTypes.Role, r))
// expires = UtcNow + 15m, HmacSha256
// TokenLifetimes: RefreshToken=7d, EmailVerification=24h, PasswordReset=1h
```

```ts
interface LoginResponse { userId: string; email: string; roles: string[]; accessToken: string; refreshToken: string; refreshTokenExpiresAtUtc: string }
```

- Access token: short-lived JWT (15m, 5m clock tolerance). Decode `exp` client-side for proactive refresh; never trust claims for authz — server re-checks roles per policy.
- Permission claims (Phase B, ADR-016): the access token additionally carries one `permission` claim per granted action-level permission (`orders.read`, `orders.cancel`, `orders.refund`, `payments.read`, `payments.stats`, `inventory.read`, `inventory.manage`, `catalog.manage`, `dashboard.read`). Grants/revokes take effect on the **next login/refresh** only — a demoted admin keeps rights until the current token expires. `GET /auth/me` returns the authoritative `permissions: string[]` for menu rendering; `SuperAdmin` bypasses every role/permission gate server-side.
- Refresh token: opaque DB-backed string (`TokenGenerator`), `7d` expiry (`refreshTokenExpiresAtUtc: DateTime`). Rotation: `RotateRefreshToken(oldValue, newValue, now, now+7d)` revokes old + issues new; presenting a revoked/reused/unknown value → `401 Unauthorized Identity.InvalidToken` (see `RefreshCommandHandler`: null token/user or rotation failure all map to `InvalidToken()`).
- Verify/reset tokens: email link tokens (`24h` / `1h`), single-use.

---

## 4. Roles and endpoint matrix

`Roles`: `Customer="Customer"`, `Admin="Admin"`, `SuperAdmin="SuperAdmin"`. Role policies are role-or-SuperAdmin (SuperAdmin passes all); `SuperAdmin` policy stays exact. Permission policies (Phase 21, full catalog in `API_CONTRACT.md` §0.5) require the matching `permission` claim — SuperAdmin passes those too.

| Endpoint | Policy | Frontend meaning |
|---|---|---|
| `POST /auth/register,login,refresh,logout,verify-email,resend-verification,forgot-password,reset-password` | Anonymous | Public |
| `POST /auth/change-password`, `PUT /auth/profile`, `GET /auth/me` | Any authenticated JWT | Any logged-in user |
| `POST /auth/register-admin`, `/admin/users/**` (roles, unlock, list, counts, get, permissions) | `SuperAdmin` | Bootstrap/admin console only; `401` no JWT, `403` wrong role |
| Catalog writes | `catalog.manage` | Admin console with the permission; otherwise `401/403` |
| `GET /inventory/**` | `inventory.read` | Reader permission |
| `POST/PATCH /inventory/**` | `inventory.manage` | Stock-manager permission |
| `GET /admin/orders/**` | `orders.read` | Sales-viewer permission |
| `POST /admin/orders/{id}/cancel` | `orders.cancel` | — |
| `POST /admin/orders/{id}/refund` | `orders.refund` | — |
| `GET /admin/payments/**` (except stats) | `payments.read` | — |
| `GET /admin/payments/stats` | `payments.stats` | — |
| `GET /admin/dashboard` | `dashboard.read` | — |
| Catalog reads, guest cart | Anonymous | Storefront, no token |
| Auth cart (`/cart`, `/cart/items`, `/cart/merge`), `POST /orders/*`, `GET /orders*`, `GET /api/payments/mine`, `GET /api/payments/{id}` | Authenticated (+ ownership on orders/payments/cart) | Customer JWT; other-user ids → `403/404` masked |

`SuperAdmin` passes every row above (supremacy); a missing permission → `403` like a wrong role.

`GET /auth/me` resolves user from `ICurrentUser` (JWT `NameIdentifier`) — no id param, so no IDOR.

---

## 5. Login gates: verification, lockout, credentials

- `401 Identity.InvalidCredentials` — wrong email/password.
- `401 Identity.EmailNotVerified` — must `POST /auth/verify-email` first (or `resend-verification` if expired; response `{emailSent, expiresAtUtc?}`).
- `401 Identity.AccountLocked` — `LockoutState`: `DefaultMaxFailedAccessAttempts=5`, `DefaultLockoutDuration=15m`. Each bad password `RecordFailedAttempt(now)`; 5th locks (`LockoutEndUtc=now+15m`, counter reset to 0); `IsLockedAt(now)=now<LockoutEndUtc`; success `Reset()` to `(0,null)`. While locked, attempts return locked without incrementing. `SuperAdmin` can `POST /admin/users/{id}/unlock`.
- `409 Identity.DuplicateEmail` on register; `409 RoleAlreadyAssigned` on role assign.

---

## 6. Password policy (exact)

`PasswordPolicy`: `MinLength=8`, `MaxLength=128`, requires upper + lower + digit + special from `!@#$%^&*()-_=+[]{};:'",.<>/?\|`+"`"+`~`. Empty → `Validation.Password.Required`; violations → `Password.MinLength/MaxLength/Uppercase/Lowercase/Digit/Special` in `errors{Password:[...]}` (`400 ValidationProblemDetail`). Applies to register, `reset(newPassword)`, `change(newPassword+confirm==new)`.

Email rules: required, format, max 254, trimmed + lowercased server-side.

---

## 7. Frontend implementation guide

```ts
// Storage: access in memory (never localStorage); refresh in httpOnly cookie or secure store if available.
// Proactive refresh at ~13-14m (before 15m exp, inside 5m skew tolerance):
async function authedFetch(url: string, init: RequestInit = {}) {
  let res = await fetch(url, { ...init, headers: { ...init.headers, Authorization: `Bearer ${accessToken}` } });
  if (res.status === 401) { // try once
    const r = await fetch("/auth/refresh", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken }) });
    if (!r.ok) { redirectToLogin(); return res; } // InvalidToken — force login
    ({ accessToken, refreshToken } = (await r.json()).data as LoginResponse);
    res = await fetch(url, { ...init, headers: { ...init.headers, Authorization: `Bearer ${accessToken}` } });
  }
  return res;
}
```

```bash
# register → verify → login → me → refresh → logout
curl -X POST https://localhost:7119/auth/register -H "Content-Type: application/json" -d '{"email":"ada@flux.local","password":"Test123!","firstName":"Ada","lastName":"Lovelace"}'
curl -X POST https://localhost:7119/auth/verify-email -H "Content-Type: application/json" -d '{"token":"<from-email>"}'
curl -X POST https://localhost:7119/auth/login -H "Content-Type: application/json" -d '{"email":"ada@flux.local","password":"Test123!"}'
curl https://localhost:7119/auth/me -H "Authorization: Bearer <access>"
curl -X POST https://localhost:7119/auth/refresh -H "Content-Type: application/json" -d '{"refreshToken":"<opaque>"}'
curl -X POST https://localhost:7119/auth/logout -H "Content-Type: application/json" -d '{"refreshToken":"<opaque>"}'
```

Rules: generate no client token state beyond the pair; on `refresh 401` drop both and redirect to login (don’t retry); after login call `POST /cart/merge {guestCartId}` to preserve guest cart; `GET /auth/me` after login/refresh to sync `roles/isEmailVerified/status`.
