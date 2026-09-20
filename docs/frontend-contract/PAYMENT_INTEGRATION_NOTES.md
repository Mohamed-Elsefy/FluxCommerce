# FluxCommerce — Payment Integration Notes for Frontend

> Which part of the Stripe flow the SPA handles directly (short answer: card confirmation only, via the client-secret endpoint). Source: `Payments.Infrastructure.Consuming.CreatePaymentRequestedConsumer` (saga event → `PaymentIntentResult`), `Payments.Application.Models` (`CreatePaymentIntentRequest`, `PaymentIntentResult{StripePaymentIntentId,Status,ClientSecret}`), `Payments.Infrastructure.Webhook.StripeWebhookService` (tolerance 300s), `PaymentsController` (`GET /api/payments/{id}`, `GET /api/payments/by-order/{orderId}`, `POST /api/payments/webhook`), `StripeOptions{SecretKey,WebhookSecret,PublishableKey}`, saga `WaitingForPayment → Completed/ReleasingInventory`.

---

## 1. Responsibility split

| Step | Owner | SPA action |
|---|---|---|
| Create Stripe PaymentIntent (`intent_{OrderId}_{PaymentId}_{Version}`, unique `OrderId`/`ProviderPaymentIntentId`) | Backend saga → `CreatePaymentRequestedConsumer` (duplicate `OrderId` → existing intent acknowledged, no second Stripe call) | None — no `POST /api/payments/intent` HTTP endpoint; SPA fetches the secret via `GET /api/payments/by-order/{orderId}` (§3) |
| Store `Payment{OrderId,CustomerId,Amount,Currency,ProviderPaymentIntentId,Provider="Stripe",Status,Version}` | Backend only | None |
| Stripe webhook `payment_intent.*` → verify (`Stripe-Signature`, tolerance 300s) → idempotent apply (`Processed`/`AlreadyProcessed` → `200 empty`) → outbox `payments.payment_succeeded/failed` → saga `Completed` / `ReleasingInventory→Cancelled` | Stripe → API (`POST /api/payments/webhook`, `AllowAnonymous`) | Never call; never replicate |
| 3-D Secure / card confirmation (`requires_action`) | **SPA via Stripe.js** — the only direct Stripe touch | `confirmCardPayment(clientSecret)` once backend provides it (see §3 blocker) |
| Outcome display | SPA via FluxCommerce API (not Stripe API) | Poll `GET /orders/{id}` (`Pending→Confirmed/Cancelled`); optionally `GET /api/payments/{id}` if `paymentId` known |

Rules: never ship `SecretKey`/`WebhookSecret` to the browser (server-only; both `""` in dev config until user-secrets/env). `PublishableKey` (`""` in dev) is the only Stripe key the SPA may hold — inject via app env, not via API. Never trust client-side amounts — pricing is Catalog-authoritative; the intent amount is the order `GrandTotal` staged by the saga, so the Stripe charge always equals the real order total.

## 2. What the SPA does today (no client-secret endpoint yet)

```ts
// After POST /orders/checkout (Pending) → poll order as payment proxy:
const terminal = new Set(["Confirmed", "Cancelled"]);
async function awaitOrder(orderId: string, accessToken: string) {
  for (;;) {
    const r = await fetch(`/orders/${orderId}`, { headers: { Authorization: `Bearer ${accessToken}` } });
    const { data } = await r.json() as ApiResponse<{ status: string }>;
    if (terminal.has(data.status)) return data.status; // Confirmed = paid · Cancelled = failed/expired/compensated
    await new Promise(r => setTimeout(r, 2000));
  }
}
```

`GET /api/payments/{paymentId}` returns `ApiResponse<{id,orderId,customerId,amount,currency,status,providerPaymentIntentId,provider,createdAtUtc,updatedAtUtc,version}>` with ownership-masked `404`, but `paymentId` is not returned by checkout and `OrderDetails` carries no payment fields — so payment polling is only possible when the id is known out-of-band. Webhook timing is async (seconds); keep the order-pending UI with cancel option (`POST /orders/{id}/cancel` while `Pending`).

## 3. Client-secret endpoint (landed)

`GET /api/payments/by-order/{orderId}` (owner JWT) → `200 ApiResponse<PaymentInitializationResponse{PaymentId,OrderId,ClientSecret,PaymentReference,Status}>`.

Behavior matrix: anonymous → `401`; empty guid → `400 Payment.OrderIdRequired`; missing payment or stranger → masked `404 Payment.NotFound`; intent not created yet (saga lag) → `409 Payment.NotReady` (poll/retry); terminal payment → `409 Payment.NotPayable`; Stripe unreachable → `500 Payment.ProviderUnavailable`. The secret is re-fetched live from Stripe per call, so retries reuse the single intent per order — no `Version+1`, no second intent. `ClientSecret` is runtime-only: never store it.

## 4. Future Stripe.js flow (when the endpoint lands)

```ts
import { loadStripe } from "@stripe/stripe-js"; // publishable key from SPA env, NOT from API
const stripe = await loadStripe(import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY);
// 1. checkout → 2. fetch clientSecret for my order → 3. confirm → 4. poll order:
const { error } = await stripe!.confirmCardPayment(clientSecret, { payment_method: { card: cardElement } });
if (error) { showDecline(error.message); /* order stays Pending; webhook Fail → Cancelled */ }
else await awaitOrder(orderId, accessToken); // Succeeded → Confirmed · Failed → Cancelled (+ inventory released) · Refunded (post-pay compensation)
```

Handle `requires_action` inside `confirmCardPayment` (Stripe.js manages the 3-DS redirect/modal). Idempotency is server-side (`intent_{OrderId}_{PaymentId}_{Version}`, `ProcessedWebhookEvents.StripeEventId` unique, duplicate webhook → `200`): safe to retry confirmation after network failure, never create a second order for retry (reuse `Idempotency-Key`).

## 5. MVP stub warning + test checklist

- Intent amount is the authoritative order `GrandTotal` (checkout reprices from Catalog; saga stages it on `create_payment_requested`) — multi-currency works if the catalog/order currency is non-USD; still verify amounts in Stripe dashboard test mode before demoing.
- Webhook local testing needs Stripe CLI forwarding with the configured `WebhookSecret` (tolerance 300s, `throwOnApiVersionMismatch: false`); duplicate deliveries must return `200` (check `AlreadyProcessed` log).
- Status mapping for UI: `Payment Pending/Processing` → spinner; `Succeeded` → success; `Failed/Cancelled` → error + `GET /orders/{id}` reason; `Refunded` → post-pay refund notice.
