# PHASE 7.2 COMPLETION REPORT — Consumer Contract & Processing Model

**Date:** 2026-08-16

## 1. Implemented

Phase 7.2 defines the **transport-agnostic consumer contract and processing model** for idempotent
consumers — contract/design only, with no transport, persistence, or broker implementation. The work:

| Deliverable | Location | Status |
|-------------|----------|--------|
| Consumer contract surface (envelope, handler, descriptor, registry) | `src/BuildingBlocks/FluxCommerce.BuildingBlocks.Messaging/Consuming/` | Implemented |
| Architecture boundary test: Messaging → no Infrastructure/Outbox/Idempotency/RabbitMQ/EF Core | `tests/Architecture/FluxCommerce.ArchitectureTests/ArchitectureTests.cs` | Implemented |
| Contract unit tests (envelope, handler, registry, R2 composition) | `tests/UnitTests/FluxCommerce.UnitTests/Messaging/ConsumerProcessingModelTests.cs` | Implemented (6/6 pass) |
| ADR-008 | `docs/adr/008-consumer-contract-and-idempotency.md` | Implemented |
| Architecture doc subsection | `docs/ARCHITECTURE.md` §14.1 | Implemented |
| UnitTests reference to `BuildingBlocks.Idempotency` (R2 composition test only) | `tests/UnitTests/FluxCommerce.UnitTests.csproj` | Implemented |

**Verification:** `dotnet build` clean (0 warnings/0 errors) on Messaging, UnitTests, and
ArchitectureTests projects; **1802/1802 unit tests** pass; **158/158 architecture tests** pass
(including the new `Messaging_ShouldNot_ReferenceInfrastructureOrRabbitMqOrEfCore` rule).

## 2. Consumer Contract

New namespace `FluxCommerce.BuildingBlocks.Messaging.Consuming`, transport-agnostic and dependency-free
(references only the existing `Events`/`Versioning` metadata types in the same assembly):

- `IntegrationEventEnvelope` — the processing context delivered to handlers (see §3).
- `IIntegrationEventHandler<in TEvent>` — handler contract: stable `string HandlerName` plus
  `Task HandleAsync(TEvent @event, IntegrationEventEnvelope envelope, CancellationToken)`. `TEvent` is
  constrained to `class, IIntegrationEvent`.
- `IntegrationEventConsumerDescriptor` — sealed record `{ EventType, EventContractType, HandlerType }`.
- `IIntegrationEventConsumerRegistry` — `IntegrationEventConsumerDescriptor? Resolve(string eventType)`.

Handlers receive the deserialized contract and the envelope — **never a broker message**. No RabbitMQ
`IModel`, no `BasicDeliverEventArgs`, no transport type leaks into the contract surface.

## 3. Processing Context

`IntegrationEventEnvelope.From(IIntegrationEvent @event, string eventType)` builds the context from the
canonical metadata (single source of truth, per `IIntegrationEvent`):

- `EventId` (event identity) — copied from the event, unchanged from the publisher (`MessageId = EventId`).
- `EventType` (stable wire type, e.g. `orders.order_created`) — supplied by the consumer dispatcher.
- `EventVersion` — the contract version, deserialized alongside the payload.
- `CorrelationId` / `CausationId` — trace identifiers, forwarded unchanged.
- `OccurredAtUtc` — the original occurrence timestamp.

The envelope is a sealed record (value semantics) and contains only primitives/metadata — no transport,
no persistence, no domain types.

## 4. Handler Resolution

Resolution is **explicit and deterministic**, replacing reflection-based discovery:

1. Transport delivers a wire `EventType`.
2. `IIntegrationEventConsumerRegistry.Resolve(eventType)` returns the descriptor: the CLR
   `EventContractType` to deserialize into (switch-on-Type, mirroring the Phase 6 publisher) and the DI
   `HandlerType` to resolve.
3. The handler instance is resolved from the container (plain DI registration, no untrusted CLR type
   activation), `HandlerName` is read, and `HandleAsync` is invoked with contract + envelope.
4. Unknown event types resolve to `null` and are skipped (forward-compatible consumption).

The registry is a contract; a per-module mapping implementation is deferred to Phase 7.3+.

## 5. Idempotency Boundary

- Idempotency identity = **`EventId + HandlerName`**, composed via the canonical
  `ProcessedMessage.Create(envelope.EventId, handler.HandlerName)`.
- The contract surface is **isolated from idempotency persistence**: `BuildingBlocks.Messaging` does not
  reference `BuildingBlocks.Idempotency`, `BuildingBlocks.Outbox`, `BuildingBlocks.Infrastructure`,
  `RabbitMQ.Client`, or EF Core (enforced by the new architecture test). The composition happens at the
  processing layer, not in the contract.
- **Contract detail captured by tests:** `ProcessedMessage` represents the identity as composite
  `Key (= EventId) + Handler` — same event across two handlers yields equal `Key`, distinct `Handler`
  (asserted in `SameEvent_DifferentHandlers_Yield_DistinctIdempotencyIdentities`). The 7.3+ dedupe must
  check the composite, not `Key` alone.
- No second idempotency abstraction was introduced; the dormant `BuildingBlocks.Idempotency` contracts
  remain the single model.

## 6. R1 Decision — Neutral Demonstration Event

No existing integration event (`orders.order_created`, `orders.order_cancelled`,
`inventory.reservation_expired`) supports a **non-Saga projection consumer** without inventing business
semantics, and no real consumer was implemented in this sub-phase. **Decision:** a dedicated neutral
integration event is required for the first real consumer; its contract definition and producer wiring
are deferred (7.3+ / before the first consumer) and will not be invented casually. Documented in ADR-008.

## 7. R2 Decision — Idempotency Identity

**Decision: `EventId + HandlerName`.** The publisher and the AMQP `MessageId` (= `EventId`) are
**unchanged** — no distinct delivery `MessageId` is introduced (that alternative was rejected: it
changes the Phase 6 contract and conflates delivery identity with event identity). The three identities
are kept distinct: **event identity** (`EventId`), **consumer/handler identity** (`HandlerName`), and
**idempotency identity** (their composite). Composition is unit-tested against the canonical
`ProcessedMessage` (`ProcessedMessage_Key_ComposesEventId_AndScopesByHandlerName`).

## 8. R6 Decision — Background Identity

Consumer scopes run on the broker path with **no HTTP request**, so there is no request `ICurrentUser`.
**Decision:** consumer/background scopes resolve a **non-request `ICurrentUser`** (unauthenticated),
matching the existing test `AnonymousCurrentUser` precedent. No authentication infrastructure change is
made; the consumer scope simply carries no request identity. The actual scope/DI wiring is deferred to
7.3+. Documented in ADR-008.

## 9. Architecture Tests

New rule `Messaging_ShouldNot_ReferenceInfrastructureOrRabbitMqOrEfCore` in
`tests/Architecture/FluxCommerce.ArchitectureTests/ArchitectureTests.cs` (forbidden namespaces:
`FluxCommerce.BuildingBlocks.Infrastructure`, `FluxCommerce.BuildingBlocks.Outbox`,
`FluxCommerce.BuildingBlocks.Idempotency`, `RabbitMQ.Client`, `Microsoft.EntityFrameworkCore`), backed
by a new `ArchitectureRegistry.Messaging` assembly entry. Full suite: **158/158 pass**. No existing
rule was weakened; the Phase 6.4 positive rules and module boundaries are untouched.

## 10. Deferred to Phase 7.3+

Intentionally out of scope (contract untouched by this sub-phase):

- RabbitMQ consumer host (`AsyncEventingBasicConsumer`/`BasicConsumeAsync`), queues, bindings, DLQ.
- ACK/NACK, redelivery, and retry handling.
- `ProcessedMessage` EF Core mapping + table, migrations, SQL idempotency store, atomic claim/dedupe.
- `IIntegrationEventConsumerRegistry` implementation and per-module handler mapping/DI registration.
- The first real consumer (needs the neutral event per R1).
- Saga orchestration, `InventoryOperations`, Payments, and any second idempotency abstraction.
- Phase 6 publisher/outbox/dispatcher behavior (verified unchanged).