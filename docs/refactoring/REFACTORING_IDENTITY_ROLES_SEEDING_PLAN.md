# FluxCommerce — Identity Roles, Authorization & Seeding — Refactoring & Completion Plan

**Status:** Planned  
**Type:** Identity Completion Cycle (post-Phase 1.6 / pre-Phase 2 Catalog gating)  
**Position:** After `REFACTORING_2_PLAN` / Phase 11 and before Phase 12 Failure Testing  
**Primary Goal:** Make Identity roles enforceable and the system bootstrappable without coupling commerce to Identity internals.

> **Reference during execution.** This is the sole source of truth for implementing the missing Identity components identified in `PHASE_1_COMPLETION_REPORT.md:220-237` (TD-2 dead policies) and confirmed by the R2 audits. All changes must preserve `PROJECT_CONTEXT.md:6.1` hierarchy `SuperAdmin → Admin → Customer` and `ARCHITECTURE.md` modular isolation (`Identity` → `ICurrentUser` only).

---

## 1. Purpose

Phase 1 delivered `User` aggregate with `Roles: List<string>` (`Domain/Aggregates/Roles.cs:3` `Customer/Admin/SuperAdmin`), `JwtProvider` role claims (`Infrastructure/Security/JwtProvider.cs:31` `ClaimTypes.Role`), `CurrentUser` (`Infrastructure/Security/CurrentUser.cs`), and 3 policies (`Api/Extensions/AuthenticationConfiguration.cs:54` `Customer/Admin/SuperAdmin` `RequireRole`) — but:

*   `RegisterAdminCommand` exists at `Application` only, no HTTP surface (`AuthController.cs:25` only `register`→`Customer`).
*   `User.AssignRole/Unlock` exist at `Domain` only, no `AssignRoleCommand`/`UnlockCommand`.
*   `RoleRepository.cs:6` is in-memory fake (`AddAsync→CompletedTask`), no `Roles` table.
*   No seeder — fresh `FluxCommerce_Identity` DB = 0 users (no `HasData`, no `DbInitializer`).
*   Only `InventoryController.cs:32` enforces `[Authorize(Policy=AdminPolicy)]`; 3 policies are dead code.

This plan closes those gaps with **5 locked decisions** (user-confirmed 2026-08-31):

1.  `POST /auth/register-admin` → **`SuperAdminPolicy`** (strict hierarchy).
2.  Catalog mutations (`POST/PUT/DELETE /catalog/**`, products/categories/variants) → **`AdminPolicy`** now.
3.  Seeder `appsettings+UserSecrets` plain → `IPasswordHasher` at runtime (no baked hash).
4.  `POST /admin/users/{id}/unlock` → `SuperAdminPolicy` (admin-only unlock).
5.  Real `Roles` EF table + `RoleRepository` DB (replace fake).

---

## 2. Position in Roadmap

```text
Phase 1 (95% — roles dead, seeding missing)
  ↓
Phase 1.5/1.6 (Runtime, i18n — done)
  ↓
Phase 2 Catalog (expects Admin gating — this plan unblocks it)
  ↓
═ REFACTORING_IDENTITY (this plan) ═
  ↓
Phase 12 Failure Testing (requires roles live to test 403 vs 401)
```

No Phase 12 work inside this cycle; no OAuth/MFA/fine-grained permissions (`PHASE_1_IDENTITY_ACCESS.md:549` out-of-scope).

---

## 3. Source of Authority

1.  `docs/PROJECT_CONTEXT.md:446` §6.1 Identity & Roles
2.  `docs/ARCHITECTURE.md:1` §5-6 dependency rules, §21 secrets
3.  `docs/adr/001-014` (esp. 001 modular monolith, 003 building blocks)
4.  `docs/completed/PHASE_1_COMPLETION_REPORT.md:220` Remaining Work & TD-2
5.  `docs/phases/PHASE_1_IDENTITY_ACCESS.md:29` objectives & §15 out-of-scope
6.  This plan + `docs/adr/015-identity-roles-authorization-and-seeding.md` (decisions)

---

## 4. Objectives

*   Make `Customer/Admin/SuperAdmin` enforceable (policies live).
*   Make DB bootstrappable idempotently (SuperAdmin seeded).
*   Normalize `Roles` persistence without coupling commerce to Identity.
*   Keep `BuildingBlocks` clean (Identity owns `Roles`, commerce only `ICurrentUser`).

## 5. Out of Scope

OAuth/OIDC/social/MFA/passkeys, device/session dashboard, fine-grained permissions, SSO/federation (`PHASE_1_IDENTITY_ACCESS.md:549`), `RemoveRole`, per-user `PreferredLanguage` provider (i18n future), `Roles` hierarchy implication (`SuperAdmin` ≠ `Admin` — exact `RequireRole` kept).

---

## 6. Principles

*   **Hierarchy exact:** `SuperAdmin` does **not** imply `Admin` unless explicitly `Roles=[SuperAdmin,Admin]` or policy changed to `RequireRole(Admin,SuperAdmin)` — keep `AuthenticationConfiguration.cs:56` exact for determinism; SuperAdmin seeded with both roles if needed.
*   **No hard-coded secrets:** `appsettings.json` `Identity:Seeder:Enabled=false` (prod off); `appsettings.Development.json` `Enabled=true` + empty `Password` → `UserSecrets` `Identity:Seeder:SuperAdmin:Password` or `IDENTITY__SEEDER__SUPERADMIN__PASSWORD` env; hashed at seed via `IPasswordHasher`.
*   **Idempotent seeder:** `ExistsByEmailAsync` check + unique `Users.Email` index → second boot no duplicate.
*   **Thin controllers:** `AuthController`/`AdminController` delegate to `ICommandHandler`, no domain logic.

---

## 7. Detailed Execution Plan

### Phase A — Real `Role` Persistence

*   **Domain:** `Domain/Aggregates/Role.cs` new — `Entity<Guid>` (`Id`, `Name(64)`, `NormalizedName(64) unique`, `CreatedAtUtc`) + `Domain/Aggregates/Roles.cs` stays constants.
*   **Infrastructure:** `DbContexts/IdentityDbContext.cs` add `DbSet<Role> Roles`; `Persistence/Configurations/RoleConfiguration.cs` new (`HasKey`, `HasIndex(NormalizedName) unique`, `HasMaxLength(64)`, `IsRequired`); `Repositories/RoleRepository.cs` replace static array with EF (`AnyAsync`, `AddAsync→Add`, `GetAllAsync→ToListAsync`, `ExistsAsync→AnyAsync(n=>n==normalized)`).
*   **Migration:** `dotnet ef migrations add AddRolesTable --project Identity.Infrastructure --startup Api` → `Roles` table + index; **no `HasData`** (seeder handles data).
*   **Tests:** `ArchitectureTests: Identity_ShouldNot_ReferenceOtherModules` still passes; add `RolesTable_ShouldExist` hardening (optional).

### Phase B — Seeder (Idempotent, Config-driven)

*   **Options:** `Persistence/Seeding/IdentitySeederOptions.cs` new — `SectionName="Identity:Seeder"` (`Enabled:bool`, `SuperAdmin:{Email,Password,FirstName,LastName}`), `ValidateOnStart` (Email/Password required when `Enabled`), bind `AddOptions<IdentitySeederOptions>().Bind(GetSection(...)).ValidateDataAnnotations().ValidateOnStart()`.
*   **Seeder:** `Persistence/Seeding/IdentitySeeder.cs` new — `SeedAsync(CancellationToken)` : `MigrateAsync` → `if !Enabled → return` → `if await ExistsByEmailAsync(email) → return` → `hash = PasswordHasher.Hash(plain)` → `user = User.Register(id,email,hash,first,last,utcNow,[SuperAdmin],null)` + `IsEmailVerified=true`/`Status=Active` → `AddAsync` + `SaveChangesAsync` → log `Information Seeded SuperAdmin {Email}` (never password/hash).
*   **Wiring:** `Infrastructure/DependencyInjection.cs` `AddScoped<IdentitySeeder>`; `Api/Program.cs` after `Build()` → `using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync()` (alt: `IHostedService IdentitySeederHostedService` once).
*   **Config:** `Api/appsettings.json` add `"Identity:Seeder":{"Enabled":false}`; `appsettings.Development.json` add `"Enabled":true, "SuperAdmin":{"Email":"admin@flux.local","Password":"","FirstName":"System","LastName":"Admin"}` (password empty → `UserSecrets`). Document in `docs/current/LOCAL_DEVELOPMENT.md` (`dotnet user-secrets set "Identity:Seeder:SuperAdmin:Password" "P@ss..."`).

### Phase C — Commands (Unlock & AssignRole)

*   `Application/Commands/AssignRole/{AssignRoleCommand.cs, Validator, Handler}` new — `UserId:Guid, Role:string` → `RoleRepository.ExistsAsync` → `UserRepo.GetByIdAsync` → `User.AssignRole` → `RoleAlreadyAssigned` → `UpdateAsync` → `Result<UserResponse>`.
*   `Application/Commands/UnlockUser/{UnlockUserCommand.cs, Validator, Handler}` new — `UserId` → `GetById` → `User.Unlock()` → `UpdateAsync` (currently `LockoutState.Reset`). Both `Result` → `UserResponseMapper`.

### Phase D — Controllers & Authorization Wiring

*   **AuthController.cs:** Inject `ICommandHandler<RegisterAdminCommand, Result<UserResponse>>` + `RegisterAdminRequest.cs:5` already exists; add:
    ```csharp
    [HttpPost("register-admin")]
    [Authorize(Policy = AuthenticationConfiguration.SuperAdminPolicy)]
    public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminRequest req, CancellationToken ct)
      => Created(await _registerAdminHandler.HandleAsync(new RegisterAdminCommand(req.Email, req.Password, req.FirstName, req.LastName), ct));
    ```
*   **New `Api/Controllers/Identity/AdminController.cs`:** `[ApiController][Route("admin/users")][Authorize(Policy=SuperAdminPolicy)]`
    ```csharp
    [HttpPost("{id:guid}/roles")] AssignRole
    [HttpPost("{id:guid}/unlock")] UnlockUser
    ```
    Thin delegation.

*   **Catalog Gating:** `Api/Controllers/Catalog/ProductsController.cs`, `CategoriesController.cs`, `ProductVariantsController.cs`, `AttributeDefinitionsController.cs` — change mutation actions (`POST`, `PUT`, `POST .../activate`, `.../archive`, `DELETE`, `PATCH`) from `[Authorize]` to `[Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]`; keep `GET/Search` as `[AllowAnonymous]` or `[Authorize]` (read public). `InventoryController.cs:32` already correct — no change.
*   **Policy Hierarchy Note:** Keep `AuthenticationConfiguration.cs:56` exact (`RequireRole(Roles.Admin)`), `SuperAdmin` not implied — document; SuperAdmin seeded with `[SuperAdmin]` only (or `[SuperAdmin,Admin]` if caller needs Admin UI, explicitly).

### Phase E — Guardrails & Tests

*   **Architecture:** `tests/Architecture/IdentityArchitectureTests.cs` add `AdminController_ShouldRequire_SuperAdmin`, `RegisterAdmin_ShouldRequire_SuperAdmin`, `CatalogMutations_ShouldRequire_Admin`.
*   **Integration:** `tests/IntegrationTests/AuthorizationTests.cs` extend matrix: `Customer→403 on POST /auth/register-admin`, `Admin→403`, `SuperAdmin→201`, `Unauth→401`; new `CatalogAdminGatingTests.cs` (`Customer 403 vs Admin 201 on POST /catalog/products`); `AdminControllerTests.cs` (duplicate role 409, unlock 200); `IdentitySeederTests.cs` (idempotent second boot 0 duplicate).
*   **Unit:** `AssignRoleCommandHandlerTests.cs`, `UnlockUserCommandHandlerTests.cs` (domain `User.AssignRole/Unlock` already `UserAggregateTests:567`).

---

## 8. File / Migration Map

```
New:
  Domain/Aggregates/Role.cs
  Infrastructure/Persistence/Configurations/RoleConfiguration.cs
  Infrastructure/Persistence/Seeding/IdentitySeeder.cs
  Infrastructure/Persistence/Seeding/IdentitySeederOptions.cs
  Application/Commands/AssignRole/AssignRoleCommand.cs + Validator + Handler
  Application/Commands/UnlockUser/UnlockUserCommand.cs + Validator + Handler
  Api/Controllers/Identity/AdminController.cs
Modified:
  Api/Controllers/Identity/AuthController.cs
  Api/Controllers/Catalog/*Controller.cs (policy attributes)
  Modules/Identity/Infrastructure/{DbContexts/IdentityDbContext.cs, Repositories/RoleRepository.cs, DependencyInjection.cs}
  Api/Program.cs
  Api/appsettings*.json
  docs/current/LOCAL_DEVELOPMENT.md
Migration:
  Identity.Infrastructure/Migrations/*_AddRolesTable.cs
```

## 9. Verification Checklist (Each Phase)

```bash
dotnet build FluxCommerce.slnx -c Debug           # 0 warnings 0 errors
dotnet test tests/Architecture/... -c Debug        # 12→17/17 (or 174)
dotnet test tests/UnitTests/... --filter Identity  # 183 + ~6 new
dotnet test tests/IntegrationTests/... --filter Authorization # 42 + ~8 new
# Manual:
dotnet user-secrets set "Identity:Seeder:SuperAdmin:Password" "Test123!"
dotnet run --project src/FluxCommerce.Api          # log Seeded SuperAdmin admin@flux.local
curl -X POST https://localhost:7119/auth/login -d '{"email":"admin@flux.local","password":"Test123!"}' # 200 + role SuperAdmin in JWT
curl -H "Authorization: Bearer <superadmin>" -X POST https://localhost:7119/auth/register-admin -d '{...}' # 201
curl -H "Authorization: Bearer <customer>" POST /catalog/products # 403
curl -H "Authorization: Bearer <admin>" POST /catalog/products # 201
# Restart → log "SuperAdmin already exists, skip" (idempotent)
```

## 10. Risks & Mitigations

*   Seeder secret leak → only `UserSecrets`/env, `Enabled` flag, never log hash; `HasData` rejected.
*   SuperAdmin lockout → seeder creates `Active` verified user (no lockout); `Unlock` now admin-gated.
*   `User.Roles nvarchar(max)` + `Roles` table dual source → `Roles` is lookup-only for validation; `User.Roles` remains string list (no FK) — intentional for fixed roles; future normalization if fine-grained permissions (deferred per `PHASE_1_IDENTITY_ACCESS.md:549`).
*   `SuperAdmin` not satisfying `AdminPolicy` → intentional exact `RequireRole`; document that SuperAdmin needing Admin UI must be assigned both roles or policy changed to `RequireRole(Admin,SuperAdmin)`.

## 11. Definition of Done

*   `POST /auth/register-admin` exists, `SuperAdmin`-only, 201/403 matrix green.
*   `POST /admin/users/{id}/roles` + `.../unlock` exist, `SuperAdmin`-only.
*   `Roles` table + `RoleRepository` DB, idempotent seeder boots SuperAdmin from config.
*   Catalog mutations `Admin`-gated, read remains public.
*   Architecture + unit + integration green, no `ARCHITECTURE.md` BuildingBlocks violation (commerce still only `ICurrentUser`).
