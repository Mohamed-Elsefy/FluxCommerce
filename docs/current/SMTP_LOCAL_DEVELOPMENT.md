# Local Email Development with smtp4dev

`smtp4dev` is a development SMTP server that captures outgoing email so you can inspect it in a browser. It never delivers messages to real recipients, which makes it ideal for locally testing the Identity module's email flows (account verification, password reset) without a real SMTP provider.

## How it is wired up

- `docker-compose.yml` defines a `smtp4dev` service (image `rnwood/smtp4dev:latest`) with the following port mappings:
  - `2525` -> SMTP server (the app sends email here)
  - `5000` -> Web UI (view captured messages)
- `appsettings.Development.json` points the `Email` options at `localhost:2525` with `UseSsl: false` and empty `Username`/`Password` (smtp4dev accepts everything without authentication).
- The Web UI and captured messages live in a SQLite database inside the container, so messages are **lost when the container is removed** (e.g. `docker compose rm` or a clean `docker compose up --force-recreate`).

## Starting smtp4dev

```powershell
docker compose up -d smtp4dev
```

Verify it is running:

```powershell
docker compose ps smtp4dev
```

The other services (`sqlserver`, `redis`, `rabbitmq`) are unaffected; they can be started independently:

```powershell
docker compose up -d
```

## Using it

1. Start the API (which also runs the Identity module):

   ```powershell
   dotnet run --project src/FluxCommerce.Api
   ```

2. Trigger an email flow, e.g. register a user:

   ```powershell
   Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/auth/register" -ContentType "application/json" -Body '{"email":"user@example.com","password":"TestPass123!"}'
   ```

3. Open the Web UI at <http://localhost:5000> and find the captured message.

4. The verification/reset links in the message contain the raw token. Use it directly in the respective endpoint (e.g. `POST /api/auth/verify-email`), or copy the full link into a browser.

> The SMTP port is `2525` because port 25 on the host is usually blocked by Windows or other local services. If you change the mapping in `docker-compose.yml`, update the `Email:Port` value in `appsettings.Development.json` (or your user secrets) to match.

## Stopping and removing smtp4dev

```powershell
docker compose stop smtp4dev    # stop only smtp4dev
docker compose rm smtp4dev      # delete it (also deletes all captured messages)
```

## Production settings

The `Email` section in `appsettings.json` (Host, Port 587, `UseSsl: true`, credentials, `FromEmail`, `FrontendBaseUrl`) is meant for real SMTP providers and must be supplied at deployment time via user secrets or environment variables — it is intentionally not populated in source.
