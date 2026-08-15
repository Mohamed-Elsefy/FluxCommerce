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
