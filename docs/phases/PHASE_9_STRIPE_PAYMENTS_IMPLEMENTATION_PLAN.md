# Phase 9 — Stripe Payments
## Implementation Plan

**Phase:** 9  
**Name:** Stripe Payments  
**Status:** Planned  
**Prerequisites:** Phase 6 — Outbox + RabbitMQ, Phase 7 — Idempotent Consumers, Phase 8 — Order / Inventory Saga  
**Next Major Phase:** Phase 10 — Payment Saga + Compensation

---

# 1. Phase Objective

Phase 8 proved that a reliable cross-module workflow can coordinate Order and Inventory through asynchronous contracts. Phase 9 introduces the first **external, untrusted system** into FluxCommerce: Stripe.

The objective is to build a **Payments bounded context** that owns Payment business state and integrates with Stripe as a Payment provider, while preserving:

- Modular Monolith boundaries.
- Module ownership.
- Module-per-DbContext.
- Clean Architecture (Domain independent of Stripe).
- At-Least-Once messaging.
- Idempotent processing (Payment Intent + Webhook).
- Outbox-based publication.
- Correlation/Causation propagation.
- No distributed transactions.

Phase 9 makes Payment capability reliable and consumable by the Saga. It does **not** complete the Order lifecycle end-to-end — connecting Payment outcomes to Order/Inventory compensation is Phase 10.

---

# 2. Architectural Position

The workflow introduced by this phase is:

```text
Orders / Saga
  |
  | Payment Request
  v
Payments Module
  |
  v
Payment Application
  |
  v
Payment Domain
  |
  v
PaymentsDbContext
  |
  v
Stripe Adapter
  |
  v
Stripe
  |
  | Webhook
  v
Payment Webhook Endpoint
  |
  v
Signature Validation
  |
  v
Idempotent Processing
  |
  v
Payment State Update
  |
  v
PaymentSucceeded / PaymentFailed
  |
  v
Outbox
  |
  v
RabbitMQ
  |
  v
Orders / Saga
```

The following boundary is mandatory:

```text
Orders
  |
  X --> Payments.Infrastructure
```

```text
Payments
  |
  X --> Orders.Infrastructure
  X --> Inventory.Infrastructure
```

Instead:

```text
Orders / Saga
  |
  v
Explicit Message Contract
  |
  v
RabbitMQ
  |
  v
Payments Module
  |
  v
Payment State
```

Payments must never directly modify Order state. Orders must never directly modify Payment state. Stripe must never become the domain source of truth — it is an external reference held by the Payment aggregate.

---

# 3. Scope

Phase 9 consists of the following sub-phases:

1. **9.1 — Baseline & Payment Architecture Audit**
2. **9.2 — Payments Module Foundation**
3. **9.3 — Payment Domain Model & Lifecycle**
4. **9.4 — Payment Persistence & Concurrency**
5. **9.5 — Stripe Integration Boundary**
6. **9.6 — Payment Intent Creation**
7. **9.7 — Payment Intent Idempotency**
8. **9.8 — Stripe Webhook Endpoint & Security**
9. **9.9 — Webhook Idempotent Processing**
10. **9.10 — Stripe Event → Payment State**
11. **9.11 — Payment Integration Events & Saga Integration**
12. **9.12 — Testing, Observability & Production Hardening**
13. **9.13 — Final Verification & Phase Completion**

Sub-phases must be implemented sequentially. Do not implement later sub-phases prematurely unless required by a dependency that is explicitly documented.

---

# 4. Phase Boundaries

## Included

- Payments module foundation (Domain, Application, Infrastructure, Contracts).
- `PaymentsDbContext`.
- Payment domain model and lifecycle/invariants.
- Payment persistence and concurrency protection.
- Stripe integration boundary (`IStripePaymentGateway`).
- Stripe Payment Intent creation.
- Payment Intent idempotency (application-level + Stripe-level).
- Stripe webhook endpoint.
- Stripe webhook signature verification.
- Webhook idempotent processing.
- Mapping of Stripe events to Payment domain state.
- Payment integration events, owned by `Payments.Contracts`.
- Outbox and RabbitMQ integration (reusing Phase 6/7 infrastructure).
- Authorization/ownership checks on Payment APIs.
- Localization, Swagger, observability.
- Unit and integration tests, including real infrastructure where required.
- Failure/retry/idempotency verification.
- Documentation and ADRs where architectural decisions are introduced.

## Explicitly Out of Scope

The following must NOT be implemented as part of Phase 9:

- Payment Saga.
- Refund orchestration.
- Inventory compensation triggered by Payment failure.
- Order compensation triggered by Payment failure.
- Reservation expiration workflow.
- Shipping workflows.
- Notifications.
- Analytics.
- Microservice extraction.

Payment failure/success **may be published** as an integration event in 9.11, but consuming it to compensate Inventory or cancel/confirm Order belongs to Phase 10. Reservation expiry belongs to Phase 11. Full failure-injection campaigns belong to Phase 12.

---

# 5. Architectural Rules

The implementation agent MUST preserve the following rules.

## 5.1 Module Ownership

Payments owns:

- Payment state.
- Payment lifecycle.
- Payment Attempt / Transaction state.
- Webhook processing state.

Orders/Saga continues to own:

- Order state.
- Order/Saga orchestration state.

Payments does not become the owner of Order state, and must not transition Order state directly.

---

## 5.2 No Cross-Module Database Access

Forbidden:

```text
Payments
  |
  +--> OrdersDbContext
```

Forbidden:

```text
Orders
  |
  +--> PaymentsDbContext
```

Required:

```text
Module A
  |
  +--> Explicit Contract
          |
          v
      Module B
```

---

## 5.3 No Distributed Transactions

A transaction must remain local to one module. For example:

```text
Payments Transaction
    |
    +--> Update Payment
    |
    +--> Record processed Webhook Event
    |
    +--> Create Outbox Message
    |
    +--> Commit
```

Cross-module coordination must use messaging, not shared transactions.

---

## 5.4 At-Least-Once Delivery

Exactly-once delivery must never be assumed — neither for RabbitMQ messages nor for Stripe webhooks. Every webhook handler and consumer must tolerate duplicate delivery.

---

## 5.5 Idempotency

There are three related requirements:

```text
Payment Intent Idempotency
        +
Webhook Idempotency
        +
Integration Event / Consumer Idempotency
```

A duplicate Payment Intent request must not create a second Stripe PaymentIntent for the same business operation. A duplicate or concurrent webhook delivery must not produce a duplicate business effect or an invalid state transition.

---

## 5.6 Stripe Isolation

```text
Payments.Domain
    X--> Stripe SDK
    X--> EF Core
    X--> RabbitMQ.Client

Payments.Application
    X--> Stripe SDK
    X--> RabbitMQ.Client

Payments.Contracts
    X--> EF Core
    X--> Stripe SDK
    X--> RabbitMQ.Client
    X--> Payments.Domain
```

Stripe SDK usage is confined to `Payments.Infrastructure`, behind `IStripePaymentGateway`.

---

## 5.7 Correlation and Causation

The workflow must preserve:

- EventId / MessageId
- CorrelationId
- CausationId
- TraceId where available
- StripeEventId (webhook-specific)
- StripePaymentIntentId (provider reference)

Example:

```text
Payment Requested
    CorrelationId = C1

Stripe PaymentIntent Created
    CorrelationId = C1
    CausationId  = PaymentRequested.EventId

Stripe Webhook Received
    StripeEventId = evt_123

Payment Succeeded (Integration Event)
    CorrelationId = C1
    CausationId  = Webhook processing context
```

---

# 6. 9.1 — Baseline & Payment Architecture Audit

## Objective

Establish the final Payment design before writing implementation code, and identify the exact integration point with the Phase 8 Saga.

## Tasks

- Re-audit `PROJECT_CONTEXT.md` and `ARCHITECTURE.md`.
- Re-audit the Orders module and Saga implementation after Phase 8.
- Inspect `BuildingBlocks.Messaging`, Outbox infrastructure, and consumer infrastructure.
- Inspect existing idempotency infrastructure and reuse patterns.
- Inspect existing Integration Event contract conventions (ownership in module `Contracts`).
- Inspect persistence, API, localization, Swagger, and error-handling conventions.
- Inspect existing architecture tests.
- Determine how the Saga will request a Payment (command/contract shape) without modifying Saga business logic prematurely.
- Define the Payments ownership boundary.
- Define candidate Payment domain states and transitions (to be finalized in 9.3).
- Identify any required ADR updates.

## Rules

Do NOT, in this sub-phase:

- Redesign the Saga.
- Rebuild Outbox or consumer infrastructure.
- Add the Stripe SDK to any Domain project.
- Create a generic multi-provider payment abstraction without a concrete need.

## Deliverables

- Approved Payment boundary and ownership model.
- Approved integration point with the Phase 8 Saga.
- Identified persistence and contract changes.
- ADR updates where architectural decisions are introduced.

## Verification

- Architecture boundaries remain valid.
- No cross-module dependency is introduced.
- No duplicated abstractions are introduced.

---

# 7. 9.2 — Payments Module Foundation

## Objective

Create the Payments module skeleton following the project's established module structure.

## Structure

```text
FluxCommerce.Modules.Payments.Domain
FluxCommerce.Modules.Payments.Application
FluxCommerce.Modules.Payments.Infrastructure
FluxCommerce.Modules.Payments.Contracts
```

```text
PaymentsDbContext
```

## Tasks

- Scaffold the four layers.
- Register DI for each layer.
- Add configuration binding (including Stripe settings placeholders, externalized — no secrets in source control).
- Add EF Core design-time factory for `PaymentsDbContext`.
- Register persistence and module in the composition root.
- Add architecture tests enforcing module isolation.
- Add migration infrastructure (no business tables yet).

## Boundary

```text
Payments.Domain
    |
    v
Payments.Application
    |
    v
Payments.Infrastructure
```

Not allowed:

```text
Payments.Domain
    --> Stripe
    --> EF Core
    --> RabbitMQ
```

## Verification

- Module builds.
- DI resolves.
- `PaymentsDbContext` initializes.
- Architecture tests confirm isolation.
- No business behavior exists yet.

---

# 8. 9.3 — Payment Domain Model & Lifecycle

## Objective

Design the Payment aggregate as a representation of business state — not a wrapper around Stripe.

## Proposed Model

The exact model must be finalized during implementation based on actual invariants. Conceptually:

```text
Payment
├── Id
├── OrderId
├── Amount
├── Currency
├── Status
├── Provider
├── ProviderPaymentIntentId
├── CreatedAtUtc
├── UpdatedAtUtc
└── ConcurrencyToken / Version
```

Optionally, a supporting entity for individual attempts:

```text
PaymentAttempt
├── Id
├── PaymentId
├── ProviderReference
├── Status
├── Amount
├── CreatedAtUtc
└── Failure information
```

## Lifecycle

A state machine must be defined explicitly instead of allowing free status mutation. Conceptually:

```text
Pending
   |
   +--> Processing
   |        |
   |        +--> Succeeded
   |        +--> Failed
   |
   +--> Cancelled
```

The internal Payment state machine is distinct from Stripe's own PaymentIntent status values (`requires_payment_method`, `requires_action`, `processing`, `succeeded`, `canceled`, ...). Stripe status must never be assigned directly to `Payment.Status`; mapping is explicit and one-directional, decided in 9.10.

## Tasks

- Define the Payment aggregate, identity, and invariants.
- Define domain events (e.g. `PaymentSucceeded`, `PaymentFailed`).
- Define domain errors for invalid operations.
- Define valid/invalid transitions and terminal states.
- Ensure Stripe concepts do not leak into the Domain layer.

## Verification

- Aggregate and invariants are unambiguous.
- Invalid transitions are rejected by the domain model.
- Domain unit tests cover valid and invalid transitions.

---

# 9. 9.4 — Payment Persistence & Concurrency

## Objective

Persist the Payment domain model while preserving module ownership and concurrency safety.

## Database

```text
PaymentsDbContext
        |
        ├── Payments
        ├── PaymentAttempts / Transactions
        └── ProcessedWebhookEvents
```

Exact table shape depends on the 9.3 design.

## Constraints

- Unique constraint on `ProviderPaymentIntentId` (or equivalent business identity).
- Additional uniqueness constraints as required by 9.3.

## Concurrency

Concurrent duplicate webhook/event processing must not rely on:

```text
SELECT
   |
   v
if not exists
   |
   v
INSERT
```

without database-level protection. Combine unique constraints, transaction boundaries, and an explicit concurrency strategy (e.g. optimistic concurrency token).

## Tasks

- Add EF Core configuration for all Payments entities.
- Add `InitialPayments` migration.
- Add required indexes.
- Add concurrency tokens where mutation races are possible.
- Apply migration and verify schema.
- Verify no pending model changes.

## Verification

- Mapping tests.
- Persistence integration tests against real SQL Server.
- Unique constraint tests.
- Concurrency tests (concurrent writes do not corrupt state).

---

# 10. 9.5 — Stripe Integration Boundary

## Objective

Isolate the Stripe SDK from the rest of the system behind an explicit abstraction.

```text
Application
     |
     v
IStripePaymentGateway
     |
     v
StripePaymentGateway
     |
     v
Stripe SDK
```

Not:

```text
Application
     |
     v
Stripe SDK
```

Nor:

```text
Domain
     |
     v
Stripe SDK
```

## Responsibilities

The abstraction covers only what Phase 9 needs, conceptually:

```text
Create Payment Intent
Retrieve Payment Intent
```

Do not build a generic `IStripeService` with dozens of unused operations.

## Configuration

```text
Stripe
├── SecretKey
├── WebhookSecret
└── ...
```

Externalized; no secrets in source control, consistent with the project's existing secret-handling rules.

## Tasks

- Define `IStripePaymentGateway` in `Payments.Application`.
- Implement `StripePaymentGateway` in `Payments.Infrastructure`.
- Bind Stripe configuration via options pattern, from environment/secret store.
- Register the gateway in DI.

## Verification

- Architecture tests confirm the Stripe SDK is referenced only from `Payments.Infrastructure`.
- Application/Domain remain testable without Stripe.
- Configuration loads correctly with no hardcoded secrets.

---

# 11. 9.6 — Payment Intent Creation

## Objective

Implement Payment Intent creation end-to-end.

```text
Payment Request
      |
      v
Validate
      |
      v
Load / Validate Payment
      |
      v
Create Stripe PaymentIntent
      |
      v
Persist Stripe Reference
      |
      v
Return Payment Information
```

## Critical Validation

- The payment amount must never be trusted from the client. It must come from the authoritative business state (Order/Payment snapshot) inside the system.
- Currency must be consistent with the Payment/Order commercial snapshot.

## Security

Do not log:

- Stripe secret key.
- Card number / CVC.
- Payment credentials.
- Sensitive raw provider payloads.

## Tasks

- Implement the application command/handler for Payment Intent creation.
- Enforce amount/currency integrity checks.
- Persist `ProviderPaymentIntentId` on success.
- Handle and translate Stripe API errors into domain-safe results.

## Verification

- Unit tests for amount/currency invariants.
- Integration test creating a real (test-mode) Stripe Payment Intent, isolated from the main test suite if network-dependent.

---

# 12. 9.7 — Payment Intent Idempotency

`PROJECT_CONTEXT` defines Payment Intent idempotency as an explicit requirement; it is treated as its own sub-phase.

## Objective

Prevent:

```text
Retry
   |
   v
Create PaymentIntent #1

Retry
   |
   v
Create PaymentIntent #2
```

## Required Behavior

For the same business operation (same Order + same Payment Intent operation), the operation must be idempotent. The system must distinguish deterministically between:

```text
Same Idempotency Key + Same Request
```

versus:

```text
Same Idempotency Key + Different Request
```

## Two Layers

```text
Application Idempotency
        +
Stripe API Idempotency
```

Relying on Stripe's idempotency key alone is not sufficient; application-level protection (e.g. unique constraint on Payment/Order + operation) is also required.

## Concurrency Scenario

```text
Request A --+
            +--> Create Payment
Request B --+
```

Concurrent requests for the same Payment must not result in a duplicate business Payment or duplicate Stripe PaymentIntent.

## Tasks

- Generate and pass a deterministic Stripe idempotency key per operation.
- Add application-level idempotency guard (unique constraint / concurrency-safe check).
- Handle idempotency-key conflicts (same key, different payload) explicitly.

## Verification

- Duplicate request test → single PaymentIntent.
- Concurrent request test → single PaymentIntent.
- Conflicting-payload-with-same-key test → explicit rejection.

---

# 13. 9.8 — Stripe Webhook Endpoint & Security

## Objective

Add a dedicated endpoint to receive Stripe webhooks.

```text
POST /api/payments/webhook
```

This endpoint differs from other endpoints:

- No customer JWT authorization.
- Relies on Stripe signature verification instead.
- Requires the raw request body.
- Treats the payload as untrusted until verified.

## Flow

```text
HTTP Request
     |
     v
Raw Body
     |
     v
Stripe Signature Verification
     |
     +--> Invalid --> Reject
     |
     +--> Valid
            |
            v
       Parse Event
            |
            v
       Process Event
```

## Signature

Verify the `Stripe-Signature` header using the configured webhook secret. Invalid signature is an explicit failure scenario that must be tested.

Never trust the event payload alone as proof of Stripe origin.

## Tasks

- Add the webhook endpoint with raw-body binding (bypassing standard model binding/JSON formatting where needed).
- Implement signature verification using the Stripe SDK.
- Reject invalid signatures with an appropriate status code, without leaking internal detail.
- Ensure no customer authorization middleware blocks the endpoint incorrectly, while ensuring no other route is unintentionally exposed the same way.

## Verification

- Valid signature → accepted.
- Invalid signature → rejected.
- Tampered payload → rejected.
- Missing signature header → rejected.

---

# 14. 9.9 — Webhook Idempotent Processing

`PROJECT_CONTEXT` explicitly requires idempotent processing of Stripe webhooks. This is one of the most critical sub-phases of Phase 9.

## Objective

Ensure:

```text
Webhook X
Webhook X
Webhook X
```

produces:

```text
One Business Effect
```

## Identity

Use the Stripe event ID as the primary identity for deduplication. Do not assume `PaymentIntentId` alone is sufficient identity for every event type.

## Persistence

A database-level guard must prevent the same Stripe event, in the same processing context, from applying its business effect more than once (e.g. unique constraint on `StripeEventId`).

## Atomicity

Where a webhook causes both a Payment state change and a processed-webhook record, within the same `PaymentsDbContext`, both must be committed atomically:

```text
Transaction
   |
   ├── Update Payment
   |
   └── Record processed Webhook Event
          |
          v
        Commit
```

Only then return a successful response to Stripe.

## Tasks

- Add `ProcessedWebhookEvents` (or equivalent) persistence with a unique constraint on `StripeEventId`.
- Wrap Payment update + webhook record in a single local transaction.
- Handle unique-constraint violations as "already processed" rather than as errors.

## Crash Scenario

```text
Payment Updated
   |
   v
Crash
   |
   v
Webhook Redelivered
```

Expected: the business effect is not reapplied.

## Verification

- Duplicate webhook test → single effect.
- Concurrent identical webhook test → single effect.
- Crash-before-ack / redelivery test → no duplicate effect.

---

# 15. 9.10 — Stripe Event → Payment State

## Objective

Define the supported Stripe event types and their explicit mapping to Payment domain transitions.

```text
Stripe Event
     |
     v
Event Mapping
     |
     v
Payment Domain Command
     |
     v
Payment Transition
```

Not:

```text
Stripe Event
     |
     v
Payment.Status = Stripe.Status
```

## Must Be Defined

- Supported Stripe events (explicit allow-list).
- Unsupported/unknown events — handled safely (ignored/logged, not errored).
- Duplicate events — handled via 9.9.
- Out-of-order events — must not regress a terminal state.
- Invalid transitions — rejected by the domain model from 9.3.

## Example

If Payment has already reached `Succeeded`, a late-arriving event indicating an earlier state (e.g. `processing`) must not revert it. External event streams do not guarantee ordering.

## Tasks

- Implement an explicit event-type-to-domain-command mapper.
- Reject/ignore unknown event types safely.
- Enforce terminal-state protection using the domain state machine.
- Log unsupported and out-of-order events for observability without failing the webhook response unnecessarily.

## Verification

- Each supported event type maps correctly.
- Unknown event type does not crash the endpoint.
- Out-of-order event does not corrupt terminal state.

---

# 16. 9.11 — Payment Integration Events & Saga Integration

This sub-phase connects Phase 9 to the infrastructure built in Phase 8.

## Objective

Publish Payment outcomes through the existing reliable messaging pipeline:

```text
Domain Event
      |
      v
Integration Event Mapping
      |
      v
Outbox
      |
      v
RabbitMQ
```

Conceptually:

```text
PaymentSucceededIntegrationEvent
PaymentFailedIntegrationEvent
```

Exact contract names/payloads are finalized during implementation.

## Ownership

Payment integration event contracts live in:

```text
Payments.Contracts
```

not `BuildingBlocks`, except shared metadata already mandated by the project — consistent with the Phase 6 decision to place integration event contracts inside each module's `Contracts` project.

## Critical Boundary

Forbidden:

```text
PaymentSucceeded
    |
    v
OrdersDbContext
```

Required:

```text
PaymentSucceeded
    |
    v
Payments Outbox
    |
    v
RabbitMQ
    |
    v
Orders / Saga
```

This prepares Phase 10 without implementing it.

## Phase 9 Boundary

In this sub-phase, a `PaymentSucceeded` (and `PaymentFailed`) event **can be published**. What must NOT be implemented yet:

```text
Payment Failed
   |
   v
Release Inventory
   |
   v
Cancel Order
```

That is Payment Saga / Compensation — Phase 10.

## Tasks

- Define integration event contracts in `Payments.Contracts`.
- Map domain events to integration events.
- Publish via the existing Outbox pattern (Phase 6 infrastructure — no new messaging framework).
- Ensure CorrelationId/CausationId propagate from the webhook processing context.

## Verification

- Outbox message is created transactionally with the Payment state change.
- Message is published to RabbitMQ and observable/consumable (even if no consumer acts on it yet, or only logs it).
- No new messaging infrastructure is introduced.

---

# 17. 9.12 — Testing, Observability & Production Hardening

This is not a cosmetic sub-phase — Payments is a high-sensitivity external integration.

## Unit Tests

### Domain

- Payment creation.
- Valid transitions.
- Invalid transitions.
- Amount invariants.
- Currency invariants.
- Terminal states.
- Duplicate state operations.

### Application

- Create Payment.
- Payment Intent creation.
- Idempotent replay.
- Idempotency conflict.
- Provider failures.
- Unsupported events.

## Integration Tests

Real SQL Server for persistence/concurrency tests.

For Stripe, separate:

```text
Application/Domain Integration Tests
```

from:

```text
External Stripe Contract/Integration Tests
```

so the main suite does not depend on Stripe network availability.

## Mandatory Payment Scenarios

At minimum:

1. Normal payment (Create → Intent → Webhook → Succeeded).
2. Payment failure (Intent → Webhook Failed → Failed).
3. Duplicate webhook → one effect.
4. Concurrent webhook → no double transition.
5. Invalid signature → rejected.
6. Duplicate Payment Intent request → one Payment Intent.
7. Concurrent Payment Intent requests → one Payment Intent.
8. Out-of-order webhook → terminal state preserved.
9. Unknown/unsupported event → handled safely.
10. End-to-end success workflow.
11. End-to-end failure workflow.

These align with the failure scenarios explicitly required by `PROJECT_CONTEXT`.

## Observability

Preserve the trace chain established in Phase 8:

```text
Checkout
   |
   v
OrderCreated
   |
   v
Saga
   |
   v
Payment Request
   |
   v
Payments
   |
   v
Stripe
   |
   v
Webhook
   |
   v
Payment Event
```

Traceable data:

```text
OrderId
PaymentId
StripePaymentIntentId
StripeEventId
CorrelationId
CausationId
EventId
EventType
TraceId
```

No sensitive payment data logged.

## Security Hardening

- Stripe `SecretKey` / `WebhookSecret` externalized, never in source control.
- Webhook signature validation, raw body verification, replay/idempotency protection.
- Customer APIs use `[Authorize]`; webhook uses Stripe signature, not customer JWT.
- Payment ownership enforced (a customer cannot access another customer's Payment — 403/404 per project convention).
- No secrets, card details, or sensitive raw provider payloads in logs.

## Tasks

- Write/complete all listed unit and integration tests.
- Add structured logging and Activity instrumentation for the Payment flow.
- Run and verify all mandatory scenarios against real infrastructure.

## Verification

- All listed scenarios pass.
- Sensitive data is verifiably absent from logs (manual/automated check).
- End-to-end trace is reconstructable from logs/telemetry.

---

# 18. 9.13 — Final Verification & Phase Completion

## Build

```bash
dotnet build FluxCommerce.slnx
```

Expected:

```text
0 warnings
0 errors
```

## Architecture Tests

Verify:

```text
Payments.Domain
    X--> Stripe
    X--> EF Core
    X--> RabbitMQ

Payments.Application
    X--> Stripe SDK
    X--> RabbitMQ.Client

Payments.Contracts
    X--> EF Core
    X--> Stripe SDK
    X--> RabbitMQ.Client
    X--> Payments.Domain
```

And:

```text
Payments
    X--> OrdersDbContext
    X--> InventoryDbContext
```

## Database Verification

- Migration applied.
- Schema verified.
- No pending model changes.
- Constraints verified.
- Indexes verified.
- Concurrency protection verified.

## Integration Verification

Prove the full chain:

```text
Payment Creation
        |
        v
Stripe PaymentIntent
        |
        v
Webhook
        |
        v
Payment State
        |
        v
Outbox
        |
        v
RabbitMQ
```

## Documentation

Update:

- `ARCHITECTURE.md`, only where architecture actually changed.
- `PROJECT_CONTEXT.md`, synchronized.
- Relevant ADRs, only for durable architectural decisions.
- `PHASE_9_COMPLETION_REPORT.md`, created.
- Known limitations documented.
- Phase 10 explicitly identified as next phase.

Do not create unrelated documentation.

---

# 19. Testing Strategy

Phase 9 testing should be layered.

## Unit Tests

- Payment state machine.
- State transitions (valid/invalid).
- Amount/currency invariants.
- Idempotency key handling.
- Stripe event mapping decisions.

## Integration Tests

- SQL persistence.
- Payment persistence and concurrency.
- Outbox.
- RabbitMQ.
- Webhook endpoint (signature valid/invalid).
- Webhook idempotency.
- Payment Intent creation and idempotency.

## End-to-End Tests

```text
Payment Request
  ↓
Stripe PaymentIntent
  ↓
Webhook
  ↓
Payment State
  ↓
Outbox
  ↓
RabbitMQ
```

Critical end-to-end tests must use real infrastructure already established by previous phases (SQL Server, RabbitMQ), and, for Stripe, test-mode API calls isolated from the default fast test suite.

---

# 20. Definition of Done

Phase 9 is complete only when all of the following are true:

### Payments Module

1. Payments module exists with all four layers.
2. `PaymentsDbContext` is independent.
3. Payment aggregate/invariants are complete.
4. Payment lifecycle is explicitly defined.
5. Persistence works, including concurrency protection.

### Stripe

6. Stripe SDK is isolated in `Payments.Infrastructure`.
7. Stripe configuration is externalized (no secrets in source control).
8. Payment Intent creation works.
9. Amount/currency integrity is verified against authoritative state.
10. Stripe reference (`ProviderPaymentIntentId`) is persisted.

### Idempotency

11. Payment Intent idempotency is implemented (application + Stripe layers).
12. Duplicate Payment Intent request is safe.
13. Concurrent Payment Intent request is safe.
14. Webhook idempotency is implemented.
15. Duplicate webhook is safe.
16. Concurrent webhook is safe.

### Webhooks

17. Dedicated webhook endpoint exists.
18. Raw payload is available for signature verification.
19. Stripe signature is validated; invalid signature is rejected.
20. Supported events are explicitly defined; unknown events are handled safely.
21. Out-of-order/invalid transitions are handled without corrupting state.

### Messaging

22. Payment integration events are defined in `Payments.Contracts`.
23. Domain → Integration event mapping is explicit.
24. Outbox is used for publication.
25. RabbitMQ publication reuses existing Phase 6 infrastructure.
26. No new messaging framework is introduced.

### Architecture

27. No cross-module `DbContext` access.
28. No distributed transaction.
29. Domain is independent of Stripe.
30. Application is independent of the Stripe SDK.
31. Payments owns Payment state; Orders/Saga remains owner of Order/Saga state.

### Security

32. Stripe secrets are externalized.
33. Webhook signature verification is enforced.
34. No sensitive payment data appears in logs.
35. Payment ownership is verified on customer-facing APIs.
36. API authorization is reviewed.

### Observability

37. CorrelationId is propagated.
38. CausationId is propagated.
39. PaymentId, StripePaymentIntentId, and StripeEventId are traceable.
40. Failures are observable via logs/telemetry.

### Testing

41. Domain unit tests pass.
42. Application unit tests pass.
43. Persistence integration tests pass against real SQL Server.
44. Payment Intent idempotency tests pass.
45. Webhook idempotency tests pass.
46. Concurrent duplicate tests pass.
47. Invalid signature tests pass.
48. Provider failure tests pass.
49. Integration event publication tests pass.
50. Real RabbitMQ tests pass where required.

### Architecture Tests / Build

51. `dotnet build FluxCommerce.slnx` completes with 0 warnings, 0 errors.
52. All architecture tests pass.

### Documentation

53. Phase 9 implementation document finalized.
54. `ARCHITECTURE.md` updated only where architecture actually changed.
55. `PROJECT_CONTEXT.md` synchronized.
56. ADRs added/updated only for durable architectural decisions.
57. `PHASE_9_COMPLETION_REPORT.md` created.
58. Known limitations documented.
59. Phase 10 explicitly identified as next phase.

### Scope Discipline

60. No Payment Saga is introduced.
61. No Refund orchestration is introduced.
62. No Inventory/Order compensation triggered by Payment outcome is introduced.
63. No Reservation Expiration functionality is introduced.

---

# 21. Execution Order

The implementation must follow:

```text
9.1  Baseline & Architecture Audit
          |
          v
9.2  Payments Module Foundation
          |
          v
9.3  Payment Domain & Lifecycle
          |
          v
9.4  Persistence & Concurrency
          |
          v
9.5  Stripe Integration Boundary
          |
          v
9.6  Payment Intent Creation
          |
          v
9.7  Payment Intent Idempotency
          |
          v
9.8  Webhook Endpoint & Signature Security
          |
          v
9.9  Webhook Idempotency
          |
          v
9.10 Stripe Event → Payment State
          |
          v
9.11 Payment Events & Saga Integration
          |
          v
9.12 Testing / Observability / Hardening
          |
          v
9.13 Final Verification & Completion
```

Each sub-phase must end with appropriate verification before proceeding.

---

# 22. Final Architectural Principle

The central rule of Phase 9 is:

> **Payments owns Payment state; Stripe is an external, untrusted provider — never the source of truth.**

Therefore:

```text
Orders / Saga
  └── Owns Order + Saga state

Payments
  └── Owns Payment + Payment Attempt state

Stripe
  └── External payment provider (untrusted, at-least-once, referenced by ProviderPaymentIntentId)

Outbox
  └── Guarantees reliable publication

RabbitMQ
  └── Transports asynchronous messages

Webhook Endpoint
  └── Verifies signature, processes idempotently
```

The desired result is a reliable, independently-owned Payment capability that the Order/Inventory Saga can consume, without violating Modular Monolith boundaries:

```text
Orders / Saga
  |
  | Payment Request
  v
Payments
  |
  | Stripe PaymentIntent / Webhook
  v
Payment State
  |
  +--> PaymentSucceeded (Integration Event)
  |
  +--> PaymentFailed (Integration Event)
```

Phase 9 must establish this capability without prematurely introducing Payment Saga, Refund/Compensation workflows, Reservation Expiration, or distributed deployment complexity. Those belong to Phase 10 and beyond.
