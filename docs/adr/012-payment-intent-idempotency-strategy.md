# ADR-012: Payment Intent Idempotency Strategy

## Status

Accepted

## Context

Phase 9 explicitly requires Payment Intent idempotency as a critical requirement (§5.5, §9.7). The system must prevent duplicate Payment Intents from being created when the same business operation is retried or when concurrent requests are made. The project's `PROJECT_CONTEXT.md` §Idempotency defines four idempotency layers: API, Message, Webhook, and Payment Intent.

Two specific idempotency scenarios must be handled:
1. **Application-level idempotency**: Repeated requests using the same idempotency key must not create duplicate Orders or duplicate Payment Intents.
2. **Stripe API idempotency**: Stripe's own idempotency key mechanism, which is insufficient as the sole protection.

## Decision

### Two-layer idempotency approach

Both application-level and Stripe-level idempotency are required, as relying on Stripe's idempotency key alone is not sufficient (§5.5, §9.7).

#### Layer 1: Application-level idempotency

- A deterministic **idempotency key** is generated per business operation (Order + operation type).
- The key is a `(CustomerId, IdempotencyKey)` pair, consistent with the existing Checkout idempotency pattern (Phase 4.7).
- A **unique constraint** in the Payments database prevents duplicate Payment records for the same key:
  ```sql
  UNIQUE (ProviderPaymentIntentId) -- or equivalent business identity
  ```
- The application service checks for an existing Payment with the same idempotency key before creating a new PaymentIntent.
- If a duplicate key is detected, the existing Payment reference is returned without contacting Stripe.
- If the key is new, the PaymentIntent is created, and the reference is persisted atomically.

#### Layer 2: Stripe API idempotency key

- Each PaymentIntent creation request includes Stripe's `Idempotency-Key` header.
- Stripe ensures that repeated requests with the same idempotency key return the same PaymentIntent object.
- This provides protection at the Stripe API level, but is **not relied upon as the sole mechanism**.

### Concurrency scenario

```
Request A          Request B
  |                  |
  +--> Create Payment  |
  |                  +--> Create Payment
  |                      |
  |                      +--> Duplicate key detected → return existing
  +--> Create PaymentIntent #1
                   |
                   v
            PaymentIntent created in Stripe
```

- If both requests arrive concurrently and both are new keys, Stripe creates two PaymentIntents (which is acceptable at the API level, but the application must prevent this).
- The application-level unique constraint ensures only one Payment record exists in the system.
- If Request A creates first and Request B arrives, Request B detects the existing Payment and returns the reference.

### Conflict handling

- **Same idempotency key + Same request**: Idempotent → return existing Payment.
- **Same idempotency key + Different request**: Explicit rejection → return conflict error (409), do not create a second Payment.
- The application ensures that the same business operation always uses the same idempotency key, even on retry.

## Consequences

Positive:
- Duplicate Payment Intents are prevented at the application layer, independent of Stripe's behavior.
- Concurrency is handled safely via database unique constraints and transactional checks.
- Consistent with the existing Checkout idempotency pattern (Phase 4.7), using the same `(UserId, IdempotencyKey)` concept.
- Clear separation: application guard + Stripe API key as defense-in-depth.

Negative:
- Requires a unique constraint on `ProviderPaymentIntentId` (or a dedicated `IdempotencyKey` column) in the PaymentsDbContext.
- Application service must perform a read-before-write check within a transaction, adding a small latency.
- The two-layer approach adds complexity; however, it is explicitly required by the project specification.

## References

- Phase 9 Implementation Plan §5.5 (Idempotency)
- Phase 9 Implementation Plan §9.7 (Payment Intent Idempotency)
- Phase 9 Implementation Plan §9.2 (Payments Module Foundation) — idempotency key generation
- Phase 4 Implementation Plan §4.7 (Checkout idempotency — established pattern)
- PROJECT_CONTEXT.md §Idempotency (four idempotency layers)
- ADR-008: Consumer Contract and Idempotency (idempotency boundary pattern)