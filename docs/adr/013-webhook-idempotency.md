# ADR-013: Stripe Webhook Idempotency

## Status

Accepted

## Context

Phase 9 explicitly requires idempotent processing of Stripe webhooks (§5.4, §9.9). Stripe may redeliver webhooks multiple times (at-least-once delivery semantics), and the system must ensure that each webhook produces exactly one business effect. The webhook endpoint receives the raw Stripe event, verifies the signature, and processes the event — but must tolerate duplicate deliveries without causing duplicate state changes.

## Decision

### Identity for deduplication

- The **Stripe Event ID** (`event.id`, e.g., `evt_123`) is the primary identity for deduplication.
- The system does not assume `PaymentIntentId` alone is sufficient identity for every event type; different event types for the same PaymentIntent may have different Event IDs.
- A **unique constraint** on `StripeEventId` in the `ProcessedWebhookEvents` table prevents the same event from being processed twice:

```sql
CREATE UNIQUE INDEX IX_ProcessedWebhookEvents_StripeEventId ON ProcessedWebhookEvents(StripeEventId);
```

### Atomic processing within PaymentsDbContext

- Webhook processing wraps the Payment state change and the `ProcessedWebhookEvents` record in a single local transaction:
  ```text
  Transaction
     ├── Update Payment state
     └── Record processed Webhook Event
           |
           v
        Commit
  ```
- Only after both the Payment update and the webhook record are committed atomically does the endpoint return a successful response to Stripe.
- If the transaction fails (e.g., DbUpdateConcurrencyException), the webhook is NOT considered processed, and Stripe will redeliver it.

### Crash scenario (before acknowledgment)

```
Payment Updated       Crash
     |                  |
     v                  v
Webhook Redelivered  →  Payment state already updated
     |
     v
Unique constraint violation → "already processed" → skip business effect → ACK
```

- If the Payment is updated but the process crashes before the `ProcessedWebhookEvents` record is committed, the redelivered webhook encounters the unique constraint violation.
- The violation is caught and treated as "already processed" — the business effect is NOT reapplied.
- The endpoint returns a successful response to Stripe, and the message is ACKed (not requeued).

### Unsupported / unknown event types

- Webhook events that are not in the supported allow-list are handled safely:
  - The event is logged (structured, no sensitive payload data).
  - The webhook returns a successful response to Stripe (200).
  - The event is NOT processed — no business state change occurs.
- This prevents unknown event types from corrupting terminal states.

### Out-of-order events

- Webhook events may arrive out of order (e.g., `payment.succeeded` before `payment.intent.created`).
- The domain state machine enforces terminal-state protection: if Payment has already reached `Succeeded`, a late-arriving event indicating an earlier state must not revert it.
- Events are processed in the order they are received; if an event would regress a terminal state, it is logged and skipped without error.

## Consequences

Positive:
- Duplicate webhooks produce exactly one business effect, guaranteed by the unique constraint.
- Crash-before-ack scenarios are safe: the unique constraint on redelivery prevents duplicate effects.
- The webhook endpoint can return 200 to Stripe after atomic commit, satisfying Stripe's requirements.
- Unknown/unsupported events are handled safely without crashing the endpoint.
- Consistent with the project's at-least-once delivery model (Phase 6/7) and idempotent consumer patterns (Phase 7/ADR-008).

Negative:
- Requires a `ProcessedWebhookEvents` table with a unique constraint on `StripeEventId` in PaymentsDbContext.
- The webhook endpoint must always return 200 within the transaction; if the transaction fails, Stripe will redeliver, which is safe but may cause retries.
- Terminal-state protection must be explicitly enforced in the domain model; out-of-order events must be handled gracefully.

## References

- Phase 9 Implementation Plan §5.4 (At-Least-Once Delivery)
- Phase 9 Implementation Plan §9.9 (Webhook Idempotent Processing)
- Phase 9 Implementation Plan §9.10 (Stripe Event → Payment State)
- PROJECT_CONTEXT.md §Idempotency (Webhook Idempotency)
- ADR-008: Consumer Contract and Idempotency (idempotency boundary — ProcessedMessage pattern)
- ADR-012: Payment Intent Idempotency Strategy (application-level idempotency complement)