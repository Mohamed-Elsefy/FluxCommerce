I now have a complete picture of the codebase. Here is the Phase 7.1 audit report.

---

# PHASE 7.1 AUDIT REPORT — Baseline & Consumer Architecture Audit

**Date:** 2026-08-16 — **Scope:** Audit only; no code changes; no Phase 7.2+ work.

---

## 1. Existing Messaging Architecture

Phase 6 delivered the **publish side only**. The complete flow today is:

```
Business Operation → Domain Event → Integration Event (mapped) + Outbox record → SAME TX → COMMIT
        → Outbox Dispatcher (BackgroundService) → RabbitMQPublisher → durable topic exchange
```

Key components:

| Component | Project | File(s) |
|---|---|---|
| Canonical event metadata base | `BuildingBlocks.Messaging` | `Events/IIntegrationEvent.cs`, `Events/IntegrationEvent.cs`, `Versioning/EventVersion.cs`, `Versioning/EventVersionJsonConverter.cs` |
| Outbox contracts | `BuildingBlocks.Outbox` | `Messages/OutboxMessage.cs`, `Messages/OutboxStatus.cs`, `Stores/IOutboxStore.cs`, `Dispatch/IOutboxDispatcher.cs`, `Configuration/OutboxDispatchOptions.cs` |
| RabbitMQ connection | `BuildingBlocks.Infrastructure` | `Messaging/IRabbitMQConnectionProvider.cs`, `Messaging/RabbitMQConnectionProvider.cs` |
| RabbitMQ publisher | `BuildingBlocks.Infrastructure` | `Messaging/IRabbitMQPublisher.cs`, `Messaging/RabbitMQPublisher.cs` |
| RabbitMQ DI | `BuildingBlocks.Infrastructure` | `DependencyInjection/RabbitMQServiceExtensions.cs` (`AddRabbitMQ`, `AddRabbitMQPublisher`) |
| Orders outbox (interceptor → factory → store → dispatcher) | `Modules.Orders.Infrastructure` | `Outbox/OrdersOutboxSaveChangesInterceptor.cs`, `Outbox/OrdersOutboxMessageFactory.cs`, `Outbox/OrdersOutboxStore.cs`, `Background/OrdersOutboxDispatcher.cs` |
| Inventory outbox (same four) | `Modules.Inventory.Infrastructure` | `Outbox/InventoryOutboxSaveChangesInterceptor.cs`, `Outbox/InventoryOutboxMessageFactory.cs`, `Outbox/InventoryOutboxStore.cs`, `Background/InventoryOutboxDispatcher.cs` |
| Host wiring | `FluxCommerce.Api` | `Extensions/DependencyInjection.cs:100-101` (`AddRabbitMQ` + `AddRabbitMQPublisher`), `Options/MessageBrokerOptions.cs` |

**Messaging semantics:** At-Least-Once. `PHASE_6_COMPLETION_REPORT.md` §7 documents that a publish confirmed by the broker but never `MarkPublished` is republished on recovery — duplicates are possible and deliberately deferred to Phase 7.

**Consumer infrastructure:** **None exists.** A grep for `BasicConsumeAsync`, `AsyncEventingBasicConsumer`, `IBasicConsumer`, and `Consumer` across `src/` returns zero consumer implementations. The only `QueueDeclareAsync`/`QueueBindAsync` calls in the solution are inside test code (`OrdersMessagingEndToEndTests.cs`, `OrdersOutboxDispatcherIntegrationTests.cs`, `RabbitMQPublisherIntegrationTests.cs`) used to *receive* published events for verification.

---

## 2. Existing Idempotency Infrastructure

**Consumer-side idempotency contracts exist but are completely dormant** (`BuildingBlocks.Idempotency`, referenced by **no production project** — only `tests/Architecture/FluxCommerce.ArchitectureTests.csproj` references it so dependency analysis can load it):

- `Messages/ProcessedMessage.cs` — `Key` (messageId string), `Handler` (nullable string), `ProcessedAtUtc`; factories `Create(Guid messageId, string? handler)` and `Create(string key, string? handler)`.
- `Messages/IdempotencyResult.cs` — `IsDuplicate`, `ProcessedAtUtc`; `NotProcessed()` / `AlreadyProcessed(DateTime)`.
- `Stores/IIdempotencyStore.cs` — `GetAsync(key, handler)` (read) + `AddAsync(message)` (write) — check-then-act surface.
- `Detection/IDuplicateDetector.cs` — `TryClaimAsync(message)` (returns `IdempotencyResult`) + `IsProcessedAsync(key, handler)` — atomic-claim surface.

There are **no implementations, no DI registrations, no EF entities, no tables, and no tests** for any of these types. `PHASE_6_COMPLETION_REPORT.md` §15 confirms they are "dormant forward contracts."

**Existing idempotency that IS implemented is API-request idempotency (Orders checkout), not message idempotency:**
- `Modules.Orders.Infrastructure/Persistence/Entities/CheckoutIdempotency.cs` + `Configurations/CheckoutIdempotencyConfiguration.cs` (unique index `UX_CheckoutIdempotency_UserId_IdempotencyKey`).
- `Modules.Orders.Infrastructure/Persistence/Stores/CheckoutStore.cs:50-86` — Order + idempotency record persist atomically in one `SaveChangesAsync`; a `DbUpdateException` re-reads the winner and surfaces `CheckoutPersistStatus.Duplicate`. This is the established in-repo precedent for **atomic business-state + dedupe-record** and for handling the unique-index race — directly relevant to Phase 7.4.

---

## 3. Existing RabbitMQ Topology

- **Exchange:** one durable **topic** exchange, name `fluxcommerce.events` (default in `Options/MessageBrokerOptions.cs:20`; tests use `fluxcommerce.events.it`). Declared lazily by the publisher on channel creation (`RabbitMQPublisher.GetChannelAsync`, `RabbitMQPublisher.cs:109-117`) with `ExchangeType.Topic`, `durable: true`, `autoDelete: false`.
- **Routing key = event type.** The concrete event's `EventType` constant doubles as the topic routing key (`IRabbitMQPublisher.cs:8-11`; `RabbitMQPublisher.cs:64`).
- **Publish semantics:** publisher confirms + confirmation tracking + shared channel + `ThrottlingRateLimiter(1)` (`RabbitMQPublisher.cs:100-103`); `mandatory: false` (`RabbitMQPublisher.cs:65`) — an unrouted publish is still broker-confirmed and silently dropped.
- **Metadata on the wire** (`RabbitMQPublisher.BuildProperties`, `RabbitMQPublisher.cs:135-154`):
  - AMQP properties: `MessageId` **= `EventId`**, `Type` = eventType, `CorrelationId`, `Timestamp`, `Persistent = true`.
  - Transport headers: `EventId`, `EventType`, `EventVersion`, `CorrelationId`, `CausationId`, `OccurredAtUtc`.
- **No queues, no bindings, no consumers, no DLQ/alternate exchange** exist anywhere in production code. Queue topology is created ad-hoc (exclusive/auto-delete) in integration tests only.
- **Connection:** `RabbitMQConnectionProvider` builds one lazy `IConnection` with `AutomaticRecoveryEnabled = true` from an `amqp://` URI (`RabbitMQConnectionProvider.cs:40-49`), exposes `GetConnection()` and `CreateChannelAsync(...)` — reusable by consumers.

---

## 4. Existing Integration Events

Exactly **three** concrete integration events exist (all in module `Contracts` projects, all deriving from the contract-only `IntegrationEvent` base per ADR-007):

| Event | EventType constant | Business payload | File |
|---|---|---|---|
| `OrderCreatedIntegrationEvent` | `orders.order_created` | OrderId, CustomerId, OrderNumber, OccurredAt | `Modules.Orders.Contracts/IntegrationEvents/OrderCreatedIntegrationEvent.cs` |
| `OrderCancelledIntegrationEvent` | `orders.order_cancelled` | OrderId, CustomerId, OrderNumber, OccurredAt | `Modules.Orders.Contracts/IntegrationEvents/OrderCancelledIntegrationEvent.cs` |
| `InventoryReservationExpiredIntegrationEvent` | `inventory.reservation_expired` | ReservationId, OrderId, Sku, Quantity, OccurredAt | `Modules.Inventory.Contracts/IntegrationEvents/InventoryReservationExpiredIntegrationEvent.cs` |

Canonical metadata available on every event: **EventId, CorrelationId, CausationId, Version (`EventVersion`), OccurredAtUtc** (`IntegrationEvent.cs`). There is **no separate `MessageId` distinct from `EventId`** on the wire — AMQP `MessageId` is set to `EventId`.

Forward-only (unused) contracts: `Modules.Inventory.Contracts/Integration/InventoryOperations.cs` (`ReserveInventoryRequest`, `ReleaseInventoryRequest`, `CommitInventoryRequest`, `InventoryReservationResult`) — explicitly Phase 8 forward contracts, zero usages.

**Candidate events for the first real consumer (Phase 7.9):** All three events are oriented toward the **Phase 8/11 Order–Inventory Saga workflow** — `order_created` → reserve inventory (Phase 8), `order_cancelled` → release inventory (Phase 8), `inventory.reservation_expired` → cancel order (Phase 11). **None is a naturally "neutral" event** for a non-Saga demonstration consumer. This is the central decision point the audit surfaces (see §9, finding R1).

---

## 5. Existing ProcessedMessage / Persistence Model

- `ProcessedMessage` (`BuildingBlocks.Idempotency/Messages/ProcessedMessage.cs`) is a shared, dependency-free record. It identifies `Key` (messageId), `Handler`, `ProcessedAtUtc`. **It is not mapped to any EF entity, has no table, no migration, no configuration, and no ownership** anywhere in the solution.
- The persistence model that Phase 7 must follow is the established **module-per-DbContext** precedent:
  - `OrdersDbContext.cs:27` exposes `OutboxMessages`; `InventoryDbContext.cs:26` exposes `OutboxMessages`.
  - `OutboxMessageConfiguration.cs` (Orders and Inventory — byte-identical) maps the shared `OutboxMessage` into each module DbContext with table name, indexes (`IX_OutboxMessages_Status_CreatedAtUtc`, `IX_OutboxMessages_Status_ProcessedAtUtc`).
  - Per-module migrations add the outbox table (`20260814153225_AddOutboxMessages`, `20260815080452_AddOutboxRetryAfterUtc` for Orders; same + `20260815080520_...` for Inventory).
  - The atomic commit pattern (Business State + Outbox record in one `SaveChangesAsync`) is implemented via per-module `SaveChangesInterceptor` (`OrdersOutboxSaveChangesInterceptor.cs`, `InventoryOutboxSaveChangesInterceptor.cs`).
- **Conclusion:** Phase 7 will need a `ProcessedMessages` table owned by each *consuming* module's DbContext (the plan §8's "InventoryDbContext → Inventory State + ProcessedMessage"), modeled after the `OutboxMessages` approach — but no such entity/table/configuration exists today.

---

## 6. Existing Consumer-Related Tests

**No consumer tests exist** (no consumers exist). The messaging test inventory is entirely publisher/dispatcher-side:

**Integration (real SQL + RabbitMQ):**
- `RabbitMQPublisherIntegrationTests.cs` (3) — confirms, delivery, routing, metadata, unreachable broker.
- `OrdersMessagingEndToEndTests.cs` (7) — full business → outbox → dispatcher → RabbitMQ path, including broker-nack recovery, atomic rollback, stale-claim recovery, batch isolation, and the At-Least-Once duplicate boundary.
- `OrdersOutboxDispatcherIntegrationTests.cs`, `OrdersOutboxStoreIntegrationTests.cs`, `OrdersOutboxIntegrationTests.cs`.
- Inventory analogs: `InventoryOutboxStoreIntegrationTests.cs`, `InventoryOutboxPersistenceTests.cs`, `InventoryOutboxIntegrationTests.cs`.
- Fixtures: `TestInfrastructure/RabbitMQTestFixture.cs` (exchange `fluxcommerce.events.it`, fail-fast connectivity), `TestInfrastructure/OrdersOutboxDispatcherTestFixture.cs` (real SQL `FluxCommerce_Orders` + RabbitMQ, parallelization-disabled collections `SqlServerOrdersDispatcherTestCollection`, `RabbitMQTestCollection`), `TestInfrastructure/SqlServerOrdersTestFixture.cs`.

**Unit:**
- `Messaging/IntegrationEventContractTests.cs`, `Messaging/RabbitMQPublisherTests.cs`.
- `Outbox/OrdersOutboxDispatcherTests.cs`, `Outbox/OrdersOutboxDispatcherLoggingTests.cs`, `Outbox/InventoryOutboxDispatcherTests.cs`, `Outbox/InventoryOutboxDispatcherLoggingTests.cs`.
- `Persistence/OrdersOutboxMessageFactoryTests.cs`, `Persistence/OrdersOutboxSaveChangesInterceptorTests.cs`, and Inventory analogs.

**Architecture tests (157 total):** messaging-relevant rules live in `OrdersArchitectureTests.cs` and `InventoryArchitectureTests.cs`:
- Domain + Application must not reference `BuildingBlocks.Messaging`; Domain/Application/Infrastructure must not reference `RabbitMQ.Client` (`Orders_ShouldNot_ReferenceMessagingOrRabbitMq`, `Inventory_ShouldNot_ReferenceMessagingOrRabbitMq`).
- Contracts may reference Messaging but not RabbitMQ/Outbox (`Orders_Contracts_ShouldNot_ReferenceRabbitMqOrOutbox`; positive pin `..._IsAllowedToReferenceMessaging`).
- Infrastructure is allowed to reference Outbox (`Orders_Infrastructure_IsAllowedToReferenceOutbox`).
- No Saga/Orchestrator types (`Orders_/Inventory_ShouldNot_IntroduceSagaOrOrchestratorTypes`).
- Cross-module isolation + one-DbContext-per-module + DbContext-resides-in-Infrastructure (`ArchitectureTests.cs`, `OrdersArchitectureTests.cs`, `InventoryArchitectureTests.cs`).

**No tests exercise** `IIdempotencyStore`, `IDuplicateDetector`, `ProcessedMessage`, or `IdempotencyResult`.

---

## 7. Reusable Components

Directly reusable without change:

1. **`IRabbitMQConnectionProvider` / `RabbitMQConnectionProvider`** (`BuildingBlocks.Infrastructure`) — singleton, lazy, auto-recovering connection; `CreateChannelAsync(...)` is exactly what a consumer host needs for transport channels. Already registered in the host DI (`FluxCommerce.Api/Extensions/DependencyInjection.cs:100`) and in test fixtures.
2. **`IntegrationEvent` / `IIntegrationEvent` / `EventVersion` / `EventVersionJsonConverter`** (`BuildingBlocks.Messaging`) — canonical wire metadata; consumers deserialize with these (dispatcher precedent: switch-on-`Type` → `JsonSerializer.Deserialize<T>` in `OrdersOutboxDispatcher.Deserialize`, `OrdersOutboxDispatcher.cs:257-265`).
3. **`ICorrelationIdProvider` / `ICorrelationIdProvider` implementations** (`BuildingBlocks.Application`, `BuildingBlocks.Infrastructure/Services/GuidCorrelationIdProvider.cs`, `WebCorrelationIdProvider`) — Phase 6.9 propagates CorrelationId onto the wire; consumers must propagate it forward.
4. **Dormant idempotency contracts** `ProcessedMessage`, `IdempotencyResult`, `IIdempotencyStore`, `IDuplicateDetector` (`BuildingBlocks.Idempotency`) — the plan (§7.3, §9, §20) mandates reusing them; they are ready but need implementations + persistence + DI.
5. **Background-service pattern** — `BackgroundService` + `IServiceScopeFactory` per-cycle scope (`OrdersOutboxDispatcher.cs:44-85`, `ReservationExpirationBackgroundService.cs:31-77`) is the established hosted-loop shape a consumer host should mirror.
6. **Options pattern** — `OutboxDispatchOptions` (`BuildingBlocks.Outbox`) shows the validated-options convention for a future `ConsumerOptions`.
7. **Atomic business-state + dedupe-record precedent** — `CheckoutStore.PersistAsync` (`CheckoutStore.cs:50-86`): same `SaveChangesAsync`, unique-index enforcement, `DbUpdateException` → re-read winner. Directly applicable to Phase 7.3/7.4.
8. **Module-per-DbContext + shared-entity configuration precedent** — `OutboxMessageConfiguration.cs` (Orders & Inventory) is the template for a per-module `ProcessedMessage` configuration and unique index.
9. **Integration-test infrastructure** — `RabbitMQTestFixture`, `OrdersOutboxDispatcherTestFixture`, parallelization-disabled collections, fail-fast broker verification; a consumer E2E suite should clone this shape.
10. **Existing architecture-test rules** — the Domain/Application X→RabbitMQ.Client rules already protect the Phase 7 boundaries for new consumer code; only new positive/negative rules for the consumer abstractions are needed.

---

## 8. Missing Components for Phase 7

1. **RabbitMQ consumer transport** — nothing consumes. Missing: queue declaration + topic-exchange binding (routing key = event type), `BasicConsume`/manual delivery handling, manual **ACK/NACK**, prefetch, bounded concurrency, channel lifecycle, graceful shutdown, cancellation behavior. (Plan §7.5, §7.6.)
2. **Consumer/handler abstraction** — no type separates transport from business processing; no handler registry/resolution by event type. (Plan §7.2.)
3. **`ProcessedMessage` persistence** — no EF entity mapping, no `ProcessedMessages` table, no migration, no configuration, no unique index on (message identity, handler). (Plan §7.3.)
4. **`IIdempotencyStore` / `IDuplicateDetector` implementations** — no SQL-backed store, no atomic `TryClaim` implementation, no DI registration. (Plan §7.3, §7.4.)
5. **Atomic idempotent-processing wiring** — no mechanism writes Business State + ProcessedMessage in one transaction for consumers. (Plan §7.4.)
6. **Consumer retry / DLQ policy** — no queue-level DLQ (`x-dead-letter-exchange`/`x-dead-letter-routing-key`), no retry/backoff config distinct from `OutboxDispatchOptions`, no terminal-failure metadata for dead-lettered messages. (Plan §7.6, §7.7.)
7. **Consumer observability** — no logs/counters for MessageId/EventId/EventType/Consumer/Attempt/Result/Duration; no CausationId propagation *into* a consumer's produced events. (Plan §7.8.)
8. **First real consumer** — none exists; event selection unresolved (see §9, R1). (Plan §7.9.)
9. **Tests** — duplicate delivery, concurrent duplicate, failure-before-commit, commit-before-ACK redelivery, retryable failure, permanent failure/DLQ, message isolation. (Plan §7.10.) No consumer fixtures.
10. **Consumer DI + architecture rules** — no consumer DI extension; no new architecture-test rules pinning the consumer abstractions and their permitted dependencies.
11. **Background-scope `ICurrentUser`** — `BaseDbContext` requires `ICurrentUser`; the API registers the web-backed implementation (request-scoped). Consumer/background scopes have no HTTP request, so a non-request `ICurrentUser` (test code uses `AnonymousCurrentUser`) must be provided for consumer scopes.

---

## 9. Architectural Risks / Findings

**R1 — No neutral event for the first consumer (blocking decision for 7.2/7.9).** Every existing event is Saga-oriented: `order_created`→reserve (Phase 8), `order_cancelled`→release (Phase 8), `inventory.reservation_expired`→cancel order (Phase 11). Wiring any of these as a state-changing consumer without care would implement Phase 8/11 Saga semantics, which the task explicitly forbids. Phase 7.2/7.9 must make an explicit choice: (a) a read-model/projection-style consumer whose effect is not Saga coordination, or (b) introduce a genuinely neutral integration event (a business contract, not infrastructure) — a decision to be resolved in 7.2, not by this audit.

**R2 — Idempotency-key identity is currently EventId-only.** The publisher sets AMQP `MessageId` = `EventId` (`RabbitMQPublisher.BuildProperties`, `RabbitMQPublisher.cs:139`), so there is **no per-delivery message id distinct from the event id**. `ProcessedMessage.Key` is documented as "messageId". Phase 7 must define the dedupe identity (EventId-based, or a new distinct delivery id) and reconcile `IIdempotencyStore`'s check-then-act (`GetAsync`/`AddAsync`) with `IDuplicateDetector`'s atomic claim (`TryClaimAsync`) and `IdempotencyResult`. Introducing a genuinely distinct message id would require a deliberate (small) publisher extension — a Phase 7.2 decision, not a Phase 6 redesign.

**R3 — `ProcessedMessage` is a BuildingBlock record, but ownership must be per module.** Plan §8 mandates the processed-message record live in the consuming module's persistence boundary (e.g., `OrdersDbContext → Order State + ProcessedMessage`). The shared record + per-module EF configuration/table must follow the `OutboxMessage` precedent exactly; there is no cross-module or global database today (`PHASE_6_COMPLETION_REPORT.md` §3).

**R4 — No DLQ / alternate-exchange topology exists, and publish is `mandatory:false`.** An unrouted publish is silently broker-confirmed. Consumer DLQ (if adopted) is queue-level topology and belongs to the consumer infra, keeping Phase 6 publish-retry (outbox backoff) distinct from Phase 7 consumer retry — per plan §12, these must not merge. `MessageBrokerOptions.RetryCount` is already a documented unused leftover (`PHASE_6_COMPLETION_REPORT.md` §14); consumer retry needs its own options type.

**R5 — SaveChanges interceptor chain already carries two interceptors.** `BaseDbContext.OnConfiguring` adds the shared DI `ISaveChangesInterceptor` (`DomainEventInterceptor`), and module DI adds the module outbox interceptor (`DependencyInjection.cs:48` for Orders). A future ProcessedMessage interceptor (if interceptor-based rather than store-based) must respect this ordering. The alternative — an explicit store writing business state + ProcessedMessage in one `SaveChangesAsync` — has a clean in-repo precedent (`CheckoutStore.PersistAsync`) and avoids the interceptor-ordering question.

**R6 — Background-scope `ICurrentUser` is unresolved.** All SQL-backed module DbContexts require `ICurrentUser` via `BaseDbContext`. Consumer scopes run outside HTTP; tests fabricate `AnonymousCurrentUser`. Phase 7 must register an appropriate non-request `ICurrentUser` for consumer scopes or the first consumer will fail at scope resolution.

**R7 — Architecture boundaries already anticipate Phase 7.** The existing rules (Domain/Application X→Messaging; Domain/Application/Infrastructure X→RabbitMQ.Client; Contracts allowed Messaging only; module isolation; no cross-module DbContext; no Saga types) mean consumer transport must live in `BuildingBlocks.Infrastructure` (or a new consumers block) and per-module consumer wiring in module `Infrastructure`, with Application/Domain invoking via Application contracts. New rules will be needed to pin the new consumer abstractions and to keep Application free of `BuildingBlocks.Infrastructure` (rule already exists: `Orders_Application_ShouldNot_ReferenceBuildingBlocksInfrastructure`).

**R8 — Deserialization safety precedent is already correct.** The dispatcher deserializes by `switch` on `Type` to concrete contract types (no type-name-driven activation) — consumer deserialization should reuse this pattern to avoid untrusted type activation (plan §7.11).

**R9 — `InventoryOperations` must not be touched in Phase 7.** The Reserve/Release/Commit request records are documented Phase 8 forward contracts with zero usages; wiring them would implement Saga.

---

## 10. Recommended Implementation Direction for Phase 7.2

For the Phase 7.2 scope (contracts + processing model only), the audit recommends:

1. **Define a minimal, transport-agnostic consumer abstraction** in a contract-only location (reusing the ADR-007/G12 precedent): e.g., an `IMessageConsumer<TEvent>`/handler contract that receives a deserialized `IIntegrationEvent` + metadata envelope, invoked by consumer infrastructure; plus an event-type → handler resolution map. The abstraction must NOT reference `RabbitMQ.Client`; the RabbitMQ-backed host lives in `BuildingBlocks.Infrastructure`, per-module wiring in module `Infrastructure`.
2. **Separate responsibilities explicitly:** transport (receive, deserialize, validate envelope, resolve handler, ACK/NACK, retry, correlate, log) vs. business processing (Application handler → Domain → persistence). Consumers never touch DbContext directly.
3. **Reuse the dormant idempotency contracts as the canonical consumer-idempotency surface** (`ProcessedMessage`, `IdempotencyResult`, `IIdempotencyStore`, `IDuplicateDetector`) rather than inventing new abstractions; define the dedupe identity (R2) in the consumer envelope contract.
4. **Persist `ProcessedMessage` per consuming module** following the `OutboxMessage` precedent (shared record, per-module entity config + unique index + migration); atomicity via the `CheckoutStore.PersistAsync` pattern (business state + ProcessedMessage in one `SaveChangesAsync`).
5. **Define consumer options separate from `OutboxDispatchOptions`** (prefetch, concurrency, retry attempts/backoff, claim/redelivery policy) and decide queue-level DLQ topology; keep Phase 6 publish-retry untouched.
6. **Propagate CorrelationId/CausationId** from wire headers into consumer logs and into any event the consumer publishes (CausationId = processed event's EventId).
7. **Resolve R1/R6 during 7.2 design** — select the first-consumer event/semantics that does not implement Phase 8/11 Saga, and specify the non-request `ICurrentUser` for consumer scopes.
8. **Do not** modify Phase 6 publisher/dispatcher, do not wire `InventoryOperations`, do not introduce Saga/Payments.

