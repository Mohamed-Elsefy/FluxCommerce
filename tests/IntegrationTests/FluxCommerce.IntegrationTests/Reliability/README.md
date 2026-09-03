# Reliability Test Suite (Phase 12.1)

This directory is the **reliability regression suite foundation** (Phase 12.1). No failure campaigns are executed here — this phase only establishes deterministic fault-injection infrastructure and isolated test harnessing.

## Structure

```
tests/IntegrationTests/
  Common/FaultInjection/
    DbFaultInterceptor.cs               # EF Core SaveChanges + DbCommand fault injection
    TestConsumerCrashHarness.cs         # Consumer crash after-commit-before-ACK harness
    BrokerFaultSimulator.cs             # RabbitMQ publish rejection / connection outage simulator
  Reliability/
    ReliabilityTestBase.cs              # Isolated SQLite fixture + reset + deterministic hooks
    README.md                           # This file

tests/Reliability/                      # Future standalone reliability campaign collection (12.2–12.14)
  README.md                             # Placeholder; campaigns are out of scope for 12.1
```

`tests/Reliability/README.md` is the companion placeholder so the `tests/Reliability/` layout mandated by the Phase 12 plan exists even though campaigns belong to Sub-Phases 12.2–12.10.

## Boundary

- Fault injectors are **strictly test-scoped**. They are never registered in `src/` and are disabled by default.
- Production business logic, domain entities, and migrations are **not modified** in Phase 12.1.
- No campaign tests (DB, Outbox, Consumer, Saga, Reservation expiry) are implemented here.
