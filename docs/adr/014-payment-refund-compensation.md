# ADR-014: Payment Refund Compensation (Phase 10.8)

**Status:** Accepted
**Date:** 2026-08-27
**Context:** Phase 10.2-10.7
**Prerequisites:** Phase 9 Stripe Payments, Phase 10.7 Order Cancellation

## Context

Post-payment downstream failures (e.g., order confirmation or fulfillment failure after `PaymentSucceeded`) require a compensating refund that must be exactly-once at the business level despite at-least-once delivery, Stripe transient failures, and consumer crashes. Phase 9’s `IStripePaymentGateway` only handled `CreatePaymentIntent`; refund was not modeled. The OrderSaga must orchestrate refund → inventory release → order cancellation without leaking Stripe concerns or allowing duplicate Stripe refund effects.

## Decision

### 1. Payment Domain
- Extend `PaymentStatus` with `Refunded = 5` and add `ProviderRefundId` (256) to `Payment` aggregate.
- Add `PaymentAlreadyRefunded` error and `PaymentRefundedDomainEvent`.
- `Payment.Refund(refundId)` enforces invariant: only `Succeeded → Refunded`, otherwise `InvalidStateTransition` or `AlreadyRefunded`; idempotent second call throws `AlreadyRefunded` which handlers treat as success.
- EF `PaymentConfiguration` maps `ProviderRefundId` nullable, `Status` via `_status` field, same concurrency token.

### 2. Contracts (Payments.Contracts, owner = Payments)
- `RefundPaymentRequestedIntegrationEvent(OrderId, PaymentId, ProviderPaymentIntentId, Amount, Currency, Reason)` – `payments.refund_payment_requested` – published by Orders saga when downstream post-payment failure requires compensation.
- `PaymentRefundedIntegrationEvent(OrderId, PaymentId, ProviderPaymentIntentId, ProviderRefundId, Amount, Currency, RefundedAtUtc)` – `payments.payment_refunded` – published by Payments after successful Stripe refund.
- `PaymentRefundFailedIntegrationEvent` – reserved for permanent refund failure observability (not yet used for compensation).

All derive from `IntegrationEvent` (correlation `OrderId`, causation `payment event id`, `OccurredAtUtc`, `Version=V1`, W3C `traceparent` via outbox).

### 3. Gateway Isolation (Stripe)
- Extend `IStripePaymentGateway` with `CreateRefundAsync(CreateRefundRequest) → RefundResult(StripeRefundId, Status)` where `CreateRefundRequest(OrderId, PaymentId, ProviderPaymentIntentId, Amount, Currency)`.
- `StripePaymentGateway.CreateRefundAsync` uses `RefundService` with `RefundCreateOptions{PaymentIntent, Amount*100, Metadata{order_id,payment_id}}` and deterministic idempotency key `refund_{PaymentId}_{OrderId}` via `RequestOptions{IdempotencyKey}`. This guarantees **ONE** Stripe refund business effect per payment compensation despite retries; Stripe’s own idempotency + local `Status==Refunded` check make duplicate deliveries safe.
- `GenerateRefundIdempotencyKey(paymentId, orderId)` mirrors `GenerateIdempotencyKey` for intents.
- Only `Payments.Infrastructure` references `Stripe.net` (`RefundService`); `Domain/Application/Contracts` have zero `Stripe.net` references (verified by architecture tests).

### 4. Payments Consumer & Outbox
- `RefundPaymentRequestedConsumer` (`StableHandlerName="Payments.RefundPayment"`) in `Payments.Infrastructure/Consuming`, runs inside `PaymentsIdempotentProcessor` (`ProcessedMessage` `Key=EventId, Handler` PK, transaction via `CreateExecutionStrategy`).
- Handler: validate, load `Payment` by `PaymentId` or `OrderId`, if `Refunded` → idempotent success (no Stripe call); if not `Succeeded` → throw (retry/DLQ); else call `CreateRefundAsync`, handle `already refunded` Stripe message as success, `transient` (timeout/5xx) → throw for retry, `permanent` → throw to DLQ without corrupting local `Status`; on success `payment.Refund(refundId)` + stage `PaymentRefunded` via `IntegrationEventOutboxMessageFactory` + `PaymentsDbContext.OutboxMessages.Add` + single `SaveChangesAsync` (atomic with `ProcessedMessage` inside processor transaction). `PaymentsOutboxDispatcher` deserializes `PaymentRefunded`/`PaymentRefundFailed` and publishes via `IRabbitMQPublisher` with confirm.

### 5. Orders Saga Compensation
- Extend `OrderSagaState` with `Refunding = 7`.
- `RequestRefund(utcNow)`: `Completed → Refunding` (idempotent on `Refunding/ReleasingInventory/Cancelled`), stages `RefundPaymentRequested` via `SagaOutboxMessageFactory` → `Orders Outbox` (atomic with `Refunding` transition). Duplicate request on `Refunding` is no-op (no second refund). Preserves `CorrelationId = saga.CorrelationId` (OrderId) and `CausationId = triggering event id`, `OccurredAtUtc = UtcNow`, W3C trace via `IntegrationEventOutboxMessageFactory`.
- `HandlePaymentRefunded(utcNow)`: `Refunding → ReleasingInventory` (idempotent on `ReleasingInventory/Cancelled/Completed`), stages `InventoryReleaseRequested` per `ReservationIds` (same as `FailPayment` path). `OrdersOutboxDispatcher` publishes `payments.refund_payment_requested` (new case).
- `PaymentRefundedConsumer` (`StableHandlerName="Orders.ApplyPaymentRefunded"`) in `Orders.Infrastructure`, validates and delegates to `ApplyPaymentRefundedAsync` which transitions `Refunding→ReleasingInventory` and stages releases; duplicate on `ReleasingInventory/Cancelled/Completed` is idempotent.
- `ApplyInventoryReleased` now uses `Order.CancelForRefund` (new) to allow `Confirmed→Cancelled` (post-payment) as well as `Pending→Cancelled`, idempotent on already `Cancelled`, then `FinalizeCancellation` `Cancelled→Completed`. All three (`MarkInventoryReleased` + `CancelForRefund` + `FinalizeCancellation`) commit atomically.

### 6. Order Aggregate
- Keep `Cancel` as strict `Pending → Cancelled` for API idempotency (existing tests expect failure on `Confirmed`/`Cancelled` via API).
- Add `CancelForRefund(utcNow)`: `Cancelled→Success` idempotent, `Pending/Confirmed→Cancelled` (adds `OrderCancelled` domain event), else `InvalidStateTransition`. Used by saga compensation for post-payment.

## Consequences

- **Positive:** Exactly-once Stripe refund per payment (`refund_{PaymentId}_{OrderId}` + local `Refunded` check + `ProcessedMessage` PK); at-least-once safe; no `Orders → Stripe` direct call; W3C trace preserved across `Orders → Payments → Orders → Inventory` hops; reuse of existing `ReleasingInventory → Cancelled → Completed` inventory compensation.
- **Negative:** Adds `Refunding` state (int 7) – requires EF state `HasConversion<int>` (no migration needed, int column). Two `Completed` visits for refund flow (first after payment, second after compensation) – idempotency relies on `Refunding` intermediate.
- **Alternatives considered:** Reusing `Completed → ReleasingInventory` directly without `Refunding` – rejected because it would conflate payment-failed and post-payment refund, losing idempotency distinction for refund request vs refund success. Using `Order.Cancel` for refund – rejected to keep API semantics strict; added `CancelForRefund`.

## Compliance

- `Orders.Infrastructure` references only `Payments.Contracts` (`IntegrationEvents` namespace) – allowed carve-out (ADR-010), verified `Orders_ShouldNot_ReferencePayments` now allows `Payments.Contracts` for Infrastructure.
- `Payments.Infrastructure` alone references `Stripe.net` (`RefundService`), verified `PaymentsArchitectureTests`.
- No distributed transaction, no cross-`DbContext`, outbox + at-least-once + idempotent consumers throughout.

## References

- Phase 9 `IStripePaymentGateway` (`docs/adr/011-stripe-integration-boundary.md`)
- ADR-009 Saga orchestration, ADR-010 payment ownership boundary
- `docs/IMPLEMENTATION_PLAN.md#phase-10--payment-saga--compensation-` (refund sub-scope)
