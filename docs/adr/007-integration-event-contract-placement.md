# ADR-007: Integration Event Contract Placement (G12)

## Status

Accepted

## Context

Phase 6 requires stable Integration Event contracts that cross module boundaries and are transported
through the Outbox and RabbitMQ. The Phase 6 audit (`docs/IMPLEMENTATION_PLAN.md#phase-6--outbox--rabbitmq-`)
recorded an unresolved structural tension — gap **G12**: the module *Contracts* projects were forbidden
by architecture tests from referencing `BuildingBlocks.Messaging`
(`Orders_/Inventory_Contracts_ShouldNot_ReferenceMessagingOrOutbox` and the module-wide
`Orders_/Inventory_ShouldNot_ReferenceMessagingOrRabbitMq` rules), yet `ARCHITECTURE.md` §6 lists
"Integration Event contracts" as an example of the content of a module's Contracts project, and the
canonical `IIntegrationEvent` / `IntegrationEvent` / `EventVersion` metadata types live in
`BuildingBlocks.Messaging` and had zero usages.

The Phase 6.4 scope mandates reusing `IIntegrationEvent`, `IntegrationEvent`, and `EventVersion` and
resolving the overlap with `EventMetadata`. Two options were available (documented in the 6.1 audit):

- (a) Keep integration-event contracts in module Contracts as plain, self-contained records and use
  `BuildingBlocks.Messaging` only as the transport/outbox metadata carrier — leaving the canonical
  base contracts permanently unused and forcing a second metadata model.
- (b) Place the integration-event contracts in module Contracts deriving from the canonical
  `BuildingBlocks.Messaging` base and refine the architecture rules with an explicit decision.

`BuildingBlocks.Messaging` is contract-only: it contains dependency-free metadata types
(`IIntegrationEvent`, `IntegrationEvent`, `EventVersion`) and no RabbitMQ client, no EF Core, and no
transport or infrastructure implementation.

## Decision

- **Placement.** Each module's Integration Event contracts live in that module's **Contracts** project
  (namespace `FluxCommerce.Modules.<Module>.Contracts.IntegrationEvents`) and derive from the
  canonical `IntegrationEvent` base in `BuildingBlocks.Messaging`. This is the documented home for
  integration-event contracts (`ARCHITECTURE.md` §6) and keeps contracts free of Domain, Application,
  Infrastructure, EF Core, the RabbitMQ client, and the Outbox abstraction.
- **Rule refinement.** The architecture rules are refined as follows (enforced by tests):
  - Module **Domain** and **Application** must not reference `BuildingBlocks.Messaging` or
    `RabbitMQ.Client`.
  - Module **Infrastructure** must not reference `RabbitMQ.Client`. It necessarily depends on the
    contract-only Messaging metadata *transitively* through the Contracts events it serializes into the
    Outbox (NetArchTest dependency analysis follows base types); that is intentional and harmless.
  - Module **Contracts** may reference `BuildingBlocks.Messaging` (the contract-only base) but must not
    reference `RabbitMQ.Client` or `BuildingBlocks.Outbox`.
  - Positive rules pin the intentional dependency: at least one Contracts type in each module must
    depend on `BuildingBlocks.Messaging`, so the decision cannot be silently reverted.
- **Canonical metadata.** `IIntegrationEvent` + `IntegrationEvent` + `EventVersion` is the single
  canonical metadata model. The overlapping, unused `EventMetadata` record is removed. The stable event
  type identifier is the per-contract `EventType` constant (e.g. `orders.order_created`), mirrored by
  the Outbox `Type` column and the future RabbitMQ routing key.

## Consequences

Positive:

- Integration Event contracts are reusable cross-module contracts (consumers depend on `Module.Contracts`,
  not module internals), matching `ARCHITECTURE.md` §6 and `PROJECT_CONTEXT.md` §4.1/§6.
- The canonical metadata base (`IIntegrationEvent`/`IntegrationEvent`/`EventVersion`) is finally used;
  no duplicate metadata model remains.
- Contracts stay free of transport, persistence, and business-layer implementation details.
- Module isolation is preserved: Domain and Application still cannot see Messaging; only the Contracts
  surface (and Infrastructure's serialization of those contracts) touches the contract-only metadata.

Negative:

- Module Infrastructure now depends on the Messaging metadata transitively via the Contracts it
  serializes; this is detectable by dependency analysis and must be understood when future rules are
  added.
- A module Contracts project now carries a (contract-only) Messaging reference; reviewers must not
  reintroduce a blanket "Contracts must not reference Messaging" rule without revisiting this ADR.

## Alternatives Considered

- Option (a) — plain self-contained contracts in module Contracts, `BuildingBlocks.Messaging` unused:
  rejected. It leaves the canonical base and versioning contract dead code (unresolved G4), forces the
  Outbox Type/payload to diverge from the metadata model, and duplicates metadata concepts.
- Placing contracts in a shared `BuildingBlocks.Messaging` assembly: rejected. Concrete integration
  events are module-owned business contracts, not cross-cutting technical primitives
  (`ADR-003`; `ARCHITECTURE.md` §4 forbids business-specific logic in BuildingBlocks).
