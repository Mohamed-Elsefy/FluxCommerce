# EF Core Command Reference

The official reference for running EF Core (migrations, DbContext, database) commands in this project.

## Overview

The solution is a modular monolith: each business module owns its own **DbContext** and its own **migrations**, stored inside that module's Infrastructure project.

- `src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure` — `IdentityDbContext`, the only module with migrations so far (under `Persistence/Migrations`).
- `src/Modules/{Catalog, Cart, Orders, Inventory, Payments}/...Infrastructure` — stub modules. Their DbContexts and **design-time factories** already exist, but no migrations yet.

Each module uses a dedicated SQL Server database (`FluxCommerce_Identity`, `FluxCommerce_Catalog`, ...) and its own migrations history table (e.g. `__EFMigrationsHistory_Identity`), so commands for different modules never interfere.

EF Core version: **10.0.10** (see `Directory.Packages.props`). Every DbContext has an `IDesignTimeDbContextFactory` (`DbContexts/*DesignTimeDbContextFactory.cs`), so `dotnet ef` never needs to boot the web application to resolve a DbContext.

## Variables

Reusable values used throughout this document (Identity module — substitute the project/DbContext when working in another module):

| Variable | Value |
|----------|-------|
| Infrastructure project | `src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure` |
| Startup project | `src/FluxCommerce.Api` |
| DbContext | `IdentityDbContext` |
| Migrations folder | `Persistence/Migrations` (Identity) |
| Migrations history table | `__EFMigrationsHistory_Identity` |
| Connection string key | `Database:ModuleConnections:Identity` |

Run every `dotnet ef` command from the **repository root** and always pass both `--project` (Infrastructure) and `--startup-project` (API):

```powershell
dotnet ef <command> --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

### Install the EF Core CLI tool

```powershell
dotnet tool install --global dotnet-ef --version 10.0.10
```

Installs the `dotnet ef` global tool pinned to the project's EF Core version.

```powershell
dotnet ef --version
```

Verifies the tool is installed and reports its version.

## Migration Commands

### Create Migration

```powershell
dotnet ef migrations add EnrichUserProfile --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api -o Persistence/Migrations
```

Scaffolds a new migration (class + `.Designer.cs` + snapshot update) from the current model. `-o Persistence/Migrations` places it in the module's migrations folder — required for Identity, whose migrations live under `Persistence/` rather than the default `Migrations/`.

> **Precondition:** the solution must compile (the tool loads the Infrastructure assembly). Run `dotnet build` first.

### Remove Migration

```powershell
dotnet ef migrations remove --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Removes the **last** migration and reverts the model snapshot — only allowed if that migration has **not** been applied to the database yet. If it was applied, `dotnet ef migrations remove` fails with a hint; roll back the database first (see [Target Migration](#target-migration)).

### Update Database

```powershell
dotnet ef database update --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Applies all pending migrations to the database named by the connection string. Nothing happens if the database is already up to date.

### List Migrations

```powershell
dotnet ef migrations list --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Lists all migrations with their state: `(Pending)` marks migrations not yet applied to the target database.

### Generate SQL Script

```powershell
dotnet ef migrations script --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api -o identity.sql
```

Generates a SQL script of all migrations and writes it to `identity.sql`. Useful for code-reviewing what a migration does and for applying migrations in environments without the EF tooling.

```powershell
dotnet ef migrations script --idempotent -o identity.sql --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Generates an idempotent script (guards every statement with the history table), safe to run multiple times — the right choice for production deployments.

### Drop Database

```powershell
dotnet ef database drop --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Drops the module's database **permanently**. Prompts for confirmation (use `--force` to skip). Nothing in the codebase is affected; the next `database update` recreates the schema from migrations.

## DbContext Commands

```powershell
dotnet ef dbcontext info --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Shows the resolved DbContext type, its provider, and the connection string the design-time factory selected — the first thing to run when a command behaves unexpectedly.

```powershell
dotnet ef dbcontext list --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Lists every DbContext discoverable in the given project/startup project combination.

## Using a Custom Connection String

Every command accepts `--connection` to override the configured connection string without touching config files:

```powershell
dotnet ef database update --connection "Server=localhost,1433;Database=FluxCommerce_Identity;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True" --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Applies migrations to the database described by `--connection`, ignoring the configured `Database:ModuleConnections:Identity` value. Handy for applying migrations to a specific local database instance or a test environment. The same flag works with `migrations script`, `database drop`, and `dbcontext info`.

## Target Migration

```powershell
dotnet ef database update SomeMigrationName --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

Applies (or reverts) migrations until the database is at the named migration. To **update to a specific migration**, name a migration you have not applied yet.

```powershell
dotnet ef database update PreviousMigrationName --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

**Rolls back one migration**: naming the migration before the last one reverts the latest. Use `migrations remove` afterwards to delete the rolled-back migration file.

```powershell
dotnet ef database update 0 --project src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure --startup-project src/FluxCommerce.Api
```

**Rolls back to zero** — removes every migration from the database, leaving an empty schema. Same as dropping the database, minus deleting the database itself.

## Helpful Development Commands

```powershell
dotnet build
```

Compiles the whole solution. `dotnet ef` requires a successful build of the target project, so build before creating migrations.

```powershell
dotnet test
```

Runs the full test suite (Architecture, Unit, Integration). Run it after creating or applying a migration to catch regressions.

```powershell
dotnet clean
```

Removes build artifacts; useful when EF tooling behaves oddly after stale outputs (`bin`/`obj`).

```powershell
dotnet build --no-restore
```

Fast incremental rebuild when no package changes are pending — speeds up the build-then-migrate loop.

## Multi-module Examples

The identical command pattern applies to every module — **only the project path and DbContext name change**. Migrations history tables are per-module, so modules are independent.

| Module | Infrastructure project | DbContext | History table |
|--------|------------------------|-----------|---------------|
| Identity | `src/Modules/Identity/FluxCommerce.Modules.Identity.Infrastructure` | `IdentityDbContext` | `__EFMigrationsHistory_Identity` |
| Catalog | `src/Modules/Catalog/FluxCommerce.Modules.Catalog.Infrastructure` | `CatalogDbContext` | `__EFMigrationsHistory_Catalog` |
| Cart | `src/Modules/Cart/FluxCommerce.Modules.Cart.Infrastructure` | `CartDbContext` | `__EFMigrationsHistory_Cart` |
| Orders | `src/Modules/Orders/FluxCommerce.Modules.Orders.Infrastructure` | `OrdersDbContext` | `__EFMigrationsHistory_Orders` |
| Inventory | `src/Modules/Inventory/FluxCommerce.Modules.Inventory.Infrastructure` | `InventoryDbContext` | `__EFMigrationsHistory_Inventory` |
| Payments | `src/Modules/Payments/FluxCommerce.Modules.Payments.Infrastructure` | `PaymentsDbContext` | `__EFMigrationsHistory_Payments` |

Example — create a migration for Catalog:

```powershell
dotnet ef migrations add InitialCatalog --project src/Modules/Catalog/FluxCommerce.Modules.Catalog.Infrastructure --startup-project src/FluxCommerce.Api
```

Example — apply Orders migrations:

```powershell
dotnet ef database update --project src/Modules/Orders/FluxCommerce.Modules.Orders.Infrastructure --startup-project src/FluxCommerce.Api
```

> Only the Identity module has migrations at the time of writing; the other modules will follow the same pattern (and each owns a dedicated database via `Database:ModuleConnections:*` in configuration).

## Common Problems

| Problem | Likely cause | Fix |
|---------|--------------|-----|
| `The command 'dotnet ef' was not found` | Global EF tool not installed | `dotnet tool install --global dotnet-ef --version 10.0.10`; reopen the shell |
| `No DbContext was found in assembly ...` | Wrong `--project` (e.g. pointed at Application instead of Infrastructure) | Point `--project` at the module's Infrastructure project (which holds the DbContext and factory) |
| `Unable to create a 'DbContext' ... factory` / `Design-time factory not found` | Factory class not discoverable | Every module has `DbContexts/*DesignTimeDbContextFactory.cs`; ensure `--startup-project src/FluxCommerce.Api` is passed, then `dotnet build` |
| `Migration 'X' already exists in project` | Migration name collision | Rename the migration in the `migrations add` command, or remove the existing one first (`migrations remove` when it's still the last, unapplied migration) |
| `An error was generated for the 'X' model` / `The model for context '...' has pending changes` | Model changed since the last migration | Create a new migration with `migrations add` — never hand-edit the snapshot |
| `Cannot connect to SQL Server` / `login failed` | SQL Server not running, or bad credentials | `docker compose up -d sqlserver`; verify the connection string — the password lives in `.env`/user secrets, not in source |
| `Connection string 'Database:ModuleConnections:Identity' is not configured` | Design-time factory could not resolve a connection string | Set it via user secrets: `dotnet user-secrets set "Database:ModuleConnections:Identity" "Server=localhost,1433;Database=FluxCommerce_Identity;User Id=sa;Password=...;TrustServerCertificate=True" --project src/FluxCommerce.Api`, or pass `--connection` |
| Command applies to the wrong module/database | Missing or wrong `--project` | Every command must target the module's Infrastructure project; each module has its own database and history table |

## Best Practices

Recommended workflow for model changes:

1. **Modify entities** — change the domain entities, DbContext configuration, or `OnModelCreating` in the module's Infrastructure project.
2. **Build** — `dotnet build`; the solution is warnings-as-errors, so fix anything red.
3. **Create migration** — `dotnet ef migrations add <DescriptiveName>` with `--project`/`--startup-project`, `-o Persistence/Migrations` for Identity. Name it for intent (e.g. `EnrichUserProfile`), not the date.
4. **Review the migration** — open the generated `.cs` file and verify `Up()`/`Down()` match the intent (expected tables, columns, indexes, data backfills). Check the snapshot diff in `migrations list`/`dbcontext info` if unsure.
5. **Apply migration** — `dotnet ef database update` against the local database.
6. **Test** — run `dotnet test`; add/extend integration tests for behavior affected by the change.

Additional rules:

- **Never edit generated migrations manually unless absolutely necessary** — if you must, edit both `Up()` and `Down()` symmetrically and say so in the pull request.
- **Never hand-edit the model snapshot** — always create a migration to reflect model changes, even if the database schema would not change.
- **Keep migrations additive when possible** — prefer adding columns/tables over destructive changes; write `Down()` carefully for data-affecting migrations.
- **One module at a time** — because history tables are per-module, apply and review each module's migrations independently.
- **Prefer scripts over direct updates in shared environments** — generate `--idempotent` SQL for staging/production.
