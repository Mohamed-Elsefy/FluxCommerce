# Refactoring Cycle 1 — Baseline & Safety Snapshot (R1.1)

> **Document purpose.** This report records the actual, observed state of the FluxCommerce codebase
> *before* any refactoring begins. It is the R1.1 safety snapshot and the comparison point for
> R1.8 final verification. Everything in this document reflects what was observed and executed, not
> assumptions.

- **Cycle:** Refactoring Cycle 1 (R1.1 — Baseline & Safety Snapshot)
- **Date:** 2026-08-13
- **Executed by:** automated agent (opencode), per `docs/refactoring/REFACTORING_1_PLAN.md`
- **Scope:** R1.1 only. No application code was modified, no warnings/errors were fixed, no
  infrastructure was introduced or modified, and no R1.2+ work was started.
- **Authoritative documents read:** `docs/refactoring/REFACTORING_1_PLAN.md`,
  `docs/PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`.

---

## 1. Repository State (R1.1.1)

### 1.1 Git snapshot

| Item | Value |
|------|-------|
| Branch | `refactoring-1` |
| HEAD commit | `ba0aeaf` — "Merge pull request #9 from Mohamed-Elsefy/feature/inventory" |
| Working tree | Clean — **no uncommitted changes to tracked files** |
| Untracked | `docs/refactoring/` (created this cycle; contains the plan and this baseline report) |

`git status --short` at the time of inspection:

```text
?? docs/refactoring/
```

No existing user changes were present in the working tree and none were discarded or overwritten.

### 1.2 Solution structure

- Solution file: `FluxCommerce.slnx` (XML solution format), **39 projects**.
- Framework: `net10.0`; `global.json` requests SDK `10.0.302` (`rollForward: latestFeature`);
  installed SDK: **10.0.303**.
- Strict build settings (`Directory.Build.props`): nullable enabled, `TreatWarningsAsErrors=true`,
  `EnforceCodeStyleInBuild=true`, XML docs generated (`CS1591` suppressed globally).

Project inventory:

| Area | Projects |
|------|----------|
| BuildingBlocks | 11 (`Application`, `Domain`, `Idempotency`, `Infrastructure`, `Localization`, `Messaging`, `Observability`, `Outbox`, `Persistence`, `Resilience`, `Web`) |
| API host | 1 (`FluxCommerce.Api`) |
| Modules | 24 — Cart, Catalog, Identity, Inventory, Orders, Payments (each: `Domain`, `Application`, `Infrastructure`, `Contracts`) |
| Test projects | 3 — `FluxCommerce.ArchitectureTests`, `FluxCommerce.UnitTests`, `FluxCommerce.IntegrationTests` |

### 1.3 Existing documentation

- Master specification: `docs/PROJECT_CONTEXT.md` (Phases 0–5 completed, Cycle 1 current).
- Architecture: `docs/ARCHITECTURE.md`.
- ADRs: `docs/adr/001`–`006`.
- Phase docs under `docs/phases/` and `docs/completed/` (0, 1, 1.5, 2, 3, 4, 5).
- Refactoring artifacts: `docs/refactoring/REFACTORING_1_PLAN.md` (this cycle's plan).

---

## 2. Build Baseline (R1.1.2)

Command:

```text
dotnet build FluxCommerce.slnx -c Debug
```

| Metric | Result |
|--------|--------|
| Overall result | **FAILED** |
| Errors | **4** (all in the Integration Tests project) |
| Warnings | **0** |
| Build duration | **00:02:00.61** |

### 2.1 Compile errors (all four)

| # | Code | Location | Message |
|---|------|----------|---------|
| 1 | CS1026 | `tests/IntegrationTests/FluxCommerce.IntegrationTests/InventoryReservationExpirationTests.cs:32` | `)` expected |
| 2 | CS1003 | same file `:132` | Syntax error, `,` expected |
| 3 | CS1026 | same file `:132` | `)` expected |
| 4 | CS1519 | same file `:135` | Invalid token `}` in a member declaration |

### 2.2 Root cause (observed, not fixed)

`InventoryReservationExpirationTests.cs` contains **malformed/truncated test bodies** in two test
methods:

1. `PendingReservationPastDeadline_IsExpired_AndStockReleased` (starts line 23) — the
   `Assert.True(expired >= 1, ...)` call at line 31–32 is never closed; it is followed by
   `Assert.Equal(0, item.ReservedQuantity);` referencing a variable `item` that is never declared in
   that scope.
2. `MultipleExpiredReservations_AllProcessed` (starts line 119) — the `Assert.True(expired >= 3, ...)`
   call at line 131 is never closed and its body references out-of-scope variables (`item`, `sku`).

The file is a **tracked, committed** file (last touched in commit `ee5aa72` "Add Phase 5 docs and
stabilize inventory expiration tests"); it is not a working-tree change.

### 2.3 Projects affected

- **Failed:** `FluxCommerce.IntegrationTests` (compile).
- **Compiled successfully:** all 38 remaining projects — 11 BuildingBlocks, `FluxCommerce.Api`, all
  24 module projects, `FluxCommerce.UnitTests`, `FluxCommerce.ArchitectureTests`.

### 2.4 Status

The solution **does not currently build**. This is the verified baseline state. Per R1.1 scope,
the errors were documented and intentionally **not fixed**.

---

## 3. Test Baseline (R1.1.3)

### 3.1 Architecture tests

Command:

```text
dotnet test tests/Architecture/FluxCommerce.ArchitectureTests/FluxCommerce.ArchitectureTests.csproj -c Debug
```

| Metric | Result |
|--------|--------|
| Total | **128** |
| Passed | **128** |
| Failed | **0** |
| Skipped | **0** |
| Test execution duration | ~41 s |
| Exit code | 0 |

### 3.2 Unit tests

Command:

```text
dotnet test tests/UnitTests/FluxCommerce.UnitTests/FluxCommerce.UnitTests.csproj -c Debug
```

| Metric | Result |
|--------|--------|
| Total | **1675** |
| Passed | **1675** |
| Failed | **0** |
| Skipped | **0** |
| Test execution duration | ~23 s |
| Exit code | 0 |

Note: the Unit test project contains the Cart integration test collection backed by **real Redis**
(`RedisCartFixture`), which fails fast when Redis is unreachable. The suite therefore exercised real
Redis and passed.

### 3.3 Integration tests

Command:

```text
dotnet test tests/IntegrationTests/FluxCommerce.IntegrationTests/FluxCommerce.IntegrationTests.csproj -c Debug
```

| Metric | Result |
|--------|--------|
| Total executed | **0** |
| Passed | **0** |
| Failed | **0** (0 executed) |
| Skipped | **0** |
| Build | **FAILED** (same 4 compile errors as §2) |
| Exit code | 1 |

The Integration Tests project **cannot be built**, so **no integration tests could be executed**.
The intended suite (from static source inspection — not executed):

- 46 `.cs` test files,
- 233 `[Fact]` + 6 `[Theory]` = **239 test cases statically present**.

The blocked tests include the **mandatory Phase 5 SQL Server concurrency tests**
(`SqlServerInventoryTestFixture`, e.g. concurrent reservation / overselling protection and
reservation-expiration recovery) which require the real SQL Server engine.

### 3.4 Comparison with prior documented results

The completed-phase docs (e.g. `PROJECT_CONTEXT.md` §14.4) previously reported different totals
(e.g. Architecture 103/103, Unit 1,497/1,497, Integration 207/207 at Phase 4 close). The counts
above are the **actual numbers observed on this run** and supersede any historical figures for the
baseline.

---

## 4. Infrastructure Baseline (R1.1.4)

Verification was **observation-only**; no infrastructure was introduced or modified.

### 4.1 Docker Compose (`fluxcommerce`)

All four services were **Up and healthy** at verification time:

| Service | Image | Ports | Container status |
|---------|-------|-------|------------------|
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 | Up (healthy) |
| `redis` | `redis:8-alpine` | 6379 | Up (healthy) |
| `rabbitmq` | `rabbitmq:4-management` | 5672, 15672 | Up (healthy) |
| `smtp4dev` | `rnwood/smtp4dev:latest` | 2525, 5000 | Up |

### 4.2 Per-service connectivity (direct probe)

| Service | Probe | Result |
|---------|-------|--------|
| Redis | `redis-cli ping` | `PONG` |
| RabbitMQ | `rabbitmq-diagnostics -q ping` | `Ping succeeded` |
| SQL Server | `sqlcmd -b -Q "SELECT @@VERSION"` (sa) | Reachable — SQL Server 2022 Developer Edition (RTM-CU26, build 16.0.4265.3) |

### 4.3 SQL Server databases present

```text
FluxCommerce_Catalog
FluxCommerce_Identity
FluxCommerce_Inventory
FluxCommerce_Orders
```

Note: `FluxCommerce_Payments` does **not** exist yet (Payments is a skeleton module at this stage).
The Inventory concurrency test fixture migrates its own schema (`FluxCommerce_Inventory`) on startup.

### 4.4 Which tests require which infrastructure

| Infrastructure | Required by | Verified |
|----------------|-------------|----------|
| SQL Server | Inventory concurrency integration tests (`SqlServerInventoryTestFixture` — real engine mandatory, not SQLite) | Reachable & healthy |
| Redis | Cart integration tests in the Unit project (`RedisCartFixture`, fail-fast) | Reachable & healthy; exercised by the green Unit run |
| RabbitMQ | No test directly exercises RabbitMQ; the API host registers a lazy connection provider + health check | Reachable & healthy |

Most other integration suites (Identity, Catalog, Checkout) override their persistence with
in-memory SQLite and replace external contracts with fakes (`WebApplicationFactory<Program>`),
so they do not require the containers.

---

## 5. Items Not Verifiable

These are explicitly recorded because they could not be verified during R1.1:

1. **Integration test results** (passed/failed/skipped counts) — could not be obtained because the
   Integration Tests project does not compile (§2, §3.3).
2. **Inventory concurrency behavior** on real SQL Server — the Phase 5 concurrency/expiration tests
   are part of the blocked Integration suite; SQL Server itself is confirmed reachable.
3. **RabbitMQ message flow** — no current test exercises messaging; only broker connectivity was
   verified. (Outbox/RabbitMQ business workflows are Phase 6+ and explicitly out of this cycle's
   scope.)
4. **Payments database** — `FluxCommerce_Payments` is not created; consistent with Payments being a
   skeleton module.

---

## 6. Definition of Done — R1.1 Status

| Criterion | Status |
|-----------|--------|
| Current repository state is understood | ✅ Verified (§1) |
| Build status is known | ✅ Known — **FAILED**, 4 compile errors in Integration Tests (§2) |
| Test status is known | ✅ Architecture 128/128, Unit 1675/1675; Integration not runnable (§3) |
| Infrastructure status is known | ✅ SQL Server, Redis, RabbitMQ reachable & healthy (§4) |
| Baseline results are documented | ✅ This report |

---

## 7. Headline Baseline Summary

| Area | Observed result |
|------|-----------------|
| Git branch | `refactoring-1` (clean tree, HEAD `ba0aeaf`) |
| Solution | `FluxCommerce.slnx`, 39 projects, net10.0 |
| Build | ❌ **Failed** — 4 compile errors in `InventoryReservationExpirationTests.cs`; 0 warnings; 00:02:00.61 |
| Architecture tests | ✅ 128 passed / 0 failed / 0 skipped (~41 s) |
| Unit tests | ✅ 1675 passed / 0 failed / 0 skipped (~23 s) |
| Integration tests | ❌ **Not runnable** (project does not compile); 239 test cases statically present |
| SQL Server | ✅ Reachable, healthy; module DBs present |
| Redis | ✅ PONG; exercised by Unit suite |
| RabbitMQ | ✅ Reachable, healthy (not exercised by tests) |

> **Safety note for subsequent cycles.** The build is currently red solely because of the broken
> `InventoryReservationExpirationTests.cs` file. Any refactoring work must either preserve this
> documented failure or address it as a separately approved finding — it must not be silently
> "fixed" as part of an unrelated change, and the R1.8 final verification must be compared against
> this snapshot.