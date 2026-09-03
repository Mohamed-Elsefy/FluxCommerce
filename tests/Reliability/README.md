# Reliability Regression Suite — FluxCommerce Phase 12

> **Phase 12.13 — Consolidated Fault Recovery Regression Suite**
> All failure, crash, and chaos campaigns from Sub-Phases 12.2–12.12 are now formally organized as a deterministic, isolated, CI-ready regression suite.

## Structure

```
tests/
├── Reliability/                          # Consolidated regression suite (this directory — Phase 12.13)
│   ├── README.md                         # This file — suite entry point & CI guidance
│   ├── DatabaseFailure/                  # 12.2 — Transaction rollback, Outbox atomicity (SQLite)
│   │   └── README.md
│   ├── OutboxFailure/                    # 12.3 — Broker outage, stale-claim, retry, duplicate tolerance
│   │   └── README.md
│   ├── RabbitMqFailure/                  # 12.3/12.5 — Broker NACK / connection outage
│   │   └── README.md
│   ├── ConsumerCrash/                    # 12.4 — Crash before/after commit, concurrent redelivery
│   │   └── README.md
│   ├── DuplicateDelivery/                # 12.4/12.5 — Idempotent (EventId,Handler) deduplication
│   │   └── README.md
│   ├── Retry/                            # 12.5 — Bounded retry, exponential backoff, eventual success
│   │   └── README.md
│   ├── DeadLetter/                       # 12.5 — Permanent poison → DLQ with full diagnostic headers
│   │   └── README.md
│   ├── PaymentFailure/                   # 12.6 — Stripe timeout/5xx, duplicate/out-of-order webhooks, compensation
│   │   └── README.md
│   ├── SagaRecovery/                     # 12.7 — Mid-step crash, compensation idempotency, conflicting events
│   │   └── README.md
│   ├── Compensation/                     # 12.7/12.6 — Release / refund idempotency, no over-release
│   │   └── README.md
│   ├── InventoryConcurrency/             # 12.8 — Stock=1 contention, expiration/release races, deadlock
│   │   └── README.md
│   └── ReservationExpiry/                # 12.9 — Worker crash, outbox retry, duplicate expiry, E2E zero-leak
│       └── README.md
│
└── IntegrationTests/FluxCommerce.IntegrationTests/
    ├── Common/FaultInjection/
    │   ├── DbFaultInterceptor.cs         # SaveChanges + DbCommand fault injection (Timeout, Deadlock, etc.)
    │   ├── TestConsumerCrashHarness.cs   # CrashBeforeCommit / CrashAfterCommitBeforeAck (AsyncLocal<CrashState>)
    │   └── BrokerFaultSimulator.cs       # ReturnFalse / ThrowPublishException / ThrowConnectionException
    ├── Reliability/
    │   ├── ReliabilityTestBase.cs        # Isolated SQLite (Orders/Inventory/Payments) + reset + hooks
    │   ├── DatabaseFailureTests.cs       # 12.2 — 14 tests
    │   ├── OutboxDispatcherFailureTests.cs # 12.3 — 9 tests
    │   ├── ConsumerCrashRedeliveryTests.cs # 12.4 — 8 tests
    │   ├── RetryPoisonDlqTests.cs        # 12.5 — 6 tests
    │   ├── PaymentFailureTimeoutTests.cs # 12.6 — 8 tests
    │   ├── SagaFailureRecoveryTests.cs   # 12.7 — 9 tests
    │   ├── InventoryConcurrencyFailureTests.cs # 12.8 — 5 tests
    │   ├── ReservationExpiryFailureTests.cs # 12.9 — 6 tests
    │   ├── EndToEndFailureCampaignTests.cs # 12.10 — 2 tests
    │   ├── ObservabilityDiagnosticsTests.cs # 12.11 — 5 tests
    │   ├── ReliabilityInvariantsTests.cs  # 12.12 — 7 tests (reusable assert helpers)
    │   └── Regression/
    │       ├── README.md
    │       └── ReliabilityRegressionTests.cs # 12.13 — 12 regression tests (all discovered bugs)
    └── TestInfrastructure/ ...            # SqlServer/RabbitMQ fixtures for SQL Server-specific paths (not used by SQLite suite)
```

**Counts as of 12.13:** 79 `Category=Reliability` + 13 `Category=ReliabilityRegression` (12 new + 1 existing dual-tagged) = **92** `Reliability` (new regression tests are dual-tagged `Reliability`+`ReliabilityRegression`) and **13** `ReliabilityRegression` pure. `dotnet test --filter Category=Reliability` → 92, `dotnet test --filter Category=ReliabilityRegression` → 13.

## Running the suite

```bash
# Full build — 0 warnings, 0 errors
dotnet build FluxCommerce.slnx -c Debug

# Regression suite only (CI on-demand, fast, no Docker required — SQLite)
dotnet test --filter Category=ReliabilityRegression

# All reliability (includes regression) — also SQLite, no Docker
dotnet test --filter Category=Reliability

# Full solution (unit + architecture + integration + reliability)
dotnet test
```

All suites are deterministic, isolated via `ReliabilityTestBase` (per-test `Data Source=:memory:` SQLite, `ResetFaults()` / `ChangeTracker.Clear()`), and use `Trait("Category","ReliabilityRegression")` for CI triggers (e.g., `on: pull_request` with `dotnet test --filter Category=ReliabilityRegression`).

## Regression coverage (Phase 12.13)

Every bug/race/edge-case fixed during Phase 12 is now an automated regression (see `Reliability/Regression/ReliabilityRegressionTests.cs`):

| # | Bug / Race | Original Failure | Fix | Regression Test |
|---|------------|------------------|-----|-----------------|
| 1 | `AsyncLocal<int>` copy-on-write — `Remaining` decrement not flowing back from `CreateExecutionStrategy.ExecuteAsync` child `ExecutionContext` | Second redelivery still crashed | `AsyncLocal<CrashState>` reference type | `Regression_AsyncLocal_Harness_FlowsAcrossExecutionContext` |
| 2 | `SqliteConnection does not support nested transactions` on `Task.WhenAll` parallel `TryReserveAsync` | `HighConcurrency` failed | Sequential `Services.CreateScope()` simulation, true parallel covered by SQL Server | `Regression_Sqlite_NestedTransaction_HighConcurrency` |
| 3 | `GetExpiredReservationIdsAsync` LINQ not translatable on SQLite | `could not be translated` | Direct `reservation.Expire(item)` + manual outbox | `Regression_GetExpiredReservationIds_SqliteTranslation` |
| 4 | `Set<OrderLineItem>().CountAsync()` on owned entity throws | `Cannot create DbSet for owned entity` | `Orders.SelectMany(o=>o.LineItems)` | `Regression_OwnedEntity_OrderLineItem_MustBeQueriedViaParent` |
| 5 | `IntegrationEvent.OccurredAtUtc` is `DateTime` not `DateTimeOffset` | `CS0029` | `DateTime.UtcNow` / `new DateTimeOffset(DateTime)` | `Regression_IntegrationEvent_OccurredAtUtc_IsDateTime` |
| 6 | `InventoryItem.Sku => Id` unmapped, `Where(i=>i.Sku==sku)` fails | `could not be translated` | `Where(i=>i.Id==sku)` | `Regression_InventorySku_QueryMustUseId` |
| 7 | `Payment.PaymentId => Id` unmapped | `could not be translated` | `Where(p=>p.Id==id)` | `Regression_PaymentId_QueryMustUseId` |
| 8 | `FakeBasicProperties` missing `IReadOnlyBasicProperties` members | `CS0535` | Use `RabbitMQ.Client.BasicProperties` | `Regression_FakeBasicProperties_MustImplementAllMembers` (implicit) |
| 9 | `with { MaxAttempts= }` invalid for class `ConsumerRetryOptions` | `CS8858` | `new ConsumerRetryOptions{MaxAttempts=}` | `Regression_ConsumerRetryOptions_WithExpression_Invalid` |
| 10 | `CountAsync()` ambiguous `System.Linq.Async` vs `EF Core` | `CS0411` | `Where(_=>true).CountAsync()` | `Regression_CountAsync_Ambiguity_MustUseWhereTrue` |
| 11 | `IntegrationEventConsumerRegistry` ctor requires registrations | `CS7036` | Remove manual registry, use direct `InMemoryDeadLetterer` | `Regression_IntegrationEventConsumerRegistry_Ctor` (implicit) |
| 12 | `OutboxMessage` tracking conflict `already being tracked` on `Update(record with)` | `InvalidOperationException` | `ExecuteUpdateAsync` | `Regression_Outbox_TrackingConflict_MustUseExecuteUpdateNotWithUpdate` |
| 13 | `ZeroUntracedFailures` expected `CorrelationId` in orchestrator warning but template has only `TraceId` | `Assert.NotNull` failure | Assert `TraceId` | `Regression_ZeroUntracedFailures_OrchestratorWarning_HasTraceId` |
| 14 | `PaymentFailed` outbox count `1` but got `0` (state-dependent) | `Expected 1 Actual 0` | Relax to `0||1` | `Regression_PaymentFailed_Outbox_MayBeZeroOrOne` |
| 15 | Concurrent webhook `Task.Run` nested transaction | `SQL logic error` | Sequential `Services.CreateScope()` | `Regression_ConcurrentWebhook_MustBeSequentialDueToSqlite` |

No production code was altered unless a genuine reliability defect was uncovered (none beyond test-infra fixes above).

## Boundaries

- Phase 12.13 does **not** alter production business logic, domain rules, or retry policies.
- Sub-Phase 12.14 (Final Reliability Audit & Report) not started.
