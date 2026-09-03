# Reliability Test Suite — FluxCommerce Phase 12

> **Phase 12.13 — Consolidated Regression Suite**
> All campaigns 12.2–12.12 are now dual-tagged `Reliability` and the regression slice is `ReliabilityRegression`.

## Structure

```
Common/FaultInjection/
  DbFaultInterceptor.cs               # SaveChanges + DbCommand fault injection
  TestConsumerCrashHarness.cs         # CrashBeforeCommit / CrashAfterCommitBeforeAck (AsyncLocal<CrashState> reference)
  BrokerFaultSimulator.cs             # ReturnFalse / ThrowPublishException / ThrowConnectionException

Reliability/
  ReliabilityTestBase.cs              # SQLite per-test isolation + hooks
  DatabaseFailureTests.cs             # 12.2 — 14 tests
  OutboxDispatcherFailureTests.cs     # 12.3 — 9 tests
  ConsumerCrashRedeliveryTests.cs     # 12.4 — 8 tests
  RetryPoisonDlqTests.cs              # 12.5 — 6 tests
  PaymentFailureTimeoutTests.cs       # 12.6 — 8 tests
  SagaFailureRecoveryTests.cs         # 12.7 — 9 tests
  InventoryConcurrencyFailureTests.cs # 12.8 — 5 tests
  ReservationExpiryFailureTests.cs    # 12.9 — 6 tests
  EndToEndFailureCampaignTests.cs     # 12.10 — 2 tests
  ObservabilityDiagnosticsTests.cs    # 12.11 — 5 tests
  ReliabilityInvariantsTests.cs       # 12.12 — 7 tests (reusable Assert* helpers)
  Regression/
    ReliabilityRegressionTests.cs     # 12.13 — 12 tests (all Phase 12 bugs) — Category=ReliabilityRegression
    README.md
```

`tests/Reliability/` (sibling) mirrors the same taxonomy for the suggested `Tests/Reliability/*` layout and contains per-category READMEs.

## Running

```bash
dotnet build FluxCommerce.slnx -c Debug                          # 0 warnings, 0 errors
dotnet test --filter Category=ReliabilityRegression              # 13 tests, ~18s, SQLite only
dotnet test --filter Category=Reliability                        # 92 tests, ~33s, SQLite only
dotnet test                                                      # unit 2277 + architecture 177 + integration 92 + reliability 92 = all green
```

## Boundaries

- Fault injectors are test-scoped, disabled by default, never in `src/`.
- No production business logic was altered in 12.13; only test-infra fixes for SQLite/owned-entity/AsyncLocal.
- Sub-Phase 12.14 (Final Audit) not started.
