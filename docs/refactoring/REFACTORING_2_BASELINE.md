# Refactoring Cycle 2 — Baseline & Safety Snapshot (R2.1)

> **Document purpose.** This report records the actual, observed state of the FluxCommerce codebase
> *before* any R2 refactoring begins. It is the R2.1 safety snapshot and the comparison point for
> R2.8 final verification. Everything in this document reflects what was observed and executed, not
> assumptions. **No production or test code was modified during this step.**

- **Cycle:** Refactoring Cycle 2 (R2.1 — Baseline & Safety Snapshot)
- **Date:** 2026-08-31
- **Executed by:** automated agent (opencode), per `docs/refactoring/REFACTORING_2_PLAN.md` §7
- **Scope:** R2.1 only — observation. No refactoring, renaming, file moves, or production/test code modifications.
- **Authoritative documents read:** `docs/refactoring/REFACTORING_2_PLAN.md`, `docs/PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`, `docs/adr/*`.

---

## 1. Repository State (R2.1 — Environment & Repository Snapshot)

### 1.1 Git snapshot

| Item | Value |
|------|-------|
| Branch | `refactoring` |
| HEAD commit | `790b07d` — "Merge pull request #16 from Mohamed-Elsefy/feature/reservation" |
| Recent history | `790b07d` (merge #16), `2ddf7e1` Implement Reservation Expiry Orchestration (Phase 11), `c51c008` Add Phase 11, `bb8942f` Merge #15 (payment), `1d7621e` Phase 10.14, `716d7f2` refund saga, `76aa66b` payment saga, `dbb2798` payment initiation, `17fdc71` Phase 9, `417a2eb` Stripe webhook & outbox |
| Working tree | Clean regarding tracked files — no staged or unstaged changes to tracked files |
| Untracked | `docs/refactoring/REFACTORING_2_PLAN.md` (plan for this cycle, not yet tracked) |
| Remote | `origin` → `https://github.com/Mohamed-Elsefy/FluxCommerce.git` (fetch/push) |

`git status` at inspection:

```text
On branch refactoring
Untracked files:
  (use "git add <file>..." to include in what will be committed)
	docs/refactoring/REFACTORING_2_PLAN.md

nothing added to commit but untracked files present (use "git add" to track)
```

No existing user changes were present in the working tree and none were discarded.

### 1.2 Solution structure

- **Solution file:** `FluxCommerce.slnx` (XML solution format).
- **Total projects:** **39** — verified by `Get-ChildItem -Recurse -Filter "*.csproj"` and the `.slnx` `<Project>` entries.
- **Framework:** `net10.0`; `global.json` requests SDK `10.0.302` (`rollForward: latestFeature`); installed SDK: **10.0.400**.
- **Strict build settings (`Directory.Build.props`):** `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `EnforceCodeStyleInBuild=true`, `GenerateDocumentationFile=true` (`CS1591` suppressed), `Deterministic=true`.
- **Central package management (`Directory.Packages.props`):** `ManagePackageVersionsCentrally=true`, `CentralPackageTransitivePinningEnabled=true`.

Project inventory (matches `FluxCommerce.slnx`):

| Area | Count | Projects |
|------|-------|----------|
| BuildingBlocks | 11 | `Application`, `Domain`, `Idempotency`, `Infrastructure`, `Localization`, `Messaging`, `Observability`, `Outbox`, `Persistence`, `Resilience`, `Web` |
| API host | 1 | `FluxCommerce.Api` |
| Modules | 24 | Cart, Catalog, Identity, Inventory, Orders, Payments — each: `Domain`, `Application`, `Infrastructure`, `Contracts` |
| Test projects | 3 | `FluxCommerce.ArchitectureTests`, `FluxCommerce.UnitTests`, `FluxCommerce.IntegrationTests` |
| **Total** | **39** | |

Source-file count (all `*.cs`):

| Area | `*.cs` files |
|------|-------------|
| `src/BuildingBlocks` | 169 |
| `src/FluxCommerce.Api` | 41 |
| `src/Modules` | 863 |
| `tests/Architecture` | 15 |
| `tests/IntegrationTests` | 88 |
| `tests/UnitTests` | 245 |
| **Total** | **1421** |

### 1.3 Documentation present

- Master spec: `docs/PROJECT_CONTEXT.md` (Phases 0–11 completed, R2 current).
- Architecture: `docs/ARCHITECTURE.md` (updated through Phase 11: §9.1 Payments, §14–15.2 consumers/saga/expiry, §26 localization).
- ADRs: `docs/adr/001`–`013` (001 modular monolith, 002 DB boundaries, 003 building blocks, 004 messaging, 005 observability, 006 catalog variant price, 007 integration event contracts, 008 consumer/idempotency, 009 saga, 010 payment ownership, 011 Stripe isolation, 012 payment intent idempotency, 013 webhook idempotency).
- Phase docs: `docs/phases/PHASE_0`–`PHASE_11` and `docs/completed/PHASE_0`–`PHASE_11`.
- Refactoring artifacts: `docs/refactoring/REFACTORING_1_BASELINE.md`, `REFACTORING_1_FINDINGS.md`, `REFACTORING_1_COMPLETION.md`, `REFACTORING_1_PLAN.md`, `REFACTORING_2_PLAN.md` (untracked).

### 1.4 Package versions (central, `Directory.Packages.props`)

| Package | Version |
|---------|---------|
| `StackExchange.Redis` | 3.1.0 |
| `RabbitMQ.Client` | 7.2.1 |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.10 |
| `Microsoft.Extensions.Options` | 10.0.10 |
| `Serilog` | 4.4.0 |
| `Serilog.AspNetCore` | 10.0.0 |
| `Serilog.Sinks.Console` | 6.1.1 |
| `OpenTelemetry` | 1.17.0 |
| `OpenTelemetry.Extensions.Hosting` | 1.17.0 |
| `OpenTelemetry.Exporter.Console` | 1.17.0 |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.17.0 |
| `OpenTelemetry.Instrumentation.Runtime` | 1.17.0 |
| `NetArchTest.Rules` | 1.3.2 |
| `Microsoft.NET.Test.Sdk` | 18.8.1 |
| `Moq` | 4.20.72 |
| `Stripe.net` | 42.3.0 |
| `xunit` | 2.9.3 |
| `xunit.runner.visualstudio` | 3.1.5 |
| `Microsoft.EntityFrameworkCore` | 10.0.10 |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.10 |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.10 |
| `System.IdentityModel.Tokens.Jwt` | 8.22.0 |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.10 |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 |
| `Swashbuckle.AspNetCore` | 10.2.3 |
| `MailKit` | 4.17.0 |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.12 |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.12 |

Per-project direct `PackageReference` usage archived in the agent log (see §1.2 project references summary in tool output); all version numbers are provided centrally.

### 1.5 Cross-module references (approved boundaries, observed)

- `FluxCommerce.Api` references all BuildingBlocks + all 6 module `Application`/`Contracts`/`Infrastructure` pairs (composition root).
- `Orders.Application` → `Cart.Contracts`, `Catalog.Contracts` (Phase 4.5 carve-out).
- `Orders.Infrastructure` → `Inventory.Contracts`, `Payments.Contracts` (`Integration`/`IntegrationEvents` only) — Phase 8/10/11 carve-outs, encoded in architecture tests.
- `Inventory.Contracts` and `Payments.Contracts` → `BuildingBlocks.Messaging` only — no `RabbitMQ.Client` or Outbox.
- No module references another module's `Infrastructure` or `DbContext` outside the above carve-outs. Verified later by Architecture tests 169/169.

### 1.6 Infrastructure configuration (files, not runtime)

| Area | File | Observed |
|------|------|----------|
| Docker | `docker-compose.yml` | `name: fluxcommerce`; services: `sqlserver` (2022-latest, health `sqlcmd -C`), `redis` (8-alpine, `appendonly yes`), `rabbitmq` (4-management, mgmt 15672), `smtp4dev` (2525/5000); volumes `sqlserver_data/redis_data/rabbitmq_data`; bridge network `fluxcommerce`; credentials via `${MSSQL_SA_PASSWORD}`, `${RABBITMQ_DEFAULT_USER/PASS}` from `.env` |
| API defaults | `src/FluxCommerce.Api/appsettings.json` | Empty defaults for `Database.ConnectionString`, `MessageBroker.*`, `Cache.ConnectionString`, `Jwt.SigningKey`, `Stripe.*`; `Database.CommandTimeoutSeconds=30`, `MaxRetryCount=3`; `Cart.KeyNamespace=cart`, `Cart.DefaultTtlMinutes=10080`; `Inventory.ReservationExpiration.Interval=00:00:30`, `BatchSize=100` |
| API dev | `src/FluxCommerce.Api/appsettings.Development.json` | `Database.ConnectionString` → `Server=localhost,1433;Database=FluxCommerce;...` plus `ModuleConnections` for Catalog/Orders/Inventory/Payments/Identity (`FluxCommerce_<Module>`); `Cache.ConnectionString=localhost:6379`; `MessageBroker.Host=localhost`, `Username=fluxcommerce`; `Jwt.Issuer/Audience=https://localhost:7119`; `Email.Host=localhost:2525`, `smtp4dev`; `Stripe` keys empty (externalized) |

### 1.7 EF Core migrations (on disk)

| DbContext | Migrations | Snapshot |
|-----------|------------|----------|
| `CatalogDbContext` | 7: `InitialCatalog`, `AddAttributeDefinitions`, `AddAttributeDefinitionLifecycle`, `AddProductAttributeValues`, `AddProductSpecifications`, `AddProductImages`, `AddVariantUnitPrice` | `CatalogDbContextModelSnapshot.cs` |
| `IdentityDbContext` | 4: `InitialIdentity`, `EnrichUserProfile`, `AdjustDisplayNameLength`, `AddTokenValueIndexes` | `IdentityDbContextModelSnapshot.cs` |
| `InventoryDbContext` | 5: `InitialInventory`, `AddOutboxMessages`, `AddOutboxRetryAfterUtc`, `AddProcessedMessages`, `AddOutboxTraceContext` | `InventoryDbContextModelSnapshot.cs` |
| `OrdersDbContext` | 9: `InitialOrders`, `AddOutboxMessages`, `AddOutboxRetryAfterUtc`, `AddOrderSummaryProjectionAndProcessedMessages`, `AddOrderSagaState`, `AddOrderSagaReservationIds`, `AddOrderSagaReleaseRequestedAt`, `AddOutboxTraceContext`, `AddPaymentToOrderSaga` | `OrdersDbContextModelSnapshot.cs` |
| `PaymentsDbContext` | 7: `InitialPayments`, `FixPaymentStatusAndId`, `AddProcessedWebhookEvents`, `AddPaymentsOutbox`, `AddPaymentCustomerId`, `AddPaymentsProcessedMessages`, `AddPaymentRefund` | `PaymentsDbContextModelSnapshot.cs` |

`dotnet ef migrations has-pending-model-changes` (against `FluxCommerce.Api` startup):

| DbContext | Result |
|-----------|--------|
| Catalog | No changes since last migration — in sync |
| Identity | No changes — in sync |
| Inventory | No changes — in sync |
| Orders | No changes — in sync |
| Payments | No changes — in sync |

Tool: `dotnet-ef 10.0.10`, SDK `10.0.400`.

---

## 2. Build Baseline (R2.1 — Build & Test Baseline Verification)

### 2.1 Restore

```bash
dotnet restore FluxCommerce.slnx
```

| Metric | Result |
|--------|--------|
| Result | ✅ Success |
| Output | `All projects are up-to-date for restore.` |

### 2.2 Build

```bash
dotnet build FluxCommerce.slnx -c Debug --verbosity minimal
```

| Metric | Result |
|--------|--------|
| Overall result | ✅ **Succeeded** |
| Errors | **0** |
| Warnings | **0** (`TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`) |
| Duration | **00:00:27.88** (this run; prior restore already up-to-date) |
| Log | `C:\Users\moham\AppData\Local\Temp\opencode\build.log` |

All 39 projects compiled. Sample tail of `build.log`:

```text
FluxCommerce.BuildingBlocks.Domain -> ...\FluxCommerce.BuildingBlocks.Domain.dll
FluxCommerce.Modules.Orders.Domain -> ...\FluxCommerce.Modules.Orders.Domain.dll
FluxCommerce.BuildingBlocks.Messaging -> ...\FluxCommerce.BuildingBlocks.Messaging.dll
FluxCommerce.Modules.Payments.Domain -> ...\FluxCommerce.Modules.Payments.Domain.dll
FluxCommerce.Modules.Orders.Infrastructure -> ...\FluxCommerce.Modules.Orders.Infrastructure.dll
FluxCommerce.Modules.Payments.Infrastructure -> ...\FluxCommerce.Modules.Payments.Infrastructure.dll
FluxCommerce.Modules.Inventory.Infrastructure -> ...\FluxCommerce.Modules.Inventory.Infrastructure.dll
FluxCommerce.Api -> ...\FluxCommerce.Api.dll
FluxCommerce.IntegrationTests -> ...\FluxCommerce.IntegrationTests.dll
FluxCommerce.UnitTests -> ...\FluxCommerce.UnitTests.dll
FluxCommerce.ArchitectureTests -> ...\FluxCommerce.ArchitectureTests.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:27.88
```

Contrast with R1.1 baseline (2026-08-13): build **FAILED** with 4 compile errors in `InventoryReservationExpirationTests.cs` — that file now compiles and the baseline is green.

---

## 3. Test Baseline (R2.1 — Build & Test Baseline Verification)

Tests were run **separately per suite** as required by the plan, plus a combined run for the integration flake.

### 3.1 Architecture tests

```bash
dotnet test tests/Architecture/FluxCommerce.ArchitectureTests/FluxCommerce.ArchitectureTests.csproj -c Debug --verbosity minimal
```

| Metric | Result |
|--------|--------|
| Total | **169** |
| Passed | **169** |
| Failed | **0** |
| Skipped | **0** |
| Duration | ~27 s |
| Exit code | **0** |
| Log | `C:\Users\moham\AppData\Local\Temp\opencode\arch.log` |

Previous baselines: R1.1 = 128/128; trajectory through Phase 11 reached 166/166; current **169/169** reflects the additional payments/expiry/compensation architecture rules added in Phases 9–11 (§14.6–15.2).

### 3.2 Unit tests

```bash
dotnet test tests/UnitTests/FluxCommerce.UnitTests/FluxCommerce.UnitTests.csproj -c Debug --verbosity minimal
```

| Metric | Result |
|--------|--------|
| Total | **2264** |
| Passed | **2264** |
| Failed | **0** |
| Skipped | **0** |
| Duration | ~36 s |
| Exit code | **0** |
| Log | `C:\Users\moham\AppData\Local\Temp\opencode\unit.log` |

Previous baselines: R1.1 = 1675/1675; Phase 11 docs cite 2095/2095; current **2264/2264** includes the Stripe/payments, saga, and expiration coverage added since R1.

### 3.3 Integration tests

```bash
dotnet test tests/IntegrationTests/FluxCommerce.IntegrationTests/FluxCommerce.IntegrationTests.csproj -c Debug --verbosity minimal
```

| Metric | Result (first full run) | Result (second full run) | Result (isolated filter) |
|--------|-------------------------|--------------------------|--------------------------|
| Total | **381** | **381** | **2** (filtered) |
| Passed | **380** | **380** | **2** |
| Failed | **1** | **1** | **0** |
| Skipped | **0** | **0** | **0** |
| Duration | ~2 m 25 s | ~2 m 06 s | ~2 s |
| Exit code | **1** | **1** | **0** |
| Log | `C:\Users\moham\AppData\Local\Temp\opencode\integration.log` | `C:\Users\moham\AppData\Local\Temp\opencode\integration2.log` | filtered run (no file) |

**Failing test (both full runs, same identity, different GUIDs — data-dependent):**

```text
FluxCommerce.IntegrationTests.OrdersOutboxStoreIntegrationTests
  .ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress [FAIL]
Assert.Equal() Failure: Collections differ
Expected: [3 ids]   Actual: [5 ids]   — two extra IDs (pos 1–2)
```

Stack:

```text
at OrdersOutboxStoreIntegrationTests.ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress()
  in tests/IntegrationTests/.../OrdersOutboxStoreIntegrationTests.cs:line 40 — Expected set
  at line 57 — assertion
```

**Isolation result:** `dotnet test --filter "ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress"` → **Passed 2/2, 0 failed** (the two parameterizations of the same test pass in isolation).

**Diagnosis (observed, not fixed per R2.1):** The failure is a **flaky shared-state contamination** when the full suite runs concurrently against the single shared SQL Server database (`Server=localhost,1433;Database=FluxCommerce_*`). The test expects exactly 3 pending `OutboxMessages` but reads 5, i.e., two residual rows left by another concurrently executing test that uses the same `Orders` database. The isolated run passes. No code was changed. This pattern was not present in the documented Phase 11 baseline (376/376) but is deterministic across two consecutive full runs in this environment (380/381, same single test).

The remaining **380 integration tests** passed, covering:

- Catalog / Identity / Checkout (SQLite `WebApplicationFactory`)
- Cart (Redis — PONG, exercised)
- Inventory concurrency (`SqlServerInventoryTestFixture` — real SQL Server)
- Outbox / RabbitMQ messaging (real SQL Server + RabbitMQ)
- Consumer / idempotency / `ProcessedMessages` (real SQL + RabbitMQ)
- Saga orchestration (15 mandatory saga scenarios — see `docs/ARCHITECTURE.md` §15)
- Stripe webhook / outbox (real SQL + SQLite payment paths)
- Payment saga / compensation
- Reservation expiry orchestration

### 3.4 Summary vs. prior baselines

| Suite | R1.1 (2026-08-13) | Phase 11 docs (`PROJECT_CONTEXT.md` §14.x) | **R2.1 observed (2026-08-31)** |
|-------|-------------------|---------------------------------------------|--------------------------------|
| Architecture | 128/128 (41 s) | 166/166 | **169/169** (27 s) — 0 failed |
| Unit | 1675/1675 (23 s) | 2095/2095 | **2264/2264** (36 s) — 0 failed |
| Integration | Not runnable (compile errors) | 376/376 | **380/381** (≤2 m 25 s) — **1 flaky** |

Total executed in R2.1: **2814** tests run (169 + 2264 + 381), **2813 passed, 1 failed (flaky)**, 0 skipped. Net coverage is up across all suites since R1.1; the only red is the single shared-DB flaky test above.

---

## 4. Infrastructure Baseline (R2.1 — Verification)

Verification was **observation-only**; no infrastructure was introduced or modified.

### 4.1 Docker Compose (`fluxcommerce`)

All four services **Up and healthy** (probed 2026-08-31, ~2 h uptime, 4 weeks since creation):

| Service | Container | Image | Ports | Status |
|---------|-----------|-------|-------|--------|
| `sqlserver` | `fluxcommerce-sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | `0.0.0.0:1433->1433` | **Up 2 hours (healthy)** — `sqlcmd -C` healthcheck 10s/5s/10 retries |
| `redis` | `fluxcommerce-redis` | `redis:8-alpine` | `0.0.0.0:6379->6379` | **Up 2 hours (healthy)** — `redis-cli ping` |
| `rabbitmq` | `fluxcommerce-rabbitmq` | `rabbitmq:4-management` | `0.0.0.0:5672->5672`, `15672->15672` | **Up 2 hours (healthy)** — `rabbitmq-diagnostics ping` |
| `smtp4dev` | `fluxcommerce-smtp4dev` | `rnwood/smtp4dev:latest` | `0.0.0.0:2525->25`, `5000->80` | **Up 2 hours** (no healthcheck) |

`docker compose ps` / `docker ps -a` both confirm `fluxcommerce` stack with `bridge` network `fluxcommerce` and named volumes `sqlserver_data`, `redis_data`, `rabbitmq_data`.

### 4.2 Per-service connectivity (direct probe)

| Service | Probe | Result |
|---------|-------|--------|
| SQL Server | `Test-NetConnection localhost:1433` | **TcpTestSucceeded: True** |
| Redis | `Test-NetConnection localhost:6379` | **TcpTestSucceeded: True** |
| RabbitMQ | `Test-NetConnection localhost:5672` | **TcpTestSucceeded: True** |
| SMTP | container `fluxcommerce-smtp4dev` Up | Reachable on 2525/5000 |

EF-level connectivity is additionally proven by the passing SQL-backed integration tests (Inventory concurrency, Outbox, Saga, Payments) that migrated and exercised the real `FluxCommerce_*` databases during the integration run.

### 4.3 SQL Server databases (from config, not enumerated via `sqlcmd` in this run)

Configured module databases (from `appsettings.Development.json` `Database.ModuleConnections`):

```text
FluxCommerce_Catalog
FluxCommerce_Orders
FluxCommerce_Inventory
FluxCommerce_Payments
FluxCommerce_Identity
```

Plus the default `FluxCommerce` database. All five were referenced by the passing integration suite.

### 4.4 Which tests require which infrastructure

| Infrastructure | Required by | Verified |
|----------------|-------------|----------|
| **SQL Server** | Inventory concurrency (`SqlServerInventoryTestFixture`), Outbox/ProcessedMessage/Saga/Payments integration tests (real engine mandatory, not SQLite) — including the flaky `OrdersOutboxStoreIntegrationTests` pending-claim test | **Reachable & healthy; exercised by 380 passing integration tests** |
| **Redis** | Cart integration/Redis persistence tests (`RedisCartFixture`, fail-fast) and the Cart module runtime | **Reachable & healthy; PONG via TCP probe; exercised by passing suite** |
| **RabbitMQ** | Outbox dispatcher + idempotent consumer tests (real broker), durable topic exchange `payments.*` / `orders.*` / `inventory.*`, DLX/DLQ topology, consumer retry/backoff | **Reachable & healthy (`diagnostics ping`); exercised by passing suite** |
| **SMTP (smtp4dev)** | Email verification flows (Identity) | **Container Up; not exercised in this snapshot** |

SQLite-backed suites (Identity, Catalog, Checkout) use `WebApplicationFactory<Program>` with fakes and do not require the containers; they are included in the passing counts above.

---

## 5. Items Not Verifiable / Known Findings

1. **Integration flake — `ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress`** (1/381). Recorded as the sole failure. Full-suite runs fail with 2 extra pending IDs (shared-DB contamination); isolated filter passes 2/2. Success rate **99.7%** on full suite. Root-cause isolation is deferred to R2.2+ (likely test isolation / `Database.EnsureDeleted`/transaction cleanup gap in the Orders outbox test fixture). R2.8 must re-verify against this baseline with a clean DB (or with the test fixture hardened) before closing the cycle.

2. **Stripe external calls** — verified via the deterministic `IStripePaymentGateway` / `IStripeWebhookService` boundary and the `intent_{OrderId}_{PaymentId}_{Version}` idempotency-key contract, not via live Stripe. No live Stripe credentials are present (`appsettings.*` `Stripe.*` empty/externalized).

3. **smtp4dev email delivery** — container Up but not exercised by the baseline runs (email content is asserted via mocks in unit tests).

4. **Pending EF model changes** — **none** for Catalog, Identity, Inventory, Orders, Payments (all 5 DbContexts report "No changes ... since last migration").

---

## 6. Definition of Done — R2.1 Status

| Criterion (per `REFACTORING_2_PLAN.md` §7) | Status |
|--------------------------------------------|--------|
| Git status / solution structure / project count recorded | ✅ Verified (§1.1–1.2 — 39 projects, 1421 `*.cs` files) |
| Package references / infrastructure configuration recorded | ✅ Verified (§1.4–1.6) |
| EF Core migrations / pending-model-changes checked | ✅ Verified — 0 pending across all 5 DbContexts (§1.7) |
| `dotnet restore` executed and recorded | ✅ All up-to-date (§2.1) |
| `dotnet build FluxCommerce.slnx -c Debug` executed and recorded | ✅ **Succeeded, 0 warnings, 0 errors** (§2.2) |
| Architecture tests executed and recorded | ✅ **169/169** (§3.1) |
| Unit tests executed and recorded | ✅ **2264/2264** (§3.2) |
| Integration tests executed and recorded | ✅ **380/381 — 1 flaky** (§3.3) |
| SQL Server / Redis / RabbitMQ / Docker verified | ✅ All reachable & healthy (§4) |
| Baseline document created at `docs/refactoring/REFACTORING_2_BASELINE.md` | ✅ This report |
| R2.1 observation boundary respected (no refactoring) | ✅ **No application/test code modified** |

---

## 7. Headline Baseline Summary

| Area | Observed result |
|------|-----------------|
| Git branch | `refactoring` — clean tracked tree, HEAD `790b07d`, untracked `REFACTORING_2_PLAN.md` only |
| Solution | `FluxCommerce.slnx`, **39 projects** (11 BuildingBlocks + 1 Api + 24 Modules + 3 Tests), `net10.0`, SDK `10.0.400`, `TreatWarningsAsErrors=true` |
| Restore | ✅ All up-to-date |
| Build (`Debug`) | ✅ **Succeeded — 0 warnings, 0 errors** — 00:00:27.88 |
| Architecture tests | ✅ **169 passed / 0 failed / 0 skipped** (~27 s) |
| Unit tests | ✅ **2264 passed / 0 failed / 0 skipped** (~36 s) |
| Integration tests | ⚠️ **380 passed / 1 failed / 0 skipped** (99.7%) — `OrdersOutboxStoreIntegrationTests.ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress` flaky on full suite, passes in isolation (`381` total; `76aa66b`–`790b07d` saga/payment/expiry suite otherwise green) |
| Total executed | **2814 run, 2813 passed, 1 flaky failed** |
| EF model sync | ✅ **0 pending model changes** across Catalog/Identity/Inventory/Orders/Payments |
| SQL Server | ✅ Reachable (1433), healthy, exercised by integration suite |
| Redis | ✅ Reachable (6379), PONG, exercised by Cart suite |
| RabbitMQ | ✅ Reachable (5672/15672), healthy, exercised by Outbox/consumer suite |
| Docker Compose | ✅ `fluxcommerce` stack — sqlserver/redis/rabbitmq/smtp4dev Up |

> **Safety note for subsequent cycles.** R2.1 is green on build and on Architecture/Unit. The sole integration failure is the deterministic flake on `OrdersOutboxStoreIntegrationTests.ClaimPendingAsync_ReturnsPendingMessages_AndMarksThemInProgress` (extra 2 pending rows when the full suite runs concurrently). Any refactoring must be compared against this snapshot: the R2.8 final verification must either (a) reproduce the same 380/381 result, or (b) record a deliberate hardening of that fixture that turns the run to 381/381. No other behavioral change should be attributed to R2 without explicit approval per `REFACTORING_2_PLAN.md` §6.
