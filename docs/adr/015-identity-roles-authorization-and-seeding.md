# ADR-015: Identity Roles, Authorization Hardening, and Seeding Strategy

## Status

Accepted (2026-08-31) — Decisions locked by user on 5 points.

## Context

Phase 1 delivered Identity as a platform capability (`PROJECT_CONTEXT.md:6.1` `Customer/Admin/SuperAdmin`, `ARCHITECTURE.md:74` `ICurrentUser`) with:

*   `Domain/Aggregates/Roles.cs:3` static `Customer/Admin/SuperAdmin`, `Domain/Aggregates/User.cs:57` `_roles: List<string>` persisted as `Users.Roles nvarchar(max)` (`Infrastructure/Persistence/Configurations/UserConfiguration.cs:86`), `User.AssignRole/Unlock` with guards, but no `AssignRoleCommand`/`UnlockCommand`.
*   `Application/Commands/RegisterCustomer` + `RegisterAdmin` (`RegisterAdminCommandHandler.cs:80` issues `Roles.Admin`), but `RegisterAdmin` has **no HTTP surface** (`Api/Controllers/Identity/AuthController.cs:25` only `register`→`Customer`, `Infrastructure/DependencyInjection.cs:63` registers handler unused via HTTP; tests bypass via `tests/.../InventoryWebApplicationFactory.cs:107 RegisterAdminAsync`).
*   `Infrastructure/Repositories/RoleRepository.cs:6` is in-memory fake (`BuiltInRoles[]`, `ExistsAsync=>Contains`, `AddAsync=>CompletedTask`), no `DbSet<Role>`, no `Roles` table.
*   `Api/Extensions/AuthenticationConfiguration.cs:54` registers 3 policies (`Customer`/`Admin`/`SuperAdmin` `RequireRole`) correctly with HS256 `JwtOptions` (`Issuer/Audience/SigningKey` validated on start), `JwtProvider.cs:31` emits `ClaimTypes.Role` per `User.Roles`, `CurrentUser.cs` reads it. Only `Api/Controllers/Inventory/InventoryController.cs:32` enforces `[Authorize(Policy=AdminPolicy)]`; 3 policies are otherwise **dead code TD-2** (`PHASE_1_COMPLETION_REPORT.md:220`).
*   No seeder: `Infrastructure/DbContexts/IdentityDbContext.cs:8` only `DbSet<User>`, no `HasData`, no `DbInitializer`; `grep Seeder|HasData` → 0 in `src`; fresh `FluxCommerce_Identity` DB = 0 users. `SuperAdmin` never assigned (grep shows only `Roles.SuperAdmin` constants, never `User.Register(..., SuperAdmin)`); `appsettings.json` `Jwt:SigningKey=""` + `Identity:Seeder` absent.

`PHASE_1_COMPLETION_REPORT.md:220-237` explicitly deferred: “Role enforcement once admin catalog endpoints exist; Admin registration/management endpoints (command done); Unlock endpoint”. `PHASE_1_IDENTITY_ACCESS.md:549` marks OAuth/MFA/fine-grained permissions as out-of-scope. The system must now make roles enforceable and bootstrappable without coupling commerce to Identity internals.

## Decision

Five interlocked decisions (user-confirmed):

### D1 — `POST /auth/register-admin` gated to `SuperAdminPolicy` (strict hierarchy `SuperAdmin → Admin → Customer`)

*   Expose `POST /auth/register-admin` in `AuthController` mapping `RegisterAdminRequest.cs:5` → `RegisterAdminCommand`, guarded `[Authorize(Policy=SuperAdminPolicy)]`.
*   Only a bootstrapped `SuperAdmin` can elevate/register `Admin`. `SuperAdmin` does **not** automatically satisfy `AdminPolicy` — policies remain exact `RequireRole(Roles.*)` (`AuthenticationConfiguration.cs:56`) for determinism (see Consequences).

### D2 — Catalog mutations gated to `AdminPolicy` immediately

*   Change mutation actions (`POST`, `PUT`, `POST .../activate`, `.../archive`, `DELETE`, `PATCH`) in `Api/Controllers/Catalog/ProductsController.cs`, `CategoriesController.cs`, `ProductVariantsController.cs`, `AttributeDefinitionsController.cs` from `[Authorize]` to `[Authorize(Policy=AdminPolicy)]`. Reads (`GET/Search`) stay `[AllowAnonymous]`/`[Authorize]` (public/customer).
*   Resolves TD-2: 3 policies become live (Inventory was already correct). Defers fine-grained permissions (`PHASE_1_IDENTITY_ACCESS.md:549`).

### D3 — Seeder source `appsettings + UserSecrets`, plain password hashed at runtime via `IPasswordHasher`

*   Keep plain-text development credentials in `UserSecrets` (`dotnet user-secrets set "Identity:Seeder:SuperAdmin:Password" "P@ss..."` or env `IDENTITY__SEEDER__SUPERADMIN__PASSWORD`), hash at seed via `Infrastructure/Security/PasswordHasher.cs` (PBKDF2-100k). Never bake static hash into migration or code (rotatable, `ARCHITECTURE.md:21` secrets).
*   Config `Api/appsettings.json: "Identity:Seeder": {"Enabled":false}` (prod off), `appsettings.Development.json: Enabled:true` + `SuperAdmin:{Email,FirstName,LastName}` (password empty). Bound via `IdentitySeederOptions` (`SectionName="Identity:Seeder"`, `ValidateOnStart`).
*   Idempotent `IdentitySeeder.SeedAsync`: `MigrateAsync` → `if !Enabled return` → `if ExistsByEmailAsync(email) return` (unique `Users.Email` index) → `User.Register(id,email,hash,...,[SuperAdmin])` + `IsEmailVerified=true`/`Status=Active` → `AddAsync`/`SaveChanges` → `Information Seeded SuperAdmin {Email}` (never hash).

### D4 — Unlock strictly via `POST /admin/users/{id}/unlock` under `SuperAdminPolicy`

*   New `Api/Controllers/Identity/AdminController.cs` `[Route("admin/users")][Authorize(Policy=SuperAdminPolicy)]` with `POST {id}/roles` (see D5) and `POST {id}/unlock` → `UnlockUserCommand` → `User.Unlock()` → `LockoutState.Reset`. No self-service `POST /auth/unlock`; unlocking is admin-gated for audit control.

### D5 — Real `Roles` EF table + `RoleRepository` DB

*   Create `Domain/Aggregates/Role.cs` (`Entity<Guid>`, `Name(64)`, `NormalizedName(64) unique`, `CreatedAtUtc`) + `Persistence/Configurations/RoleConfiguration.cs` (`HasIndex(NormalizedName) unique`). `IdentityDbContext: DbSet<Role> Roles`. Migration `AddRolesTable`.
*   Replace `RoleRepository` fake array with EF (`AnyAsync`, `AddAsync→Add`, `ExistsAsync→AnyAsync(NormalizedName)`). `GetAllAsync` returns persisted `Role.Name`.
*   Seed static roles (`Customer/Admin/SuperAdmin`) via `IdentitySeeder` (or `RoleConfiguration HasData` rejected for hash reasons, but roles are insert via `RoleRepository` at seed: `if !ExistsAsync(SuperAdmin) AddAsync`). `User.Roles` string list stays (`nvarchar(max)`) — `Roles` table is **lookup only** for validation, no FK (intentional for fixed roles; future normalization if permissions deferred per `PHASE_1_IDENTITY_ACCESS.md:549`).

## Consequences

Positive:
*   Roles become enforceable: `SuperAdmin` bootstraps via seeder, creates `Admin` via gated `register-admin`, `Admin` gates catalog writes — TD-2 resolved, `PHASE_1_COMPLETION_REPORT:222` fulfilled.
*   System bootstrappable: fresh env `dotnet run` seeds `SuperAdmin` idempotently; second boot no duplicate (unique Email).
*   No secret in repo: `UserSecrets`/env + `IPasswordHasher` at runtime, `Enabled` flag prod-safe; `ValidateOnStart` fails if `Jwt:SigningKey` or `Seeder:Password` missing when enabled.
*   `RoleRepository` validates against real `Roles` rows, not hardcoded array — aligns `Identity` persistence layer.

Negative / Trade-offs:
*   `User.Roles nvarchar(max)` + `Roles` table dual source — `Roles` is not authoritative for `User.Roles` assignments (no FK) — kept intentional for fixed 3 roles; normalizing now would add `UserRoles` join for no immediate value (`ARCHITECTURE.md:205` duplication preferable).
*   `SuperAdmin` does not satisfy `AdminPolicy` — a `SuperAdmin` needing Admin UI must be assigned both roles (`[SuperAdmin,Admin]`) or policy must change to `RequireRole(Admin,SuperAdmin)` later (documented).
*   Seeder runs on every boot (`Program.cs` after `Build()`) — adds ~1 query; `Enabled=false` in prod avoids overhead.
*   No `RemoveRole` — prevents accidental `SuperAdmin` removal; add later if needed.

## Alternatives Considered

*   **Seeder via `HasData` in migration** — rejected: bakes hash into migration, non-rotatable, violates `ARCHITECTURE.md:21` secrets.
*   **HostedService vs `Program.cs` inline** — both viable; chosen `Program.cs` `SeedAsync` for determinism before first request (hosted service also viable, uses `BackgroundService` once).
*   **`Admin` can create `Admin` (vs `SuperAdmin`-only)** — rejected per hierarchy decision D1; would allow privilege escalation without SuperAdmin.
*   **Catalog reads gated to `Admin`** — rejected: reads stay public/customer, only mutations gated (least privilege).
*   **Normalize `User.Roles` to `UserRoles` join now** — rejected as over-engineering for 3 fixed roles; deferred to fine-grained permissions phase.

## References

*   `PROJECT_CONTEXT.md:446` §6.1 Identity & Roles
*   `ARCHITECTURE.md:21` Secrets, §74 `ICurrentUser` isolation
*   `PHASE_1_COMPLETION_REPORT.md:220` Remaining Work & TD-2
*   `PHASE_1_IDENTITY_ACCESS.md:549` Out-of-scope (MFA, permissions)
*   This plan: `docs/refactoring/REFACTORING_IDENTITY_ROLES_SEEDING_PLAN.md`
