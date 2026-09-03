# Reliability Regression Suite — Phase 12.13

This folder is the **CI-ready regression entry point** for `Category=ReliabilityRegression`.

- **File:** `ReliabilityRegressionTests.cs` — 12 deterministic regression tests (dual-tagged `Reliability` + `ReliabilityRegression`) that lock in every bug/race fixed during Phase 12 (see `tests/Reliability/README.md` table).
- **Run:** `dotnet test --filter Category=ReliabilityRegression` — 13 tests (12 new + 1 dual-tagged existing) in ~18s, 100% pass, no Docker, SQLite only.
- **Isolation:** Each test inherits `ReliabilityTestBase` (`Data Source=:memory:` per test, `ResetFaults()`), no shared state, no flakiness.
- **Coverage:** Transient DB locks, AsyncLocal harness flow, SQLite nested-transaction, owned-entity query, DateTime/DateTimeOffset, Sku/PaymentId unmapped, `with` expression, `CountAsync` ambiguity, registry ctor, outbox tracking, TraceId, PaymentFailed outbox, concurrent webhook — all previously failing, now green.

Existing campaigns (`DatabaseFailureTests` … `ReliabilityInvariantsTests`) remain the source of truth for full `Category=Reliability` (79 → 92 after dual-tag). This regression slice is the minimal, fast CI gate that proves no bug regresses.
