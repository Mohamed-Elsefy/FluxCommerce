# ADR-009: Saga Orchestration & Contract Placement

## Status

Accepted

## Context

Phase 8 introduces the first reliable cross-module business workflow: the Order / Inventory Saga. The
Phase 8.1 baseline audit (`docs/completed/PHASE_8_1_SAGA_BASELINE_AUDIT.md`) re-audited the Orders and
Inventory modules after Phase 7 and confirmed the plan's architectural position (plan §2, §5):

- Orders owns Order state, the Order lifecycle, and Saga/orchestration state (plan §5.1).
- Inventory owns Inventory item state, reservation state, and the reservation lifecycle (plan §5.1).
- The Saga coordinates the workflow; it does not own Order or Inventory business state.
- Coordination happens only through explicit Integration Event contracts over RabbitMQ, published
  reliably through each module's Outbox, consumed by the idempotent consumer infrastructure.
- No cross-module database access and no distributed transactions.

The audit surfaced six findings that force architectural decisions before implementation begins:

- **F1 — No Saga wire contracts exist.** The only Inventory integration event is
  `inventory.reservation_expired`. The `InventoryOperations` request/result records
  (`ReserveInventoryRequest`, `ReleaseInventoryRequest`, `CommitInventoryRequest`,
  `InventoryReservationResult`) are plain records: they do not derive from `IntegrationEvent`, declare
  no `EventType` const, and carry no canonical metadata, so they cannot flow through the Outbox /
  RabbitMQ pipeline (the publisher and `IntegrationEventTypeResolver` require an `IntegrationEvent`
  contract with a public `const string EventType`).
- **F2 — Module isolation conflicts with the required consumer wiring.** The Saga (Orders-owned) must
  publish a ReserveInventory request and consume Inventory result events. Implementing
  `IIntegrationEventHandler<InventoryReservedIntegrationEvent>` in Orders.Infrastructure requires
  Orders to reference the Inventory event contracts. `Orders_ShouldNot_ReferenceInventory` and
  `Orders_ShouldNot_ReferenceOtherModules` currently forbid Orders assemblies from referencing ANY
  Inventory layer, including Contracts.
- **F3 — Saga type names are forbidden by architecture tests.**
  `Orders_ShouldNot_IntroduceSagaOrOrchestratorTypes` and
  `Inventory_ShouldNot_IntroduceSagaOrOrchestratorTypes` fail any type whose name ends in `Saga` or
  `Orchestrator` in the module assemblies. The Saga orchestrator type introduced in 8.3 will trip the
  Orders test.
- **F4 — `Order.Version` is not an EF concurrency token.** `OrderConfiguration` persists `Version` but
  does not call `IsConcurrencyToken()`. The Saga's Order transitions (8.6) and conflicting-result
  handling (8.8) need optimistic concurrency protection at the database.
- **F5 — The Order Summary projection is stale on cancellation.** The projection consumer handles only
  `orders.order_created`. There is no consumer for `orders.order_cancelled`, so a Saga-driven
  cancellation (reservation failure) would leave `OrderSummaryProjections.Status` stale.
- **F6 — Reservation expiration is out of Phase 8 scope.** The plan (§4) explicitly excludes the
  reservation expiration worker; the Saga will not consume `inventory.reservation_expired` in this
  phase (unknown event types are ACKed and skipped per ADR-008).

## Decision

- **Ownership boundary.** Saga/orchestration state is owned by Orders and persisted in the Orders
  database in a dedicated Saga state table (introduced in 8.2). It stores only workflow progress
  (Id, OrderId, CorrelationId, State, timestamps, concurrency Version) and never duplicates Order or
  Inventory business state.
- **Contract placement.** The new Saga wire contracts live in `Inventory.Contracts` — the module that
  owns the operations and state they describe — consistent with ADR-007:
  - `InventoryReservationRequestedIntegrationEvent` (`inventory.reservation_requested`) —
    OrderId, Sku, Quantity, ExpiresAt.
  - `InventoryReleaseRequestedIntegrationEvent` (`inventory.release_requested`) — ReservationId.
  - `InventoryReservedIntegrationEvent` (`inventory.reserved`) — ReservationId, OrderId, Sku,
    Quantity, ExpiresAt, Status, CreatedAtUtc, UpdatedAtUtc, Version.
  - `InventoryReservationFailedIntegrationEvent` (`inventory.reservation_failed`) — OrderId, Sku,
    Quantity, Reason.
  All derive from `IntegrationEvent` and declare the stable `EventType` const. The existing
  `InventoryOperations` records remain the application-facing payload definitions and are reused as
  the payload basis. `CommitInventoryRequest` stays unused in Phase 8 (commit is out of scope).
- **Approved cross-module reference.** Orders.Infrastructure MAY reference
  `FluxCommerce.Modules.Inventory.Contracts` (the `Integration` and `IntegrationEvents` namespaces
  only) so it can publish the request contracts and implement the result-event consumers. This mirrors
  the existing Phase 4.5 carve-out that lets Orders.Application reference the Cart/Catalog Contracts.
  The architecture tests `Orders_ShouldNot_ReferenceOtherModules` and
  `Orders_ShouldNot_ReferenceInventory` are updated in 8.2 to encode this narrow carve-out. Inventory
  never references Orders.
- **Saga type placement.** The Saga orchestrator lives in Orders.Infrastructure, beside the module
  DbContext, idempotency store, and consumers it shares. The `*Saga` / `*Orchestrator` architecture
  tests are revised in 8.2 (narrowed so the rule no longer applies to Orders.Infrastructure) when the
  Saga type is introduced.
- **Correlation / causation.** Every Saga-produced event uses the Phase 7.8 envelope helpers:
  `ProducedEventCorrelationId` (= incoming CorrelationId) and `ProducedEventCausationId` (= incoming
  EventId). Chain: `OrderCreated(C1)` -> `InventoryReservationRequested(C1, caus = OrderCreated.EventId)`
  -> `InventoryReserved` / `InventoryReservationFailed(C1, caus = ReservationRequested.EventId)` ->
  Order transition. This matches plan §5.6.
- **Idempotency & concurrency.** Each consumer runs inside its module's idempotent-processing
  boundary (EventId + HandlerName, ADR-008). Saga state transitions are additionally guarded by the
  Saga row's optimistic concurrency token, so duplicate or conflicting results (Reserved vs Failed)
  cannot be applied twice (plan §5.5, §8.8).

## Consequences

Positive:

- The Saga is implementable without violating the Modular Monolith boundaries: coordination flows only
  through explicit contracts, and each module keeps full ownership of its state.
- New wire event types are additive; existing producers and consumers are untouched, and unknown event
  types continue to be ACKed and skipped.
- Correlation and causation stay canonical and lossless across every async hop.

Negative:

- Orders.Infrastructure gains a narrow, approved dependency on Inventory.Contracts; the architecture
  tests must encode the carve-out as part of 8.2 so the boundary keeps being enforced.
- The architecture tests that forbid `*Saga` / `*Orchestrator` type names must be revised when the
  Saga type is introduced in 8.2/8.3.
- `Order.Version` must be configured as a concurrency token and the Order Summary projection must
  learn about cancellation; both are follow-up items implemented in 8.2+ (F4, F5), not in 8.1.

## Alternatives Considered

- **Hosting the Saga request/result contracts in Orders.Contracts** — rejected: the contracts describe
  Inventory operations/state, so per ADR-007 they belong to Inventory; hosting them in Orders would
  force Inventory to reference Orders.Contracts to consume them (a symmetric boundary violation).
- **Hosting Saga state in a new third module / database** — rejected: plan §5.1 explicitly assigns
  Saga/orchestration state to Orders, and a third module adds deployment and operational cost without
  an ownership benefit.
- **Placing the Saga in Domain or Application** — rejected: the Saga is transport-coupled orchestration
  and must live in Infrastructure beside the consumers and persistence it uses; the consumer
  architecture tests already require consumer handlers to reside in module Infrastructure.
- **Relocating the result events to a neutral shared contract assembly** — rejected: it would fragment
  the module-owned contract model established by ADR-007 and defeat the single-source-of-truth
  placement of integration contracts.