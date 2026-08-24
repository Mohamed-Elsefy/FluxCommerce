# ADR-011: Stripe Integration Boundary

## Status

Accepted

## Context

Phase 9 introduces Stripe as the external payment provider. The Stripe SDK is an external, third-party library that must be isolated from the rest of the system to preserve module boundaries, testability, and independence from external service contracts. The Phase 9 implementation plan (§5.6) explicitly states that the Stripe SDK must be confined to `Payments.Infrastructure` behind an abstraction.

## Decision

### Abstraction layer

- **`IStripePaymentGateway`** is defined in `Payments.Application` and declares only the operations Phase 9 needs:
  - `CreatePaymentIntentAsync(...)` — creates a Stripe PaymentIntent
  - `RetrievePaymentIntentAsync(...)` — retrieves a PaymentIntent status from Stripe
- **`StripePaymentGateway`** is implemented in `Payments.Infrastructure` and is the only class that references the Stripe SDK (`using Stripe;`).
- All other layers (Domain, Contracts, API/Host) depend only on the abstraction, never on the Stripe SDK directly.

### Layer restrictions (from Phase 9 plan §5.6)

```
Payments.Domain
    X--> Stripe SDK          (FORBIDDEN)
    X--> EF Core             (FORBIDDEN)
    X--> RabbitMQ.Client     (FORBIDDEN)

Payments.Application
    X--> Stripe SDK          (FORBIDDEN)
    X--> RabbitMQ.Client     (FORBIDDEN)

Payments.Contracts
    X--> EF Core             (allowed, for entity shapes)
    X--> Stripe SDK          (FORBIDDEN)
    X--> RabbitMQ.Client     (allowed, for integration event contracts)
    X--> Payments.Domain     (allowed)
```

### Configuration externalization

Stripe configuration is externalized via the Options pattern, reading from environment variables or secret stores:

```csharp
public class StripeSettings
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
```

Configuration is bound in `Payments.Infrastructure` DI registration; no secrets are checked into source control. Consistent with the project's existing secret-handling rules (ARCHITECTURE.md §21).

### Stripe SDK placement

The Stripe SDK is referenced only from `Payments.Infrastructure`. The gateway implementation uses the Stripe .NET SDK to:
- Create PaymentIntents with explicit idempotency keys
- Retrieve PaymentIntent status
- Handle API errors and translate them to domain-safe results

No other module (Orders, Inventory, Catalog, etc.) may reference the Stripe SDK.

## Consequences

Positive:
- Application and Domain layers remain testable without Stripe dependencies
- Stripe SDK changes or provider switches are contained within Infrastructure
- Clear separation of concerns: abstraction in Application, implementation in Infrastructure
- Consistent with the project's modular monolith boundaries and module ownership rules

Negative:
- Requires the additional abstraction layer (IStripePaymentGateway + StripePaymentGateway)
- Calls to the gateway go through DI, which adds a minor indirection
- The narrow carve-out in architecture tests must be maintained (Payments.Infrastructure → Stripe SDK allowed only)

## References

- Phase 9 Implementation Plan §5.6 (Stripe Isolation)
- Phase 9 Implementation Plan §9.5 (Stripe Integration Boundary)
- Phase 9 Implementation Plan §9.6 (Payment Intent Creation)
- Phase 9 Implementation Plan §9.9 (Webhook Idempotent Processing)
- ARCHITECTURE.md §21 (Configuration / Secrets)
- ADR-007: Integration Event Contract Placement (abstraction boundary pattern)