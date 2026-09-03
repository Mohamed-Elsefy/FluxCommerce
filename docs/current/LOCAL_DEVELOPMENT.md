# Local Development

## Start Infrastructure

docker compose up -d

or

docker compose up -d smtp4dev

The full stack starts SQL Server (`localhost:1433`), Redis (`localhost:6379`), and RabbitMQ (`localhost:5672`, management UI `localhost:15672`). Secrets are resolved from the gitignored `.env` file (`MSSQL_SA_PASSWORD`, `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS`).

---

## Check Containers

docker compose ps

docker compose ps smtp4dev

---

## Run API

dotnet run --project src/FluxCommerce.Api

or

dotnet run --project src/FluxCommerce.Api --launch-profile https

Swagger

http://localhost:5260/swagger

https://localhost:7119/swagger

smtp4dev

http://localhost:5000

---

## Identity Seeder (SuperAdmin Bootstrap)

The Identity seeder is **config-driven and idempotent**. It runs automatically on startup via `IdentitySeeder.SeedAsync()` (`Infrastructure/Persistence/Seeding/IdentitySeeder.cs`):

*   `MigrateAsync` is invoked first to ensure `Identity` schema (including `Roles` table) is up-to-date.
*   If `Identity:Seeder:Enabled=false` (default in `appsettings.json`), seeding is skipped.
*   If `Enabled=true` (default in `appsettings.Development.json`), the seeder checks idempotency via `UserRepository.ExistsByEmailAsync(seederEmail)` — if the SuperAdmin already exists, it logs `SuperAdmin already exists, skipping seeding` and exits.
*   Otherwise it reads the plain-text password from `Identity:Seeder:SuperAdmin:Password`, hashes it at runtime via `IPasswordHasher` (PBKDF2), creates `User.Register(..., [Roles.SuperAdmin])`, marks `IsEmailVerified=true` / `Status=Active`, and persists via `UserRepository.AddAsync` + `SaveChangesAsync`. It logs `Seeded SuperAdmin {Email}` (never password/hash).

### Configure SuperAdmin Password (Development)

`appsettings.Development.json` contains `Identity:Seeder:SuperAdmin:Email=admin@flux.local` with an **empty** `Password` (expects `UserSecrets` override):

```bash
# From repository root, set the seeder password (plain text, hashed at runtime):
dotnet user-secrets set "Identity:Seeder:SuperAdmin:Password" "Test123!" --project src/FluxCommerce.Api

# Or via environment variable (e.g., Docker, CI):
# IDENTIY__SEEDER__SUPERADMIN__PASSWORD=Test123!
# (Note: double underscore __ separates sections: Identity:Seeder:SuperAdmin:Password)
```

**Verify seeding:**

```bash
dotnet run --project src/FluxCommerce.Api
# Log: Seeded SuperAdmin admin@flux.local  (first boot)
# Log: SuperAdmin already exists, skipping seeding. Email: admin@flux.local  (second boot, idempotent)
```

**Login as SuperAdmin:**

```bash
curl -X POST https://localhost:7119/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@flux.local","password":"Test123!"}'
# → 200 { accessToken, refreshToken } with role SuperAdmin in JWT (ClaimTypes.Role)
```

> **Security:** Never hard-code or bake hashes via `HasData` migrations. The seeder reads the plain password from `appsettings+UserSecrets`/env only when `Enabled=true` and hashes via `IPasswordHasher` at runtime. `appsettings.json` ships with `Enabled:false` for production.

---

## Messaging (Phase 6 — Outbox + RabbitMQ)

The transactional Outbox publishes integration events to a durable RabbitMQ **topic** exchange
(`fluxcommerce.events` in production; `fluxcommerce.events.it` for integration tests). Configuration
lives under the `MessageBroker` and `OutboxDispatch` sections of `appsettings*.json`; the RabbitMQ
connection string can be overridden with the `FLUXCOMMERCE_RABBITMQ_CONNECTION` environment variable.

RabbitMQ management UI

http://localhost:15672

Check the broker

rabbitmq-diagnostics -q ping

---

## Useful Docker Commands

docker compose logs smtp4dev

docker compose restart smtp4dev

docker compose stop smtp4dev

docker compose down
