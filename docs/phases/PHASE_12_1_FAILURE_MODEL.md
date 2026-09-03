# Phase 12.1 — Failure Model & Injection Strategy

## 1. Purpose

This document is the **formal failure model** for Phase 12. It identifies every critical failure injection point, the expected recovery behavior, and the deterministic harness that reproduces the failure. It is the exit-criteria artifact for Sub-Phase 12.1.

## 2. Failure Categories (from PHASE_12_FAILURE_TESTING.md §2)

| Category | Description |
|---|---|
| Transient failure | Single attempt fails, retry succeeds |
| Permanent failure | Repeated attempts fail → bounded retries → DLQ / manual intervention |
| Process crash | Host dies mid-operation; redelivery must be safe |
| Timeout | DB or broker does not respond within the window |
| Duplicate delivery | Broker redelivers the same `EventId` |
| Concurrent execution | Two delivers of the same or conflicting events race |
| Out-of-order delivery | Events arrive in a different order than produced |
| Infrastructure unavailability | SQL Server, RabbitMQ, or Redis unreachable |

## 3. Critical Failure Injection Points

### 3.1 Before database commit (SaveChangesInterceptor / transaction rollback)

- **Harness:** `DbFaultInterceptor.InjectSaveChangesFault(DbFaultKind, times)` — injects inside `ISaveChangesInterceptor.SavingChanges{Async}` so EF throws before the transaction commits.
- **Fault kinds:** `Timeout`, `ConnectionDrop`, `PrimaryKeyViolation`, `Deadlock`, `TransientFailure`.
- **Expected recovery:**
  - Business State + Outbox (or Business State + `ProcessedMessage`) are **not persisted** (rolled back).
  - No `ProcessedMessage` is created for consumer failures, so the message stays **retryable** and the broker redelivers.
  - No partial Order / Inventory / Payment / Saga state remains.
- **Production guard:** `DbFaultInterceptor` lives in `tests/IntegrationTests/Common/FaultInjection/` and is only registered when a reliability test builds its service provider with the interceptor. Normal hosts never register it.

### 3.2 After database commit but before message ACK (consumer crash simulation)

- **Harness:** `TestConsumerCrashHarness.CrashNextAfterCommit(times)` + `CrashingIdempotentProcessorDecorator` — wraps `IIdempotentProcessor.TryProcessAsync`, commits Business State + `ProcessedMessage` via the inner processor, then throws `ConsumerCrashException` before the caller ACKs.
- **Alternate:** `TestConsumerCrashHarness.CrashNextBeforeCommit(times)` — throws inside the business operation so the transaction rolls back (no `ProcessedMessage`).
- **Expected recovery:**
  - **After-commit-before-ACK:** commit is durable; redelivery finds the `(EventId, HandlerName)` PK and returns `AlreadyProcessed` without re-executing business logic (exactly one effect).
  - **Before-commit:** commit is rolled back; no `ProcessedMessage`; redelivery re-executes exactly once.
  - The crash exception is classified **retryable** so it never dead-letters prematurely.
- **Production guard:** `TestConsumerCrashHarness` uses an `AsyncLocal` slot and is only wired via `CrashingIdempotentProcessorDecorator`, which is only registered when a test explicitly decorates the processor. Production processors have no decorator.

### 3.3 Before / during Outbox message publication (dispatcher failure / broker unavailable)

- **Harness:** `BrokerFaultSimulator.InjectFault(BrokerFaultMode, times)` / `SimulateConnectionOutage` / `SimulatePublishRejection` — decorates `IRabbitMQPublisher`; intercepts `PublishAsync` and either returns `false` (NACK) or throws `PublishException` / connection exception.
- **Fault kinds:** `ReturnFalse` (NACK / broker return), `ThrowPublishException`, `ThrowConnectionException`.
- **Expected recovery:**
  - Outbox record stays durable before publication (business transaction already committed).
  - Publish failure returns the Outbox message to `Pending` with exponential backoff (`OutboxDispatchOptions`) and `RetryAfterUtc` / `RetryCount` / `Error`.
  - Stale `InProgress` claims are recovered to `Pending` after `ClaimTimeout`.
  - Successful republish may produce **duplicate deliveries** — downstream consumers deduplicate via idempotency, so no duplicate business effect.
  - Permanent failures after `MaxRetryCount` go to `Failed` (terminal).
- **Production guard:** `BrokerFaultSimulator` is only registered via `AddBrokerFaultSimulator()` or the `ReliabilityTestBase.TryWrapPublisherWithBrokerSimulator` path. Both are test-only extensions. Production registers the plain `RabbitMQPublisher`.

### 3.4 During Saga transitions & compensation steps

- **Harnesses (composed):**
  - DB faults via `DbFaultInterceptor` on `OrdersDbContext` (Saga state + Order transition + Outbox atomic claim).
  - Consumer crashes via `TestConsumerCrashHarness` on any Saga consumer (`OrderCreatedSagaConsumer`, `InventoryReservedConsumer`, `InventoryReservationFailedConsumer`, `OrderCancelledSagaConsumer`, `InventoryReservationExpiredConsumer`).
  - Broker faults via `BrokerFaultSimulator` on `orders.order_cancelled` / `inventory.release_requested` / `payments.create_payment_requested` publications.
- **Expected recovery:**
  - Saga row optimistic-concurrency token (`Version`) ensures duplicate or conflicting results (`Reserved` vs `Failed`, concurrent duplicates) apply **at most once**.
  - Duplicate / late / out-of-order deliveries are acknowledged without re-execution; conflicting deliveries on terminal states are rejected.
  - Compensation (`inventory.release_requested`) is idempotent and safe to execute repeatedly; Inventory release is performed exactly once per reservation, duplicates are benign no-ops.
  - Payment after expiry race (`PaymentSucceeded` after `ReservationExpired` → `Cancelled`) stages `refund_payment_requested` instead of resurrecting the saga.
- **Production guard:** no saga logic is modified in 12.1; faults are only asserted through the three injectors above.

### 3.5 During reservation expiration worker processing

- **Harnesses (composed):**
  - DB faults on `InventoryDbContext` during `ExpiredReservationProcessor` batch-load and `SaveLifecycleChangesAsync` (per-reservation optimistic-concurrency).
  - Broker faults on the subsequent `InventoryReservationExpiredIntegrationEvent` publication (Outbox → dispatcher → RabbitMQ).
  - Consumer crashes on `InventoryReservationExpiredConsumer` (Orders saga expiry path).
  - Saga faults as in §3.4 for the `WaitingForPayment → Cancelled` expiry transition.
- **Expected recovery:**
  - A reservation is released **exactly once** via `InventoryReservation.Expire`; concurrent commit/release/expiration racing the same reservation fails the losing save via the concurrency token and the winner is reloaded.
  - Expiration is retryable; the domain event `InventoryReservationExpired` → integration event → Outbox → RabbitMQ → Orders consumer chain preserves at-least-once delivery.
  - Duplicate `ReservationExpired` deliveries deduplicate via `ProcessedMessage (EventId, Orders.ApplyReservationExpired)`; a redelivery on a terminal/completed/in-flight-compensation saga state is an idempotent no-op.
  - Late expiry never moves a terminal state backward; only an active `WaitingForPayment` saga is cancelled.
- **Production guard:** expiration worker + dispatcher are removed from SQLite test hosts for determinism (mirrors `InventoryWebApplicationFactory` / `CheckoutWebApplicationFactory` precedent); reliability tests drive the processor directly.

## 4. Setup Instructions (strictly test-scoped)

```csharp
// SQLite-isolated base — each test gets its own in-memory DB
public sealed class MyReliabilityTest : ReliabilityTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // Optional: register a publisher so BrokerFaultSimulator can be armed
        services.AddSingleton<IRabbitMQPublisher>(...);
    }

    [Fact]
    public async Task Operation_IsAtomic_WhenDbFailsBeforeCommit()
    {
        InfectDbSaveChangesFault(DbFaultKind.Timeout);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => ordersContext.SaveChangesAsync());
        Assert.Empty(ordersContext.Orders);
        Assert.Empty(ordersContext.OutboxMessages);

        ResetFaults(); // or await ResetDatabasesAsync();

        // Retry without fault succeeds
        await ordersContext.SaveChangesAsync();
        Assert.Single(ordersContext.Orders);
    }
}

// Direct injector use (without ReliabilityTestBase)
var interceptor = new DbFaultInterceptor();
interceptor.InjectSaveChangesFault(DbFaultKind.ConnectionDrop, times: 1);
interceptor.InjectCommandFault(DbFaultKind.PrimaryKeyViolation, times: 1);
var broker = provider.GetRequiredService<BrokerFaultSimulator>();
broker.SimulateConnectionOutage(times: 2);
TestConsumerCrashHarness.CrashNextAfterCommit(times: 1);
// ... assert ...
interceptor.Reset(); broker.Reset(); TestConsumerCrashHarness.Reset();
```

- `ReliabilityTestBase` disables all faults by default. Tests arm them explicitly and call `ResetFaults()` / `ResetDatabasesAsync()` / `DisposeAsync()` to clear them.
- No fault injector is referenced from `src/`; do not register any of the three classes in `Program.cs` or module `Add*Infrastructure` extensions.

## 5. What is NOT Done in Sub-Phase 12.1

- No production business logic, domain entity, DbContext configuration, or migration is altered.
- No reliability campaigns are executed (those belong to Sub-Phases 12.2–12.10):
  no `DatabaseFailure/`, `OutboxFailure/`, `ConsumerCrash/`, etc. test classes are implemented.
- `tests/Reliability/` remains a directory-structure placeholder until 12.2+.
- This document plus the three helpers and `ReliabilityTestBase` is the deliverable; they unlock the campaigns without themselves running them.

## 6. Traceability

- Failure categories: `docs/phases/PHASE_12_FAILURE_TESTING.md` §2
- Injection points: `docs/phases/PHASE_12_FAILURE_TESTING.md` §12.1
- Helpers: `tests/IntegrationTests/Common/FaultInjection/{DbFaultInterceptor,TestConsumerCrashHarness,BrokerFaultSimulator}.cs` — lines carry `file:line` traceability.
- Base fixture: `tests/IntegrationTests/Reliability/ReliabilityTestBase.cs`
- Future regression layout: `docs/phases/PHASE_12_FAILURE_TESTING.md` §12.13
