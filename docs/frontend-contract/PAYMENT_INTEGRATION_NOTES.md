# FluxCommerce — Payment Integration Notes for Frontend

> Which part of the Stripe flow the SPA handles directly (short answer: card confirmation only — and only once the backend exposes a client-secret endpoint). Source: `Payments.Application.Commands.CreatePayment` (`CreatePaymentIntentCommand(OrderId) → PaymentIntentResult`), `Payments.Application.Models` (`CreatePaymentIntentRequest`, `PaymentIntentResult{StripePaymentIntentId,Status,ClientSecret}`), `Payments.Infrastructure.Webhook.StripeWebhookService` (tolerance 300s), `PaymentsController` (`GET /api/payments/{id}`, `POST /api/payments/webhook`), `StripeOptions{SecretKey,WebhookSecret,PublishableKey}`, saga `WaitingForPayment → Completed/ReleasingInventory`.

---

## 1. Responsibility split

| Step | Owner | SPA action |
|---|---|---|
| Create Stripe PaymentIntent (`intent_{OrderId}_{PaymentId}_{Version}`, unique `OrderId`/`ProviderPaymentIntentId`) | Backend saga/consumer (`CreatePaymentIntentCommand{OrderId}`; duplicate `OrderId` → existing intent, `ClientSecret: null`) | None — no `POST /api/payments/intent` HTTP endpoint exists today |
| Store `Payment{OrderId,CustomerId,Amount,Currency,ProviderPaymentIntentId,Provider="Stripe",Status,Version}` | Backend only | None |
| Stripe webhook `payment_intent.*` → verify (`Stripe-Signature`, tolerance 300s) → idempotent apply (`Processed`/`AlreadyProcessed` → `200 empty`) → outbox `payments.payment_succeeded/failed` → saga `Completed` / `ReleasingInventory→Cancelled` | Stripe → API (`POST /api/payments/webhook`, `AllowAnonymous`) | Never call; never replicate |
| 3-D Secure / card confirmation (`requires_action`) | **SPA via Stripe.js** — the only direct Stripe touch | `confirmCardPayment(clientSecret)` once backend provides it (see §3 blocker) |
| Outcome display | SPA via FluxCommerce API (not Stripe API) | Poll `GET /orders/{id}` (`Pending→Confirmed/Cancelled`); optionally `GET /api/payments/{id}` if `paymentId` known |

Rules: never ship `SecretKey`/`WebhookSecret` to the browser (server-only; both `""` in dev config until user-secrets/env). `PublishableKey` (`""` in dev) is the only Stripe key the SPA may hold — inject via app env, not via API. Never trust client-side amounts — pricing is Catalog-authoritative; the current intent handler even hardcodes a placeholder `19.99 USD` (see §4), so amounts shown at checkout are informational until the totals wiring lands.

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

## 3. Blocker: no HTTP path to `ClientSecret` today

`PaymentIntentResult.StripePaymentIntentId/ClientSecret` is an Application-layer model returned to the saga consumer — no controller action serializes it, and the duplicate-intent path returns `ClientSecret: null` by design (single intent per `OrderId`). Until the backend adds an endpoint (e.g. `GET /api/payments/by-order/{orderId}` or extending `OrderDetails` with `paymentId/clientSecret` for the owner), the SPA **cannot** run `stripe.confirmCardPayment`. Do not work around this by calling Stripe directly (no intent id/secret) or by exposing `SecretKey`.

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

- Intent amount is currently hardcoded (`authoritativeAmount = 19.99m`, `authoritativeCurrency = "USD"`) — multi-currency/real totals are not wired; verify amounts in Stripe dashboard test mode before demoing non-USD carts.
- Webhook local testing needs Stripe CLI forwarding with the configured `WebhookSecret` (tolerance 300s, `throwOnApiVersionMismatch: false`); duplicate deliveries must return `200` (check `AlreadyProcessed` log).
- Status mapping for UI: `Payment Pending/Processing` → spinner; `Succeeded` → success; `Failed/Cancelled` → error + `GET /orders/{id}` reason; `Refunded` → post-pay refund notice.
