# EF Core Commands

## Variables

```text
<InfrastructureProject> = src/Modules/<Module>/FluxCommerce.Modules.<Module>.Infrastructure
<StartupProject>        = src/FluxCommerce.Api
```

Replace `<Module>` with the desired module name (Identity, Catalog, Cart, Orders, Inventory, Payments, ...).

---

## Create Initial Migration

```bash
dotnet ef migrations add Initial<Module> --project <InfrastructureProject> --startup-project <StartupProject> -o Persistence/Migrations
```

---

## Create Migration

```bash
dotnet ef migrations add <MigrationName> --project <InfrastructureProject> --startup-project <StartupProject> -o Persistence/Migrations
```

---

## Remove Migration

```bash
dotnet ef migrations remove --project <InfrastructureProject> --startup-project <StartupProject
```

---

## Update Database

```bash
dotnet ef database update --project <InfrastructureProject> --startup-project <StartupProject>
```

---

## Rollback to Previous Migration

```bash
dotnet ef database update <PreviousMigration> --project <InfrastructureProject> --startup-project <StartupProject>
```

---

## Reset Database (Remove All Migrations)

```bash
dotnet ef database update 0 --project <InfrastructureProject> --startup-project <StartupProject>
```

---

## List Migrations

```bash
dotnet ef migrations list --project <InfrastructureProject> --startup-project <StartupProject>
```

---

## Generate SQL Script

```bash
dotnet ef migrations script --project <InfrastructureProject> --startup-project <StartupProject> -o <Module>.sql
```

---

## Generate Idempotent SQL Script

```bash
dotnet ef migrations script --idempotent --project <InfrastructureProject> --startup-project <StartupProject> -o <Module>.sql
```

---

## Drop Database

```bash
dotnet ef database drop --project <InfrastructureProject> --startup-project <StartupProject>
```

---

## DbContext Information

```bash
dotnet ef dbcontext info --project <InfrastructureProject> --startup-project <StartupProject>
```

---

## List DbContexts

```bash
dotnet ef dbcontext list --project <InfrastructureProject> --startup-project <StartupProject>
```

---

# General Commands

## Install EF CLI

```bash
dotnet tool install --global dotnet-ef --version 10.0.10
```

## EF Version

```bash
dotnet ef --version
```

## Build Solution

```bash
dotnet build
```

## Run Tests

```bash
dotnet test
```

## Clean Solution

```bash
dotnet clean
```

---

# Examples

## Identity

```text
<Module> = Identity
```

## Catalog

```text
<Module> = Catalog
```

## Orders

```text
<Module> = Orders
```

## Payments

```text
<Module> = Payments
```
