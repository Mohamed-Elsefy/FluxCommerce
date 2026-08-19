# PHASE 8.1 COMPLETION REPORT — Saga Architecture & Baseline Audit

**Date:** 2026-08-19

## 1. Phase 8.1 Objective

Phase 8.1 establishes the final Saga design before implementing the Order / Inventory workflow
(plan §6). The sub-phase is an audit and design pass only — no production code was changed. Its
deliverables are:

- Approved Saga ownership boundaries.
- Approved Saga state model and allowed transitions.
- Approved message/event flow (commands/messages, result events, compensation).
- CorrelationId / CausationId propagation model.
- Identified persistence changes.
- Identified contract changes.
- ADR updates where architectural decisions are introduced.
- Baseline verification that the architecture boundaries remain valid.

## 2. Baseline Verification

The baseline was verified before and after the audit (no code changes were made):

| Check | Command | Result |
|-------|---------|--------|
| Solution build | `dotnet build FluxCommerce.slnx` | 0 warnings, 0 errors |
| Architecture tests | `dotnet test tests/Architecture/FluxCommerce.ArchitectureTests` | 163 passed, 0 failed |
| Unit tests | `dotnet test tests/UnitTests/FluxCommerce.UnitTests` | 1910 passed, 0 failed |

Branch: `feature/saga`. The Phase 8 plan (`docs/phases/PHASE_8_ORDER_INVENTORY_SAGA_IMPLEMENTATION_PLAN.md`)
is present but untracked in git.

## 3. Audit Findings

### 3.1 Orders module (post-Phase 7)

- **Order lifecycle.** `Order` aggregate is Pending -> Cancelled only. `Version` increments on every
  mutation (`Touch`), persisted as the `Version` column, but `OrderConfiguration` does NOT configure it
  as an EF concurrency token. There is no intermediate "awaiting reservation" status — the plan (§11)
  forbids a second independent Order lifecycle, so the Saga's progress must live in Saga state, not in
  `Order.Status`.
- **Domain events.** `OrderCreated` / `OrderCancelled` map to `orders.order_created` /
  `orders.order_cancelled` via `OrdersOutboxMessageFactory`; `CorrelationId` is injected from
  `ICorrelationIdProvider`. Both are IntegrationEvent-derived contracts in Orders.Contracts with a
  stable `EventType` const.
- **Projection.** `OrderSummaryProjectionConsumer` (HandlerName `Orders.ProjectOrderSummary`) handles
  ONLY `orders.order_created`. There is no consumer for `orders.order_cancelled`, so a Saga-driven
  cancellation would leave `OrderSummaryProjections.Status` stale (finding F5).
- **Persistence.** Orders DbContext has Orders, CheckoutIdempotencies, OutboxMessages,
  ProcessedMessages, OrderSummaryProjections. No Saga state table exists yet.

### 3.2 Inventory module (post-Phase 7)

- **Reservation lifecycle.** `InventoryReservation` is Pending -> Committed/Released/Expired; terminal
  states are immutable; `Version` is an optimistic concurrency token; a check constraint enforces
  positive quantity. `InventoryStore.TryReserveAsync` is a single atomic UPDATE guarded by availability,
  so concurrent reservations cannot oversell.
- **Commands.** `ReserveInventoryCommand(OrderId, Sku, Quantity, ExpiresAt)`,
  `ReleaseReservationCommand(ReservationId)`, `CommitReservationCommand(ReservationId)`,
  `ExpireReservationCommand` all resolve to application handlers; failures map through
  `InventoryReservationErrorMapper` to application errors (InvalidOrderId, InvalidReservationId,
  ReservationNotFound, InvalidExpiration, InsufficientStock, InvalidStateTransition,
  ReleaseExceedsReserved, CommitExceedsReserved, LifecycleFailed).
- **Integration contracts.** The ONLY Inventory integration event is
  `inventory.reservation_expired`. `InventoryOperations` defines `ReserveInventoryRequest`,
  `ReleaseInventoryRequest`, `CommitInventoryRequest`, and `InventoryReservationResult` as plain records
  explicitly marked as the future Order/Saga boundary (finding F1).
- **Expiration worker.** `ReservationExpirationBackgroundService` + `IExpiredReservationProcessor`
  exist; reservation expiration handling by the Saga is explicitly out of Phase 8 scope (finding F6).

### 3.3 Messaging, Outbox, idempotency, consumers

- Per-module Outbox commits atomically with business state; the dispatcher publishes to the Phase 6
  durable topic exchange (`fluxcommerce.events` / `fluxcommerce.events.it`). Outbox rows carry EventId,
  Type, Payload, CorrelationId, CausationId, OccurredAtUtc, Status, RetryCount, Error.
- Consumers: `RabbitMQConsumerBackgroundService` (per-channel prefetch, bounded concurrency, manual
  ACK, DLX dead-lettering); `IntegrationEventMessageProcessor` deserializes, validates the envelope,
  resolves the handler by EventType, runs the module idempotent-processing boundary (ProcessedMessage
  key = EventId + HandlerName committed atomically with business state, ADR-008), applies retry/backoff,
  and ACKs or dead-letters. Unknown event types are ACKed and skipped.
- `IntegrationEventEnvelope` exposes `ProducedEventCorrelationId` (= incoming CorrelationId) and
  `ProducedEventCausationId` (= incoming EventId) — the canonical propagation helpers (Phase 7.8) the
  Saga must use.
- API composition registers consumers explicitly:
  `AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, OrderSummaryProjectionConsumer, OrdersIdempotentProcessor>()`
  on queue `q.orders.summary`. Inventory registers its module infrastructure but no consumer queue yet.

### 3.4 Architecture boundary constraints (from the architecture test suite)

- `Orders_ShouldNot_ReferenceInventory` forbids ALL Orders assemblies from referencing ANY Inventory
  layer, including Contracts (finding F2).
- `Orders_ShouldNot_ReferenceOtherModules` forbids all other cross-module references except the
  Phase 4.5 carve-out (Orders.Application -> Cart/Catalog Contracts).
- `Inventory_ShouldNot_ReferenceOtherModules` forbids ALL Inventory assemblies from referencing any
  other module.
- `Orders_ShouldNot_IntroduceSagaOrOrchestratorTypes` and
  `Inventory_ShouldNot_IntroduceSagaOrOrchestratorTypes` forbid `*Saga` / `*Orchestrator` type names in
  all Orders/Inventory assemblies (finding F3).
- Consumers must reside in module Infrastructure; Domain/Application/Contracts must not reference
  RabbitMQ, the consumer transport, EF Core, or the Outbox/Idempotency abstractions; Contracts may
  reference `BuildingBlocks.Messaging` only.

## 4. Approved Saga Design

### 4.1 Ownership boundaries

- **Orders owns** Order state, the Order lifecycle, and Saga/orchestration state (plan §5.1).
- **Inventory owns** Inventory item state, reservation state, and the reservation lifecycle.
- The Saga coordinates the workflow; it never owns Order or Inventory business state, never writes to
  the Inventory database, and never reads from it. All coordination flows through explicit Integration
  Event contracts over RabbitMQ via each module's Outbox. No distributed transaction.

### 4.2 Saga state model (to be implemented in 8.2)

Persisted in the Orders database in a dedicated Saga table:

```text
OrderSaga
├── Id              (saga instance id)
├── OrderId         (unique)
├── CorrelationId
├── State
├── CreatedAtUtc
├── UpdatedAtUtc
└── Version         (optimistic concurrency token)
```

States:

| State | Meaning | Terminal |
|-------|---------|----------|
| Started | Saga created from OrderCreated | no |
| WaitingForInventory | ReserveInventory issued | no |
| Completed | InventoryReserved applied to Order | yes |
| Failed | InventoryReservationFailed applied to Order | yes |

Allowed transitions:

```text
Started --(OrderCreated)-------------------------> Started (no-op on duplicate)
Started --(issue ReserveInventory)---------------> WaitingForInventory
WaitingForInventory --(InventoryReserved)-------> Completed
WaitingForInventory --(InventoryReservationFailed) -> Failed
```

Duplicate result events are no-ops (message idempotency); conflicting results (Reserved after Failed,
or Failed after Completed) are rejected by transition validation and the Saga row's concurrency token.

### 4.3 Message / event flow

```text
OrderCreated (orders.order_created, C1)
   |
   v
Order Saga (Orders.Infrastructure)
   |  create/load Saga -> Started -> WaitingForInventory
   |  publish InventoryReservationRequested (inventory.reservation_requested, C1, caus=OrderCreated.EventId)
   v
RabbitMQ -> Inventory consumer (Inventory.Infrastructure)
   |  run ReserveInventoryCommandHandler within Inventory idempotency boundary
   v
Inventory Domain (atomic conditional reservation)
   |
   +-- success -> publish InventoryReserved (inventory.reserved, C1, caus=ReservationRequested.EventId)
   +-- failure -> publish InventoryReservationFailed (inventory.reservation_failed, C1, caus=ReservationRequested.EventId)
   |
   v
RabbitMQ -> Orders Saga
   |  WaitingForInventory --(InventoryReserved)-------> Completed  -> advance Order
   |  WaitingForInventory --(InventoryReservationFailed) -> Failed  -> cancel Order
   v
Compensation (8.7): on cancellation / failure recovery the Saga publishes
InventoryReleaseRequested (inventory.release_requested, ReservationId, C1, caus=<triggering EventId>)
   -> Inventory consumer -> ReleaseReservationCommandHandler (idempotent)
```

`CommitInventoryRequest` remains out of Phase 8 scope; `inventory.reservation_expired` is not consumed
by the Saga in this phase.

### 4.4 Consumers to be added (8.4 / 8.5 / 8.7)

| Handler | Module | HandlerName | Event | Action |
|---------|--------|-------------|-------|--------|
| InventoryReservationRequestedConsumer | Inventory.Infrastructure | `Inventory.ReserveInventory` | inventory.reservation_requested | ReserveInventory command |
| InventoryReservedConsumer | Orders.Infrastructure | `Orders.ApplyReservationSuccess` | inventory.reserved | Advance Saga + Order |
| InventoryReservationFailedConsumer | Orders.Infrastructure | `Orders.ApplyReservationFailure` | inventory.reservation_failed | Fail Saga + cancel Order |
| InventoryReleaseRequestedConsumer | Inventory.Infrastructure | `Inventory.ReleaseReservation` | inventory.release_requested | ReleaseReservation command |

### 4.5 Correlation / causation propagation

`OrderCreated(C1)` -> `ReservationRequested(C1, caus = OrderCreated.EventId)` ->
`InventoryReserved/Failed(C1, caus = ReservationRequested.EventId)` -> Order transition. Every
Saga-produced event uses `envelope.ProducedEventCorrelationId` and
`envelope.ProducedEventCausationId` (Phase 7.8). This matches plan §5.6 exactly.

## 5. Identified Persistence Changes (implemented in 8.2+)

- Orders database: new `OrderSaga` table (schema above) with a unique index on `OrderId`, optimistic
  concurrency on `Version`, and a new EF migration. It stores only workflow progress, never Order or
  Inventory business state.
- Orders database: configure `Order.Version` as an EF concurrency token (`IsConcurrencyToken()`) so
  Saga-driven Order transitions and conflicting-result handling (8.6/8.8) are guarded at the database.
- Inventory database: no new persistence; result events are published through the existing Inventory
  Outbox.

## 6. Identified Contract Changes (implemented in 8.4/8.5/8.7)

New IntegrationEvent-derived contracts in `Inventory.Contracts.IntegrationEvents`, each with a stable
`EventType` const and canonical metadata (per ADR-009):

- `InventoryReservationRequestedIntegrationEvent` — `inventory.reservation_requested`.
- `InventoryReleaseRequestedIntegrationEvent` — `inventory.release_requested`.
- `InventoryReservedIntegrationEvent` — `inventory.reserved`.
- `InventoryReservationFailedIntegrationEvent` — `inventory.reservation_failed`.

The existing `InventoryOperations` records remain the application-facing payload definitions. No
existing contract is modified.

## 7. ADR Updates

- **Created: ADR-009 — Saga Orchestration & Contract Placement.** Records the ownership boundary, the
  placement of the Saga wire contracts in Inventory.Contracts, the narrow Orders.Infrastructure ->
  Inventory.Contracts reference, the Saga type placement in Orders.Infrastructure, the correlation /
  causation model, and the idempotency + concurrency strategy.

## 8. Verification

- Architecture boundaries remain valid: verified by the passing 163 architecture tests and the
  build (0 warnings, 0 errors). No cross-module dependency was introduced by this sub-phase.
- No implementation begins before the ambiguous decisions are resolved: the decisions above (ADR-009)
  resolve the open points (contract placement, ownership, cross-module reference, Saga type naming,
  correlation model) before 8.2 starts.
- The `*Saga` / `*Orchestrator` architecture tests and the `Orders_ShouldNot_ReferenceInventory`
  carve-out are documented as required 8.2 changes so the phase is not surprised when the Saga type is
  introduced.

## 9. Findings Deferred to 8.2+

| # | Finding | Deferred to | Action required |
|---|---------|-------------|-----------------|
| F1 | No Saga wire contracts exist; InventoryOperations records are not IntegrationEvents | 8.4/8.5 | Add the four new contracts listed in §6 |
| F2 | Orders.Infrastructure needs a narrow reference to Inventory.Contracts | 8.2 | Update `Orders_ShouldNot_ReferenceOtherModules` and `Orders_ShouldNot_ReferenceInventory` with the carve-out |
| F3 | `*Saga` / `*Orchestrator` type names are forbidden by architecture tests | 8.2/8.3 | Revise both tests when the Saga type is introduced |
| F4 | `Order.Version` is not an EF concurrency token | 8.2/8.8 | Configure `IsConcurrencyToken()` + migration |
| F5 | No consumer for `orders.order_cancelled`; projection goes stale on Saga-driven cancellation | 8.5/8.6 | Add an OrderCancelled projection consumer or update the projection on the Saga failure path |
| F6 | Reservation expiration is out of Phase 8 scope | Later phase | None for Phase 8; `inventory.reservation_expired` stays unhandled (ACK + skip) |