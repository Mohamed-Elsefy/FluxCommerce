# Local Email Development with smtp4dev

## Overview

`smtp4dev` is a lightweight development SMTP server that runs in Docker. It accepts outgoing email from the application and captures it in a local SQLite database so you can inspect every message in a web UI.

We use it to test the Identity module's email flows — account verification and password reset — without a real SMTP provider. It never delivers messages to real recipients, so there is zero risk of sending test emails to actual mailboxes, and no dependency on external credentials to do local development.

## Architecture

```
Application (FluxCommerce API)
        │  sends email via SMTP (port 2525)
        ▼
MailKit (SmtpEmailSender)
        │
        ▼
smtp4dev Container (SMTP server, port 2525)
        │  stores captured messages in SQLite
        ▼
Web UI (http://localhost:5000) — inspect captured messages
```

The API never talks to a real mail server. `MailKit` connects to the `smtp4dev` container exactly as it would to a real SMTP host; `smtp4dev` accepts everything without authentication and simply stores the messages.

## Prerequisites

- **Docker Desktop** — required to run the `smtp4dev` container (must be started before the steps below).
- **Docker Compose** — ships with Docker Desktop; used to define and start the services in `docker-compose.yml`.
- **The FluxCommerce solution** — the API (`src/FluxCommerce.Api`) must be able to reach `localhost:2525` while running locally.

## Starting smtp4dev

Start only the `smtp4dev` service:

```powershell
docker compose up -d smtp4dev
```

This pulls the `rnwood/smtp4dev:latest` image on first run, starts the container in the background (`-d`), and maps port `2525` (SMTP) and port `5000` (Web UI) to the host. It does not touch the other services.

Verify it is running:

```powershell
docker compose ps smtp4dev
```

This shows the state of the `smtp4dev` service — the container should report `Up` (healthy/running). If you see `Exited` or `Restarting`, see [Common Problems](#common-problems).

## Starting the complete infrastructure

Start every service defined in `docker-compose.yml`:

```powershell
docker compose up -d
```

This starts four containers:

| Container | Purpose | Host port(s) |
|-----------|---------|--------------|
| `fluxcommerce-sqlserver` | SQL Server 2022 database | `1433` |
| `fluxcommerce-redis` | Redis cache / token storage | `6379` |
| `fluxcommerce-rabbitmq` | RabbitMQ message broker (future messaging) | `5672`, `15672` (management UI) |
| `fluxcommerce-smtp4dev` | Local SMTP server + Web UI | `2525` (SMTP), `5000` (Web UI) |

> `sqlserver` and `rabbitmq` require a `.env` file in the repository root containing `MSSQL_SA_PASSWORD`, `RABBITMQ_DEFAULT_USER`, and `RABBITMQ_DEFAULT_PASS`. `smtp4dev` itself has no such requirement — `docker compose up -d smtp4dev` works even without `.env`.

Verify the whole stack:

```powershell
docker compose ps
```

## Running the API

### Visual Studio

1. Open the solution (`FluxCommerce.sln`).
2. Set `FluxCommerce.Api` as the startup project.
3. Select the **https** profile (launches at `https://localhost:7119` with Swagger).
4. Press **F5**.

### dotnet run

```powershell
dotnet run --project src/FluxCommerce.Api
```

By default this uses the first launch profile (`http`), which listens at `http://localhost:5260`. To use the HTTPS profile explicitly:

```powershell
dotnet run --project src/FluxCommerce.Api --launch-profile https
```

Either way, Swagger UI is available at `/swagger` — for example `http://localhost:5260/swagger` or `https://localhost:7119/swagger`.

> All API routes below use the base path from the profile you chose. The examples assume `http://localhost:5260`.

## Testing Registration

1. **Open Swagger** at `http://localhost:5260/swagger` (or `https://localhost:7119/swagger`).
2. **Call Register**: expand `POST /auth/register` (marked `AllowAnonymous`), click **Try it out**, and send:

   ```json
   {
     "email": "customer@example.com",
     "password": "TestPass123!",
     "firstName": "John",
     "lastName": "Doe"
   }
   ```

   Password rules: 8–128 characters, at least one uppercase letter, one lowercase letter, one digit, and one special character. First and last names are required; the display name is derived from them. A `200` response with the user payload means the account was created in `Pending` status.
3. **Create account**: the API sends a verification email via SMTP and returns the user object (`Status: Pending`).
4. **Check smtp4dev**: open the Web UI at `http://localhost:5000` — the captured message "Verify your FluxCommerce account" appears in the message list.
5. **Open verification email**: click the message in the list to view it. The email contains a "Verify email" button whose link looks like:

   ```
   https://localhost:4200/verify-email?token=<token>
   ```

   The link points at the Angular frontend (`FrontendBaseUrl`). The token is the `token` query parameter. Tokens are valid for **24 hours**.
6. **Copy verification token**: copy the value of the `token` query parameter from the link (or use the raw link).
7. **Call VerifyEmail**: expand `POST /auth/verify-email`, click **Try it out**, and send:

   ```json
   {
     "token": "<token copied in step 6>"
   }
   ```

8. **Verify account**: a `200` response confirms the address. The user object now reports `Status: Active`. The same token cannot be used twice.

If you close the browser before verifying, `POST /auth/resend-verification` (body `{ "email": "customer@example.com" }`) issues a fresh token — check smtp4dev for the new message.

## Testing Forgot Password

1. **Call ForgotPassword**: expand `POST /auth/forgot-password` and send:

   ```json
   {
     "email": "customer@example.com"
   }
   ```

   A `200` is returned regardless of whether the account exists (no account enumeration), and if it does exist, a reset email is sent.
2. **Receive email**: open the smtp4dev Web UI at `http://localhost:5000` — the message "Reset your FluxCommerce password" appears. It contains a link of the form:

   ```
   https://localhost:4200/reset-password?token=<token>
   ```

   Reset tokens are valid for **1 hour**.
3. **Copy token**: copy the `token` query parameter from the reset link.
4. **Call ResetPassword**: expand `POST /auth/reset-password` and send:

   ```json
   {
     "token": "<token copied in step 3>",
     "newPassword": "NewPass456!"
   }
   ```

   A `200` means the password was changed. The old password no longer works; the new one does. Reset tokens are single-use.

## smtp4dev Web UI

The Web UI is at **http://localhost:5000** (port `5000` on the host maps to port `80` inside the container).

- Captured messages appear in the list on the left-hand side of the screen, newest first.
- Click a message to view subject, sender, recipient, and the full HTML/text body — the verification and reset links are inside the body.
- Messages are stored in a SQLite database **inside the container**, so they are lost when the container is removed (`docker compose rm smtp4dev` or a fresh `docker compose up --force-recreate`). Restarting (`docker compose restart`) keeps them.

## Common Problems

| Problem | Likely cause | Fix |
|---------|--------------|-----|
| `docker compose up` says Docker is not running | Docker Desktop is not started | Start Docker Desktop and wait for the engine, then retry |
| `docker compose ps smtp4dev` shows `Exited`/`Restarting` | Container failed to start | `docker compose logs smtp4dev` to inspect; `docker compose restart smtp4dev`; remove and recreate (`docker compose up -d --force-recreate smtp4dev`) |
| `port is already allocated` when starting smtp4dev | Port `2525` or `5000` is already in use on the host | Run `netstat -ano` and look for the port in the output; stop the owning process, or change the host-side mapping in `docker-compose.yml` and update `Email:Port` in `appsettings.Development.json` to match the new SMTP port |
| No email appears in the Web UI | The API cannot reach the SMTP server | Check `Email:Host` is `localhost` and `Email:Port` is `2525` in `appsettings.Development.json`; confirm `docker compose ps smtp4dev` shows `Up`; watch the API console for `Failed to connect` SMTP errors |
| `Connection refused` / `No such host` on email send | Wrong `Email:Host` or `Email:Port` in config | `Host` must be `localhost` (not the container name — the API runs on the host, not in the compose network); `Port` must match the host mapping `2525` |
| API does not start because SMTP fails | Missing SMTP config | The API does not connect to SMTP at startup; failures surface when an email is sent (register/forgot-password) — see "No email appears" |

## Useful Docker Commands

```powershell
docker compose ps
```

Shows the state of every service in the compose project.

```powershell
docker compose logs smtp4dev
```

Streams the container logs (add `-f` to follow). Useful when no email arrives or the container misbehaves.

```powershell
docker compose restart smtp4dev
```

Restarts the container, keeping stored messages.

```powershell
docker compose stop smtp4dev
```

Stops the container without removing it. The stored messages are preserved.

```powershell
docker compose rm smtp4dev
```

Removes the container — **this deletes all captured messages** (they live in a SQLite file inside the container).

```powershell
docker compose down
```

Stops and removes all containers **and volumes** (use `-v` with care — it deletes the SQL Server, Redis, and RabbitMQ data volumes too).

## Production Notes

**smtp4dev is for LOCAL DEVELOPMENT ONLY.** It accepts mail without authentication and never delivers anything — a real deployment must use a real SMTP provider.

For production:

- The `Email` section in `appsettings.json` (host, port `587`, `UseSsl: true`, credentials, `FromEmail`, `FrontendBaseUrl`) is the contract for real providers and is intentionally **not populated in source**.
- Supply production SMTP settings at deployment time via user secrets or environment variables.
- Use an authenticated, TLS-encrypted SMTP provider (SendGrid, SES, Mailgun, SMTP2GO, etc.) and keep credentials out of source control.
