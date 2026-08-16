# ADR-008: Consumer Contract & Idempotency Identity

## Status

Accepted

## Context

Phase 7 mandates idempotent consumers. The Phase 7.1 audit confirmed
the publish side (Outbox + RabbitMQ publisher, `BuildingBlocks.Outbox` and
`BuildingBlocks.Infrastructure` messaging) is complete and must remain unchanged, while no production
consumer exists and the idempotency contracts (`ProcessedMessage`, `IdempotencyResult`,
`IIdempotencyStore`, `IDuplicateDetector`) are dormant — they have no EF mapping, table, or
implementation. See `docs/phases/PHASE_7_IDEMPOTENT_CONSUMERS.md` for the phase scope.

Three gaps drive this decision:

- **G1 (no consumer contract):** there is no transport-agnostic handler abstraction. A consumer that
  reaches for `RabbitMQ.Client` directly would bind module business logic to the transport and make
  idempotency (G2) and later Saga/handler work impossible to test without a broker.
- **G2 (idempotency identity):** RabbitMQ `MessageId` equals `EventId`; the audit resolved that a
  distinct delivery `MessageId` is not introduced. With At-Least-Once delivery, one logical event may
  be delivered more than once to the same consumer, and the same event is legitimately consumed by
  multiple handlers (e.g. one projection, one saga step). Idempotency therefore must be scoped per
  consumer/handler, not per event alone, and it must compose with the existing `ProcessedMessage`
  contracts rather than introduce a second idempotency abstraction.
- **G6 (background identity):** consumers execute in background scopes with no HTTP request, so there
  is no request `ICurrentUser`. The processing scope must resolve a non-request identity.

## Decision

- **Consumer contract (transport-agnostic).** `BuildingBlocks.Messaging` (contract-only, dependency-free)
  gains a `Consuming` surface: `IntegrationEventEnvelope` (processing context: EventId, EventType,
  EventVersion, CorrelationId, CausationId, OccurredAtUtc), `IIntegrationEventHandler<TEvent>`
  (handler with a stable `HandlerName` and `HandleAsync(event, envelope, token)`),
  `IntegrationEventConsumerDescriptor` (EventType, EventContractType, HandlerType), and
  `IIntegrationEventConsumerRegistry` (deterministic `Resolve(eventType)`). The consumer contract
  references no transport, no Outbox, no EF Core, and no idempotency persistence — enforced by an
  architecture test.
- **Handler resolution.** EventType -> contract -> handler is explicit and deterministic: a registry
  maps the wire type to a CLR contract for deserialization (switch-on-Type, mirroring the Phase 6
  publisher) and to a DI-registered handler type. No reflection-based activation; handlers are plain
  DI types. Unknown event types resolve to `null` and are skipped.
- **Idempotency identity (R2).** Idempotency identity = `EventId + HandlerName`, composed via the
  canonical `ProcessedMessage.Create(envelope.EventId, handler.HandlerName)`. The publisher and the
  AMQP `MessageId` (= `EventId`) are unchanged. The idempotency persistence and atomic claim are not
  implemented here; `BuildingBlocks.Idempotency` contracts remain the single idempotency model.
- **Neutral demonstration event (R1).** No existing integration event supports a non-Saga projection
  consumer without inventing business semantics. A dedicated neutral integration event is required for
  the first real consumer; its definition and producer wiring are deferred to a later sub-phase and
  will not be invented casually.
- **Background identity (R6).** Consumer/background scopes resolve a non-request `ICurrentUser`
  (unauthenticated), matching the existing test `AnonymousCurrentUser` precedent. No authentication
  infrastructure change; the consumer scope simply does not carry a request identity.

## Consequences

Positive:

- Consumers are written against a transport-agnostic contract, testable without a broker and reusable
  by any future transport.
- Idempotency identity distinguishes event identity, consumer/handler identity, and idempotency
  identity explicitly, and reuses the canonical `ProcessedMessage` contracts (no second abstraction).
- Handler registration stays deterministic, DI-friendly, and safe (no untrusted type activation).
- Phase 6 publish-side behavior is untouched.

Negative:

- A real broker consumer, queue/binding/DLQ setup, ACK/NACK/retry, `ProcessedMessage` persistence,
  migrations, the SQL idempotency store, the atomic claim, and the first real consumer remain to be
  implemented in Phase 7.3+.
- The architecture test forbidding Messaging from referencing Idempotency means the composition
  (`ProcessedMessage.Create`) lives at the processing layer, not in the contract.

## Alternatives Considered

- **Delivery-scoped `MessageId` (G2 alt):** giving every delivery a distinct broker `MessageId`
  (`EventId + delivery ordinal`) and keying idempotency on it — rejected: it changes the Phase 6
  publisher, conflates delivery identity with event identity, and breaks the documented
  `MessageId = EventId` contract.
- **Keying idempotency on `EventId` alone (G2 alt):** would drop legitimate duplicate deliveries to
  distinct handlers (projection + saga step) — rejected.
- **Reflection-based handler discovery (G1 alt):** scanning assemblies for handler types — rejected:
  non-deterministic, untestable mapping, and fragile against untrusted payloads.
- **First consumer as a Saga (R1 alt):** demonstrating via a Saga — rejected: Saga orchestration is a
  later phase with its own commands/state, and a projection consumer better exercises the contract
  without introducing orchestration prematurely.