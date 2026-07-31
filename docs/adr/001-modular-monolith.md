# ADR-001: Modular Monolith Architecture

## Status

Accepted

## Context

The project must support core commerce workflows spanning multiple bounded contexts (Catalog, Cart, Orders, Inventory, Payments) while remaining operationally simple and evolvable. Distributed systems introduce complexity (network failure handling, sagas, message semantics) that provides no value while the platform has no demonstrated scaling or organizational need. At the same time, a monolithic codebase without boundaries historically erodes into unmaintainable coupled code that cannot be split later.

The project specification states the initial architecture must be a Modular Monolith and must explicitly not start as Microservices:

- `PROJECT_CONTEXT.md` §3 — "The initial architecture is a Modular Monolith with Clean Architecture and DDD principles, designed to support Event-Driven Architecture and future extraction of selected bounded contexts into independent services if justified."
- `PROJECT_CONTEXT.md` §4.1 — "The backend must initially be deployed as one application. Modules must have explicit boundaries."
- `ARCHITECTURE.md` §1 — "The system starts as a Modular Monolith. It is intentionally NOT implemented as Microservices from the beginning."
- `PHASE_0_ARCHITECTURE_FOUNDATION.md` §1 — "The system must start as a Modular Monolith" and must be designed so modules "can evolve independently and, if required in the future, be extracted into independent services with minimal architectural disruption."

## Decision

FluxCommerce is implemented as a Modular Monolith:

- A single deployable ASP.NET Core application containing all modules.
- Modules are separated by explicit, enforced boundaries, each owning its Domain, Application, Infrastructure, and Contracts layers.
- The architecture follows Clean Architecture and DDD principles.
- No distributed-system complexity is introduced before it provides real value.
- The design preserves a documented evolution path: strong module boundaries -> independent persistence boundaries -> asynchronous integration contracts -> optional service extraction.

## Consequences

Positive:

- One deployable unit with simple operational and development characteristics.
- Module ownership and boundaries are established while extraction is still cheap.
- Architecture rules can be enforced in-process with automated architecture tests (`ARCHITECTURE.md` §23, `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.12).
- Future extraction of a module (e.g., Orders, Inventory, Payments) is possible only because boundaries, contracts, and ownership rules exist from the start.

Negative:

- Module boundaries must be actively enforced (tests + review); they are not structural guarantees of the deployment topology.
- A single process shares resources (memory, failure domains); a crash or resource exhaustion affects all modules.
- Cross-module communication discipline must be maintained so that in-process shortcuts do not silently replace contract-based communication.

## Alternatives Considered

- Microservices from the beginning — explicitly rejected in `PROJECT_CONTEXT.md` §3 ("The system should not start as Microservices") and `ARCHITECTURE.md` §1 ("It is intentionally NOT implemented as Microservices from the beginning"); distributed complexity would be introduced without justification.
- A single giant monolith without module boundaries — rejected in `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.2 ("The structure must avoid a single giant shared Domain/Application/Infrastructure project for the entire system"), as it prevents module ownership and future extraction.
