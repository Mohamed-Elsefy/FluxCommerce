# PHASE 7 COMPLETION REPORT — Idempotent Consumers

**Date:** 2026-08-17

## 1. Phase 7 Objective

Phase 7 builds the reliable consumption half of the messaging foundation started in Phase 6: real
RabbitMQ consumers that deserialize Integration Events, validate the envelope, invoke application
handlers through a module-owned atomic idempotency boundary (Business State + ProcessedMessage commit
together), acknowledge manually, apply a bounded in-process retry/backoff policy, dead-letter terminal
failures, preserve correlation/causation, and observe every outcome with structured logs. The central
property verified by the phase is:

> **At-Least-Once Delivery + Idempotent Processing = Reliable Consumption** — the same message can be
> delivered multiple times, including concurrent delivery and redelivery after a consumer failure,
> while producing exactly one intended business side effect.

## 2. Implemented Sub-Phases

| SubPhase | Scope | Status |
|----------|-------|--------|
| 7.1 | Baseline & consumer architecture audit | Implemented |
| 7.2 | Consumer contract & processing model | Implemented |
| 7.3 | Inbox / ProcessedMessage persistence | Implemented |
| 7.4 | Atomic idempotent processing | Implemented |
| 7.5 | RabbitMQ consumer infrastructure | Implemented |
| 7.6 | ACK / retry / failure handling | Implemented |
| 7.7 | Retry & dead-letter strategy | Implemented |
| 7.8 | Correlation / causation & observability | Implemented |
| 7.9 | First real integration consumer (Order Summary projection) | Implemented |
| 7.10 | Duplicate / concurrency / failure tests | Implemented |
| 7.11 | Architecture & security hardening | Implemented |
| 7.12 | Performance & operational hardening | Implemented (review-driven; see §6) |
| 7.13 | Documentation synchronization | Implemented (this phase) |
| 7.14 | Final verification & phase completion | Implemented (this report) |

The authoritative Phase 7 specification remains `docs/phases/PHASE_7_IDEMPOTENT_CONSUMERS.md`; this
completion report is the consolidated verified record.

## 3. Consumer Architecture

- **Transport-agnostic handlers.** `IIntegrationEventHandler<TEvent>` (in `BuildingBlocks.Messaging`)
  receives the deserialized contract plus the canonical `IntegrationEventEnvelope` (EventId, EventType,
  EventVersion, CorrelationId, CausationId, OccurredAtUtc) — never a broker message. Handler
  implementations live in the consuming module's Infrastructure beside the DbContext and
  `IIdempotentProcessor` they share. Domain/Application never reference the messaging consumer
  contracts.
- **Explicit consumer registry.** `IntegrationEventConsumerRegistry` maps the wire `EventType` to a
  contract type, handler type, and processor type via `IntegrationEventConsumerDescriptor`
  (no reflection-based activation). Unknown event types resolve to `null` and are ACKed + skipped
  (ADR-008) — they are not poison for this queue.
- **Consumer host.** `RabbitMQConsumerBackgroundService` (an `IHostedService`) consumes from the SAME
  Phase 6 durable topic exchange — no parallel messaging architecture. It declares the exchange and
  the configured queue, binds the queue to each registered event type's routing key, applies per-channel
  prefetch, and starts `RabbitMQConsumerOptions.Concurrency` consumers (the broker round-robins,
  giving bounded in-flight concurrency = Concurrency × PrefetchCount). Every delivery is processed in
  its own DI scope. Delivery is manual: ACK only after processing commits.
- **Message processor.** `IntegrationEventMessageProcessor` reads the wire `EventType` from the
  `EventType` header (fallback AMQP `Type`), resolves the descriptor, builds the envelope from the
  transport headers (authoritative metadata), deserializes the contract (switch-on-Type registry), and
  verifies the payload's EventId/Version agree with the envelope. Invalid/corrupt/mismatched deliveries
  are REJECTED (Nack requeue:false → broker dead-letters verbatim).
- **Per-attempt DI scopes.** Each retry attempt runs in a FRESH DI scope: the scoped DbContext and the
  `IIdempotentProcessor` that owns it are only safe to use once (after a business failure rolls the
  transaction back, EF Core considers the context poisoned).

## 4. Idempotency & Atomic Processing

- **Identity.** `EventId + HandlerName`, composed as `ProcessedMessage.Create(eventId,
  handler.HandlerName)` (ADR-008). The canonical `ProcessedMessage` record (Key = EventId, Handler,
  ProcessedAtUtc) is mapped into each consuming module's DbContext — strictly module-local; one module
  never touches another's `ProcessedMessages`.
- **Database protection.** The composite `(Key, Handler)` is the table PRIMARY KEY in both
  `InventoryDbContext` and `OrdersDbContext` — the SQL unique constraint is the final concurrency
  arbiter. `(E1, H1)` can be claimed once; `(E1, H2)` is an independent consumer; a concurrent duplicate
  race is resolved by the database and reported as already-processed, never an application failure.
- **Atomic boundary.** `IIdempotentProcessor.TryProcessAsync(message, businessOperation)` runs the
  business operation and the ProcessedMessage insert inside ONE module-local transaction (execution
  strategy + explicit transaction) and commits once. A success commits Business State + ProcessedMessage
  together; a business failure rolls both back (the message stays retryable, no ProcessedMessage is
  created); a known duplicate is detected up front and acknowledged WITHOUT re-executing the business
  operation; a concurrent duplicate race rolls back the loser, re-reads the winner and reports
  `AlreadyProcessed`. Exactly one business effect and exactly one processed message per identity.
- **Module-owned processors (critical fix).** Every consumer resolves BOTH its handler and its
  `IIdempotentProcessor` by the concrete type declared on its descriptor — never by the container-wide
  `IIdempotentProcessor` interface, which every module registers and which would resolve
  last-registration-wins across modules. `AddIntegrationEventConsumer<TEvent, THandler, TProcessor>`
  registers the handler, the module-owned processor, and the descriptor carrying `ProcessorType`.
  Module DI registers concrete `IIdempotencyStore`/`IDuplicateDetector`/`IIdempotentProcessor` and
  forwards the shared interfaces to them for single-module internal use. Result: Orders consumers always
  execute through `OrdersIdempotentProcessor`/`OrdersDbContext`, Inventory consumers through
  `InventoryIdempotentProcessor`/`InventoryDbContext` — verified by the module-isolation integration
  tests (§8).

## 5. Retry, Dead-Letter & Observability

- **Bounded in-process retry.** `ConsumerRetryOptions`: `MaxAttempts` (default 3; 1 disables retries),
  `BackoffType` (Fixed / Linear / Exponential), `InitialDelay` (1s), `DelayMultiplier` (2.0),
  `MaxDelay` (30s). The attempt counter is seeded from the broker `x-death` redelivery count (max count
  across entries, since the message lives in exactly one queue at a time), so attempts stay bounded even
  across crashes/requeues/dead-letter hops. The consumer retry is deliberately separate from the Phase 6
  outbox publication retry (`OutboxDispatchOptions`).
- **Failure classification.** `IIntegrationEventFailureClassifier`: an exception whose type (or inner
  exception) is listed in `NonRetryableExceptionTypes` is permanent and dead-lettered immediately;
  everything else is retryable.
- **Dead-letter topology.** The main queue is declared with `x-dead-letter-exchange` → a durable topic
  DLX (`{Queue}.dlx`) bound with `#` to a durable DLQ (`{Queue}.dlq`). Malformed deliveries are rejected
  verbatim (broker-added `x-death` only). Permanent/exhausted failures are ENRICHED — a copy of the
  original body + headers plus `x-dead-letter-reason`, `x-dead-lettered-at`, `x-consumer`, `x-attempt`,
  `x-error-type`, `x-error-message` — published to the DLX via `IIntegrationEventDeadLetterer`
  (confirm-enabled, throttled); the original is ACKed when the enriched copy is confirmed, otherwise
  Nack(requeue:false) preserves the original verbatim. Terminal failures always reach the DLQ exactly
  once, never silently, never back in the processing loop.
- **Observability.** Every consumer log uses a consistent field set — MessageId, EventId, EventType,
  EventVersion, CorrelationId, CausationId, Consumer (stable HandlerName), Attempt, Result
  (`processed` | `duplicate` | `rejected` | `dead-lettered` | `retrying` | `skipped`),
  FailureClassification, DeadLetterReason, DurationMs. Business payloads are never logged. A consuming
  handler producing an integration event reuses `envelope.ProducedEventCorrelationId` (incoming
  CorrelationId) and `envelope.ProducedEventCausationId` (consumed EventId).
- **Graceful shutdown.** `StopAsync` cancels in-flight processing (leaving uncommitted deliveries
  unacknowledged so the broker redelivers them after the channels close — at-least-once), cancels the
  consumers, awaits in-flight work, then disposes the channels.

## 6. Performance & Operational Hardening (7.12)

Reviewed against the current implementation; no performance defect required a code change.

- **Prefetch / bounded concurrency.** `PrefetchCount` (default 1, per-channel `BasicQos`) and
  `Concurrency` (default 1) bound in-flight deliveries; verified by tests including a two-channel
  consumer and concurrent-duplicate delivery.
- **Connection reuse.** One shared `IConnection` per provider with `AutomaticRecoveryEnabled`; channels
  are created per consumer and disposed on shutdown.
- **Backpressure / retry storms.** Bounded by prefetch/concurrency plus the capped in-process backoff
  (MaxDelay 30s) and finite `MaxAttempts` — a storm cannot grow without bound.
- **Database.** The composite `(Key, Handler)` PK serves every lookup (`GetAsync`/`IsProcessedAsync`/
  `TryClaimAsync`) as a single-row PK seek — no N+1, no additional index justified. One atomic
  transaction per attempt.
- **Retention decision.** ProcessedMessages are retained indefinitely with NO automatic cleanup by
  design: under at-least-once there is no delivery end-of-life bound, so a time-based purge would reopen
  the duplicate window. A controlled purge is only safe once a workflow has a confirmed end-of-life
  (e.g., a completed saga in Phase 8+). Documented in `docs/ARCHITECTURE.md` §14.1.1.

## 7. First Real Consumer (7.9) & Module Isolation

- `OrderSummaryProjectionConsumer` (HandlerName `Orders.ProjectOrderSummary`) consumes
  `orders.order_created` from the Phase 6 exchange on queue `q.orders.summary`. It is a thin adapter
  that delegates to `IOrderSummaryProjectionService` (Application) — it never touches transport types or
  the DbContext directly. The projection row + ProcessedMessage commit atomically through
  `OrdersIdempotentProcessor`/`OrdersDbContext` (migration
  `20260817123551_AddOrderSummaryProjectionAndProcessedMessages`).
- The API host registers the consumer with the concrete processor:
  `AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, OrderSummaryProjectionConsumer,
  OrdersIdempotentProcessor>()` — the Orders consumer is therefore isolated from the Inventory
  processor registered elsewhere in the container (no last-registration-wins).

## 8. Final Verification (7.14)

Run against real SQL Server + RabbitMQ (containers verified healthy).

| Verification | Result |
|--------------|--------|
| `dotnet build FluxCommerce.slnx` | **0 warnings / 0 errors** |
| Unit tests | **1910/1910 passed** |
| Architecture tests | **163/163 passed** |
| Integration tests (real SQL Server + RabbitMQ) | **343/343 passed** |
| Phase 6 outbox/publisher regression (integration, filtered) | **47/47 passed** |
| Phase 7 consumer / idempotency / ProcessedMessage tests (integration, filtered) | **40/40 passed** |

(Filtered runs are executed sequentially against the shared broker/database; two parallel test
processes on the same broker/database interfere with each other and are not used for results.)

Verified behavior includes: normal processing + ACK; duplicate delivery (one business effect); concurrent
duplicate delivery (database composite PK arbitrates exactly one winner); failure before commit rolls back
then redelivery succeeds; commit-before-ACK redelivery handled as duplicate; retryable failures follow the
configured policy; retry exhaustion dead-letters with enriched metadata; non-retryable failures dead-letter
immediately; retry backoff gap; x-death-seeded attempt accounting across dead-letter hops; poison-message
isolation (a failing message does not block healthy ones); unknown event types skipped (ADR-008);
malformed payloads rejected to the DLQ verbatim; correlation/causation preserved into the projection;
structured logs expose trace fields without payloads; bounded concurrency (two channels); graceful shutdown
without loss; and module isolation (Orders consumer writes only the Orders database, Inventory consumer
writes only the Inventory database, regardless of container registration order).

## 9. Failure & Idempotency Guarantees (Final Acceptance Criteria)

| Criterion | Confirmed |
|-----------|-----------|
| Business State + ProcessedMessage are atomic | ✅ same module-local transaction, one commit |
| Duplicate delivery cannot repeat the business effect | ✅ duplicate → ACK, business logic not re-executed |
| Concurrent duplicates → exactly one business effect | ✅ composite `(Key, Handler)` PK is the sole arbiter |
| Failed processing remains retryable | ✅ rollback leaves no ProcessedMessage; retry re-attempts |
| Retry attempts are bounded | ✅ `MaxAttempts` + x-death-seeded attempt counter |
| Permanent/exhausted failures reach the DLQ | ✅ enriched DLX copy (or verbatim broker dead-letter) |
| Malformed messages cannot cause endless retry | ✅ rejected verbatim (Nack requeue:false) |
| Unknown event types follow ADR-008 | ✅ ACK + skip |
| Correlation/Causation metadata preserved | ✅ envelope + structured logs + projection |
| Consumer logging does not leak payloads | ✅ business payloads never logged |
| Consumer concurrency is bounded | ✅ Concurrency × PrefetchCount |
| Shutdown is safe; unfinished deliveries redeliver | ✅ cancellation leaves unacked → broker redelivers |
| Module boundaries remain intact | ✅ module-owned processors; no cross-module DbContext |
| Phase 6 behavior remains unchanged | ✅ Phase 6 outbox/publisher regression 47/47 |

## 10. Known Non-Blocking Observations

- **`RabbitMQConsumerBackgroundService.StopAsync` is not double-stop safe.** It disposes `_processingCts`;
  a second `StopAsync` would throw on the already-disposed cancellation source. No production path
  triggers double-stop (a host stops each hosted service exactly once), so this is a latent robustness
  note, not a Phase 7 acceptance failure.
- **Message-size limit** is the broker default (no client-side cap); deliberately not enforced in code —
  a broker-side operational concern.
- **`OrderSummaryProjectionEntity` primary key is `OrderId`.** Today only one event type
  (`orders.order_created`) projects into the table; a future second event type projecting the same Order
  would collide on the PK. Deferred to Phase 8+.
- **Dead-letter double-copy edge case.** If the enriched DLX publish is confirmed but the original ACK is
  lost, both copies exist in the DLQ. Inherent to at-least-once; the enriched record is always preserved.
- **ProcessedMessage retention** has no automated cleanup by design (documented in `docs/ARCHITECTURE.md`
  §14.1.1); any future purge must be gated on a confirmed workflow end-of-life.

## 11. Deferred Work

- **Phase 8 — Order/Inventory Saga orchestration** (explicitly next).
- Payments (Phase 9), compensation workflows (Phase 10), reservation expiry orchestration (Phase 11),
  failure testing (Phase 12).
- Consumer retry/redelivery observability dashboards and any managed ProcessedMessage retention gated on
  saga completion remain future operational work.

Phase 8 is confirmed as the next phase. Phase 7 is complete.