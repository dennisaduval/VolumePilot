# VolumePilot HQ API

The HQ API is the cloud-facing application boundary for HQ. It exposes health endpoints, cookie-based Identity sessions, a development-only first-owner bootstrap, and tenant-scoped organization, Job, and Event endpoints.

Run the API locally:

```sh
dotnet run --project apps/VolumePilot.HQ.Api/VolumePilot.HQ.Api.csproj
```

The API listens on `http://localhost:5080` in the supplied launch profile. In Development it uses a local SQLite database and creates the early schema on startup. Outside Development, configure the PostgreSQL `ConnectionStrings:HqDatabase` value and apply the HQ migrations before starting the API.

`GET /health/live` is a process liveness check. `GET /api/health` is the web client's same-origin API check. Neither endpoint reports database health.

## Local first run

1. Call `GET /api/auth/csrf` and retain the returned request token and anti-forgery cookie.
2. Check `GET /api/dev/bootstrap-status`.
3. If the response says bootstrap is required, send `POST /api/dev/bootstrap` with `X-CSRF-TOKEN`, a company name, email address, and a password of at least 12 characters that includes a digit, lowercase letter, uppercase letter, and symbol.
4. The bootstrap creates the first company owner and signs that owner in. The route is mapped only in Development and only succeeds before the first company is created.
5. Fetch a fresh CSRF request token after sign-in. Tokens are bound to the current authenticated identity; fetch another after any later sign-in.

Public registration is not mapped. Staff invitations and production email delivery will be added behind the accepted invitation-only account policy. Cookie-authenticated state changes require the anti-forgery header. Login attempts are rate-limited, and application data endpoints require an Owner or Admin membership in the active company.

## Initial data endpoints

- `GET` and `POST /api/organizations`
- `GET` and `POST /api/jobs`
- `GET /api/events?jobId={jobId}` and `POST /api/events`

The active company is taken from the authenticated session. Request bodies cannot choose the tenant. A user who belongs to multiple companies must select one through `POST /api/auth/active-company` before accessing company data.

## PostgreSQL schema changes

The `Persistence/Migrations` directory contains the PostgreSQL migration history. Development SQLite files are created with `EnsureCreated` and are not part of this migration history. The application does not apply production migrations on startup.

With the .NET 10 SDK and `dotnet-ef` 10.0.12 installed, generate an idempotent SQL script from the repository root:

```sh
dotnet ef migrations script --idempotent \
  --project apps/VolumePilot.HQ.Api/VolumePilot.HQ.Api.csproj \
  --startup-project apps/VolumePilot.HQ.Api/VolumePilot.HQ.Api.csproj \
  --context HqDbContext --output hq-postgres.sql
```

Review and apply that script to the intended PostgreSQL database as a separate deployment step. Keep database credentials outside source control. To add a schema change, update the model and scaffold a PostgreSQL migration with `dotnet ef migrations add <Name> --output-dir Persistence/Migrations` using the same project, startup project, and context options. CI checks for model changes without a migration and applies migrations to an ephemeral PostgreSQL database.

The production account invitation and email delivery flow is still pending, so the schema alone does not enable commercial onboarding.
