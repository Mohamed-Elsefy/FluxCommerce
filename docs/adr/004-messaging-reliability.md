# ADR-004: Messaging and Reliability Foundation

## Status

Accepted

## Context

Cross-module workflows (order placement, inventory reservation, payments) require asynchronous communication between modules that is reliable and traceable. Publish-after-commit ("Database Commit -> Publish Message") can lose messages or publish duplicates when the broker fails between commit and publish. The specification mandates At-Least-Once delivery, so duplicate message processing is an expected condition that consumers must survive. Phase 0 must establish the contracts and connectivity for this foundation without implementing business event flows.

The messaging and reliability requirements are documented:

- `PROJECT_CONTEXT.md` §8 — the Outbox pattern guarantees reliable event publication; business state change and outbox record share the same database transaction; "The Outbox is not the Message Broker."
- `PROJECT_CONTEXT.md` §9 — RabbitMQ is the initial Message Broker; messaging must support retry, manual ACK, dead-letter exchange/queue, idempotent consumers, and correlation IDs.
- `PROJECT_CONTEXT.md` §4.5 — "At-Least-Once Delivery" is assumed; "every message consumer that performs business state changes must be designed to be idempotent."
- `ARCHITECTURE.md` §12-§14 — Outbox pattern with required metadata (Message ID, Event ID, Event Type, Payload, OccurredAtUtc, CorrelationId, CausationId, CreatedAtUtc, ProcessedAtUtc, retry/error information); At-Least-Once semantics; RabbitMQ as the asynchronous messaging infrastructure.
- `ARCHITECTURE.md` §16 and `PROJECT_CONTEXT.md` §4.6 — idempotency is required at multiple levels (API requests, message consumers, outbox dispatch, payment webhooks, saga commands); the strategy depends on the operation.
- `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.8-§0.9 — Phase 0 defines the contracts and architectural foundations (integration events, event metadata, event versioning, correlation/causation IDs, outbox contracts, idempotency contracts) and connectivity; full production outbox dispatching and real idempotent consumers belong to later phases.

## Decision

FluxCommerce establishes the messaging and reliability foundation as follows:

- **RabbitMQ** is the asynchronous messaging infrastructure (connectivity only in Phase 0; no exchanges, queues, consumers, or publishers are created yet).
- **Integration events** are the cross-module communication mechanism; they carry event metadata including Event ID, CorrelationId, CausationId, and OccurredAtUtc, and are versioned (`EventVersion`).
- **Transactional Outbox** is the publication mechanism: business state change and outbox record are written in the same transaction, then an outbox dispatcher publishes to RabbitMQ. Phase 0 defines the outbox contracts (`IOutboxStore`, `IOutboxDispatcher`, `OutboxMessage`, `OutboxStatus`) without the dispatcher implementation.
- **At-Least-Once delivery** is the assumed messaging semantic; exactly-once delivery is never assumed.
- **Idempotent consumers** are the required processing pattern: consumers must safely handle duplicate delivery. Phase 0 defines the idempotency contracts (`IIdempotencyStore`, `IDuplicateDetector`, `ProcessedMessage`).
- Business integration events, consumers, sagas, and compensation workflows are explicitly out of Phase 0 scope.

## Consequences

Positive:

- Event publication becomes reliable: a message cannot be lost between commit and publish.
- Consumers are designed for duplicate-safe, traceable processing from the start.
- Phase 0 delivers the contracts, so later phases implement workflows without redesigning the messaging foundation.

Negative:

- Outbox and idempotency add processing layers and complexity to every future event-driven flow.
- At-Least-Once semantics mean consumers must always carry idempotency overhead.
- Full outbox dispatch and consumer infrastructure remain unimplemented until the relevant phases (Outbox + RabbitMQ, Idempotent Consumers per `PROJECT_CONTEXT.md` §14).

## Alternatives Considered

- Exactly-Once delivery — rejected: `ARCHITECTURE.md` §13 states "Exactly-Once Delivery must not be assumed"; `PROJECT_CONTEXT.md` §4.5 mandates At-Least-Once assumptions.
- Publish-after-commit without an outbox — rejected by the documented transactional outbox architecture (`PROJECT_CONTEXT.md` §8, `ARCHITECTURE.md` §12), which explicitly avoids the unsafe "Database Commit -> Publish Message" flow.
- Alternative message brokers — not documented; `PROJECT_CONTEXT.md` §9 and `ARCHITECTURE.md` §14 designate RabbitMQ as the messaging infrastructure.
