# FluxCommerce — Enterprise Commerce & Order Management Platform

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/ASP.NET%20Core-10.0-512BD4?style=for-the-badge" alt="ASP.NET Core" />
  <img src="https://img.shields.io/badge/Clean%20Architecture-modular%20monolith-0F766E?style=for-the-badge" alt="Architecture" />
  <img src="https://img.shields.io/badge/license-MIT-green?style=for-the-badge" alt="License" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen?style=flat-square" alt="Build" />
  <img src="https://img.shields.io/badge/tests-2856%20passing-brightgreen?style=flat-square" alt="Tests" />
  <img src="https://img.shields.io/badge/arch%20tests-177%2F177-brightgreen?style=flat-square" alt="Arch Tests" />
  <img src="https://img.shields.io/badge/coverage-RabbitMQ%20%7C%20Redis%20%7C%20SQL%20Server-blue?style=flat-square" alt="Infra" />
</p>

> **Not another CRUD store.** FluxCommerce is a production-oriented reference implementation that proves an order can survive concurrency, duplicate messages, broker crashes, and Stripe retries — without overselling or double-charging.

---

## Table of Contents

- [Why FluxCommerce](#why-fluxcommerce)
- [Architecture at a Glance](#architecture-at-a-glance)
- [Tech Stack](#tech-stack)
- [Key Reliability Patterns](#key-reliability-patterns)
- [Modules](#modules)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [API Reference](#api-reference)
- [Project Structure](#project-structure)
- [Testing](#testing)
- [Documentation](#documentation)
- [Roadmap](#roadmap)
- [Philosophy](#philosophy)
- [Contributing](#contributing)
- [License](#license)

---

## Why FluxCommerce

Most e-commerce tutorials stop at `POST /orders`. This project starts there and answers the hard questions:

| Question | Answer in this repo |
|---|---|
| Why Modular Monolith over Microservices? | [ADR-001](docs/adr/001-modular-monolith.md) — one deployable, strong boundaries, cheap extraction later |
| How to prevent overselling with `Available = 1` and two concurrent reserves? | Atomic conditional `UPDATE` + `Version` optimistic token ([Phase 5](docs/completed/PHASE_5_COMPLETION_REPORT.md)) |
| What if RabbitMQ delivers the same event twice? | `ProcessedMessage (EventId, Handler)` PK + `IIdempotentProcessor` boundary ([ADR-008](docs/adr/008-consumer-contract-and-idempotency.md)) |
| What if the consumer crashes after DB commit but before ACK? | At-least-once + redelivery → idempotency → exactly-one business effect |
| Duplicate Stripe webhook? | `ProcessedWebhookEvents` unique `StripeEventId` + 200 on duplicate ([ADR-013](docs/adr/013-webhook-idempotency.md)) |
| Payment succeeds but order confirmation fails? | Saga retry → DLQ → explicit refund compensation ([ADR-014](docs/adr/014-payment-refund-compensation.md)) |
| How to trace one order across async hops? | W3C `traceparent` propagated via Outbox → broker headers → consumer ([ADR-005](docs/adr/005-observability.md)) |

**Core principle:**
> *Each module owns its business state. The Saga orchestrates. Modules communicate only through explicit contracts.*

---

## Architecture at a Glance

**Style:** Modular Monolith · Clean Architecture · DDD · Event-Driven

```
                 Angular (Storefront / Admin)          ← future
                         \        /
                          \      /
                    ASP.NET Core API Host
                             │
                     Modular Monolith
                             │
        ┌────────────────────┼────────────────────┐
        │                    │                    │
     Catalog              Orders              Inventory
        │                    │                    │
        │                 Payments                │
        └────────────────────┼────────────────────┘
                             │
                      SQL Server (1 instance, N DbContexts)
                             │
                ┌────────────┼──────────┐
                │            │          │
             Redis       Outbox     RabbitMQ (topic: fluxcommerce.events)
                │            │          │
                └────────────┼──────────┘
                             │
                    Background Workers
                             │
                         Stripe
```

**Request → Event flow:**

```
Business Operation → DB Transaction (State + Outbox) → Outbox Dispatcher → RabbitMQ
→ Consumer (manual ACK) → IIdempotentProcessor (State + ProcessedMessage atomic)
→ Saga transition (Version-guarded) → next IntegrationEvent
```

> Full spec: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) · Master context: [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md)

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 10.0, ASP.NET Core 10, C# latest |
| **Persistence** | EF Core 10, SQL Server 2022 (module-per-DbContext), Redis 8 (Cart) |
| **Messaging** | RabbitMQ 4 (durable topic exchange + confirms), Transactional Outbox |
| **Payments** | Stripe.net 42.3 (isolated to `Payments.Infrastructure` only) |
| **Observability** | Serilog, OpenTelemetry (ActivitySource `FluxCommerce`), W3C Trace Context |
| **Auth** | ASP.NET Identity + JWT (Access + Refresh), `Roles: Customer/Admin/SuperAdmin` |
| **Tooling** | Docker Compose, Swashbuckle, NetArchTest, xUnit, Moq, MailKit |
| **Quality Gates** | `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, 177 arch tests |

---

## Key Reliability Patterns

- **Transactional Outbox** — `Business State + OutboxMessage` commit atomically per `DbContext`; dispatcher claims via `UPDLOCK, READPAST` CTE, exponential backoff, stale-claim recovery. No distributed transactions.
- **At-Least-Once + Idempotent Consumers** — `ProcessedMessage` composite PK `(Key, Handler)` is the final arbiter; `TryProcessAsync` guarantees exactly-one effect even on concurrent redelivery.
- **Saga Orchestration** — Orders-owned `OrderSaga` (`WaitingForInventory → WaitingForPayment → Completed/Failed/ReleasingInventory/Cancelled/Refunding`), `Version` concurrency, compensation via `inventory.release_requested` and `payments.refund_requested`.
- **Inventory Concurrency** — atomic conditional update prevents overselling; `Available = OnHand - Reserved`.
- **Stripe Isolation** — `Payments.Domain/Application/Contracts` have zero `Stripe.net` refs; only `Payments.Infrastructure` touches the SDK (enforced by arch tests). Idempotency via `intent_{OrderId}_{PaymentId}_{Version}` + webhook dedup.
- **Reservation Expiry** — Inventory worker expires → `InventoryReservationExpiredIntegrationEvent` via Outbox → Orders Saga `WaitingForPayment → Cancelled` (terminal-safe, no double release).
- **Resilience** — bounded retry (Fixed/Linear/Exponential + `MaxDelay`), DLX/DLQ with enriched headers (`x-dead-letter-reason`, `x-attempt`, `x-error-type`), graceful shutdown (unacked → redelivery).

---

## Modules

| Module | Owns | Highlights |
|---|---|---|
| **Identity** | Users, Roles, Tokens | JWT + Refresh, `Customer/Admin/SuperAdmin`, email verify, lockout, config-driven seeder |
| **Catalog** | Products, Variants, SKU | SQL filtering/sorting/pagination, soft delete, `decimal(18,6)` money |
| **Cart** | Guest & Auth carts | Redis primary, opaque `CartId`, merge Guest→Auth, Catalog contract `ICatalogVariantCartLookup` |
| **Orders** | Order, OrderLine, Saga | Immutable snapshot, `Idempotency-Key` header `(UserId,Key)` unique, `Pending→Cancelled/Confirmed` |
| **Inventory** | Stock, Reservations | Reservation lifecycle, expiration worker, oversell-proof |
| **Payments** | Payment, Webhook, Refund | Stripe intent isolation, `ProcessedWebhookEvents`, Outbox `payments.payment_succeeded/failed` |
| **BuildingBlocks** | Cross-cutting only | `Domain`, `Application` (Result/Error), `Messaging` (contract-only), `Outbox`, `Idempotency`, `Persistence`, `Observability`, `Localization`, `Web`, `Infrastructure` |

> `Pricing`, `Promotions`, `Wishlist`, `Reviews`, `Shipping`, `Notifications`, `Analytics`, `Search` are deferred until Core MVP (Phase 12) is green — see [Roadmap](#roadmap).

---

## Getting Started

### Prerequisites

- [.NET SDK 10.0.302+](https://dotnet.microsoft.com/download) (`global.json`)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- EF Core CLI: `dotnet tool install --global dotnet-ef --version 10.0.10`

### 1. Clone & Configure

```bash
git clone https://github.com/<your-org>/FluxCommerce.git
cd FluxCommerce

# create env for compose (gitignored)
cp .env.example .env
# edit .env → set MSSQL_SA_PASSWORD, RABBITMQ_DEFAULT_USER/PASS
```

### 2. Start Infrastructure

```bash
docker compose up -d
docker compose ps          # sqlserver :1433, redis :6379, rabbitmq :5672/:15672, smtp4dev :5000
```

RabbitMQ UI → http://localhost:15672 · smtp4dev → http://localhost:5000

### 3. Configure SuperAdmin Seeder (dev only)

`appsettings.json` ships with `Identity:Seeder:Enabled=false`. Dev enables it with an empty password — override via User Secrets:

```bash
dotnet user-secrets set "Identity:Seeder:SuperAdmin:Password" "Test123!" --project src/FluxCommerce.Api
dotnet run --project src/FluxCommerce.Api
# log: Seeded SuperAdmin admin@flux.local  (first boot)
# log: SuperAdmin already exists, skipping seeding  (second boot — idempotent)
```

Login:

```bash
curl -X POST https://localhost:7119/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@flux.local","password":"Test123!"}'
```

### 4. Run the API

```bash
dotnet run --project src/FluxCommerce.Api --launch-profile https
# Swagger: https://localhost:7119/swagger
# HTTP:    http://localhost:5260/swagger
```

### 5. Verify

```bash
dotnet build FluxCommerce.slnx -c Debug      # 0 warnings, 0 errors
dotnet test --filter "Architecture"          # 177/177
```

> More: [`docs/current/LOCAL_DEVELOPMENT.md`](docs/current/LOCAL_DEVELOPMENT.md) · [`docs/current/EF_CORE_COMMANDS.md`](docs/current/EF_CORE_COMMANDS.md)

---

## Configuration

All secrets are externalized via `IConfiguration` / `IOptions<T>` — never committed.

| Setting | Source | Example |
|---|---|---|
| `ConnectionStrings:Database` | `.env` → `MSSQL_SA_PASSWORD` | `Server=localhost,1433;Database=FluxCommerce_Orders;...` |
| `MessageBroker:*` | `.env` `RABBITMQ_*` | `fluxcommerce.events` topic exchange |
| `Jwt:SigningKey` | User Secrets / env | `Jwt__SigningKey` |
| `Stripe:SecretKey/WebhookSecret` | User Secrets / env | `Stripe__SecretKey` |
| `Identity:Seeder:SuperAdmin:Password` | User Secrets | `Identity:Seeder:SuperAdmin:Password` |
| `Cache:ConnectionString` | compose | `localhost:6379` |

`appsettings.Development.json` uses `localhost` hosts with empty secrets; production (`appsettings.json`) ships with `""` defaults and `ValidateOnStart`.

---

## API Reference

Swagger is available at `/swagger` when the API is running. Key endpoints:

| Area | Method & Path | Auth |
|---|---|---|
| Auth | `POST /auth/register`, `POST /auth/login`, `POST /auth/refresh` | Anonymous |
| Catalog | `GET /catalog/products`, `POST /catalog/products` | Read anon, Write `Admin` |
| Cart | `POST /cart/guest`, `POST /cart/authenticated`, `POST /cart/merge` | Guest / Auth |
| Orders | `POST /orders/checkout` (`Idempotency-Key` header), `GET /orders`, `POST /orders/{id}/cancel` | `Customer` |
| Inventory | `GET /inventory/items` | `Admin` |
| Payments | `GET /api/payments/{paymentId}`, `POST /api/payments/webhook` | Owner `Customer` / `AllowAnonymous` (Stripe sig) |

ProblemDetails are localized (`en/fr/de/es/ar` via `Accept-Language` / `?culture=` / cookie, `Content-Language` header) — see [`docs/ARCHITECTURE.md#26`](docs/ARCHITECTURE.md).

---

## Project Structure

```
FluxCommerce.slnx
├── src/
│   ├── FluxCommerce.Api/                 # Host, Program.cs, middleware, Swagger
│   ├── BuildingBlocks/                   # Domain, Application, Messaging, Outbox, Idempotency,
│   │                                     # Persistence, Infrastructure, Observability, Localization, Web
│   └── Modules/
│       ├── Identity/   (Domain/Application/Infrastructure/Contracts)
│       ├── Catalog/    (Domain/Application/Infrastructure/Contracts)
│       ├── Cart/       (Domain/Application/Infrastructure/Contracts — Redis)
│       ├── Orders/     (Domain/Application/Infrastructure/Contracts — Saga)
│       ├── Inventory/  (Domain/Application/Infrastructure/Contracts)
│       └── Payments/   (Domain/Application/Infrastructure/Contracts — Stripe isolated)
├── tests/
│   ├── Architecture/   # 177 tests — dependency & layer rules (NetArchTest)
│   ├── UnitTests/      # 2277 tests — domain & application
│   ├── IntegrationTests/ # 402 tests — EF + Redis + RabbitMQ (real infra)
│   └── Reliability/    # Failure harness (Phase 12)
└── docs/
    ├── PROJECT_CONTEXT.md   # Master spec & AI implementation guide
    ├── ARCHITECTURE.md      # Architecture specification
    ├── adr/ (15)            # Architecture Decision Records
    ├── phases/ (11)         # Phase implementation plans
    ├── completed/ (13)      # Phase completion reports + INDEX
    ├── refactoring/         # Refactoring cycles R1/R2
    └── current/             # LOCAL_DEVELOPMENT, EF_CORE_COMMANDS
```

Conventions: `TreatWarningsAsErrors`, `Nullable=enable`, one `DbContext` per module, `Contracts` depend only on `BuildingBlocks.Messaging`.

---

## Testing

```bash
# all — per-suite is authoritative (parallel Redis EVAL can flake in combined run)
dotnet test tests/Architecture/FluxCommerce.ArchitectureTests -c Debug
dotnet test tests/UnitTests/FluxCommerce.UnitTests -c Debug
dotnet test tests/IntegrationTests/FluxCommerce.IntegrationTests -c Debug

# with infra (sqlserver + redis + rabbitmq must be up)
dotnet test tests/IntegrationTests -c Debug  # 402/402

# EF model drift check (should be "No changes")
dotnet ef migrations has-pending-model-changes --project src/Modules/Orders/FluxCommerce.Modules.Orders.Infrastructure --startup-project src/FluxCommerce.Api
```

| Suite | Result (R2.8) | What it proves |
|---|---|---|
| Architecture | **177/177** | No cross-module DbContext access, `Stripe` infra-only, layer boundaries |
| Unit | **2277/2277** | Domain invariants, saga matrix, webhook interpreter, idempotency |
| Integration | **402/402** | Outbox atomic claim, consumer redelivery, saga E2E, oversell race |

---

## Documentation

| Document | Purpose |
|---|---|
| [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md) | Master spec — read this first (phases, ownership, reliability) |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Module structure, BuildingBlocks, messaging, saga, observability |
| [`docs/adr/`](docs/adr/) | 15 ADRs — modular monolith → refund compensation |
| [`docs/phases/`](docs/phases/) | Implementation plans per phase (0–12) |
| [`docs/completed/`](docs/completed/) | Completion reports + [`INDEX.md`](docs/completed/INDEX.md) |
| [`docs/current/LOCAL_DEVELOPMENT.md`](docs/current/LOCAL_DEVELOPMENT.md) | Docker, seeder, Swagger, troubleshooting |
| [`docs/current/EF_CORE_COMMANDS.md`](docs/current/EF_CORE_COMMANDS.md) | `dotnet ef` recipes per module |
| [`docs/refactoring/`](docs/refactoring/) | R1/R2 hardening — findings, moves, verification |

---

## Roadmap

Incremental — future phases are **not** implemented prematurely.

| Phase | Status | Summary |
|---|---|---|
| 0 Architecture Foundation | ✅ Done | Solution, BuildingBlocks, module skeletons |
| 1 Identity & Access | ✅ Done | JWT, roles, seeder |
| 1.5 Runtime Preparation | ✅ Done | Docker, Swagger, DB init |
| 2 Catalog | ✅ Done | Products, variants, persistence |
| 3 Cart | ✅ Done | Redis cart, guest→auth merge |
| 4 Orders & Checkout | ✅ Done | Immutable snapshot, idempotent checkout |
| 5 Inventory Concurrency | ✅ Done | Reservation, oversell prevention |
| 6 Outbox + RabbitMQ | ✅ Done | Reliable publication, topic exchange |
| 7 Idempotent Consumers | ✅ Done | Inbox, retry, DLQ |
| 8 Order/Inventory Saga | ✅ Done | `order.created → reservation` workflow |
| 9 Stripe Payments | ✅ Done | Intent isolation, webhook dedup |
| 10 Payment Saga & Compensation | ✅ Done | Refund, release, cancel orchestration |
| 11 Reservation Expiry | ✅ Done | Expiration worker → Saga cancel |
| **12 Failure Testing** | ✅ **Done** | Chaos, redelivery, DLQ, timeout harness |
| 13 Search · 14 Promotions · 15 Wishlist · 16 Reviews · 17 Notifications · 18 Shipping · 19 Analytics · 20 Hardening | ⏳ Planned | Post-MVP |

Core MVP is complete after Phase 12.

---

## Philosophy

```
Simple Correct Architecture
        ↓
Strong Module Boundaries
        ↓
Reliable Business Workflow
        ↓
Concurrency → Async → Failure Handling → Observability → Hardening
```

> Build a strong Modular Monolith first, then introduce distributed-systems patterns only where they solve real reliability or scalability problems.

---

## Contributing

Contributions are welcome — especially failure-scenario tests for Phase 12.

1. Read `docs/PROJECT_CONTEXT.md` and `docs/ARCHITECTURE.md`
2. Check the current phase in `docs/completed/INDEX.md` — do not implement future phases early
3. Respect module boundaries (`No cross-DbContext`, `Contracts` only via `BuildingBlocks.Messaging`)
4. Add/maintain Architecture + Unit + Integration tests
5. Run `dotnet build` (0W/0E) and `dotnet test` per-suite before PR

See `docs/refactoring/REFACTORING_2_PLAN.md` for the hardening bar that gates Phase 12.

---

## License

MIT — intended as a portfolio and educational reference. See `LICENSE` if present; otherwise treat as MIT for learning and reuse.

---

<p align="center">
  Built to demonstrate <strong>reliable order processing</strong> — not just CRUD.
  <br/>
  <sub>FluxCommerce · Modular Monolith · Outbox · Idempotent Consumers · Saga · Compensation</sub>
</p>
