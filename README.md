# Hally

AI-first family assistant: *"Çek, söyle, Hally halletsin."* Parents snap, share or say something; Hally turns it into tasks and events, **only after their approval**, and shares the load between the adults.

> "Hally" is a working name. It is configured in one place per app (`App:Name` in the API, `AppConfig.appName` in Flutter).

## Repository layout

| Path | What |
|---|---|
| `backend/src/Hally.Domain` | Entities and business rules (families, members, children, invitations, users) |
| `backend/src/Hally.Infrastructure` | EF Core + PostgreSQL, migrations, analytics sink |
| `backend/src/Hally.Api` | ASP.NET Core 8 Web API (`/api/v1`), auth, policies, idempotency |
| `backend/tests/Hally.Api.Tests` | Unit + integration tests against real PostgreSQL |
| `mobile/` | Flutter app (Riverpod, GoRouter, Dio, tr/en localization) |
| `docs/adr` | Architecture decision records |

## Run the backend

```bash
cp .env.example .env        # fill in POSTGRES_PASSWORD and JWT_SIGNING_KEY
docker compose up --build   # PostgreSQL + API on http://localhost:8080
```

Swagger: http://localhost:8080/swagger · Health: `/health/live`, `/health/ready`.

Without Docker: start PostgreSQL locally and run `dotnet run --project backend/src/Hally.Api` (uses `appsettings.Development.json`, migrates on startup).

### Tests

```bash
dotnet test                                     # uses Testcontainers (needs Docker)
HALLY_TEST_CONNECTION="Host=localhost;Username=postgres;Password=postgres" dotnet test   # or an existing server
```

### Migrations

```bash
dotnet tool install -g dotnet-ef --version 8.0.11
dotnet ef migrations add <Name> -p backend/src/Hally.Infrastructure -s backend/src/Hally.Infrastructure -o Persistence/Migrations
```

## Run the mobile app

```bash
cd mobile
flutter create --org app.hally --project-name hally --platforms=android,ios .   # first time only: generates native folders
flutter pub get
flutter run --dart-define=HALLY_API_URL=http://10.0.2.2:8080   # Android emulator → host API
```

`--dart-define` keys: `HALLY_API_URL`, `HALLY_APP_NAME`, `HALLY_GOOGLE_SERVER_CLIENT_ID`, `HALLY_DEV_SIGN_IN` (set `false` for store builds).

## Sign-in

The app gets an ID token from Google or Apple and posts it to `POST /api/v1/auth/{google|apple}`; the API verifies it and issues its own JWT access token plus a rotating refresh token.

| Provider | Config needed |
|---|---|
| Google | `ExternalAuth:GoogleClientIds` (API) · `HALLY_GOOGLE_SERVER_CLIENT_ID` + `google-services.json` / `GoogleService-Info.plist` (app) |
| Apple | `ExternalAuth:AppleAudiences` = bundle id / Services ID (API) · Sign in with Apple capability (iOS) |
| Dev | `ExternalAuth:EnableDevProvider=true`, Development/Testing only. Token: `dev:{email}:{name}` |

## API (Sprint 1)

| Method | Path | Notes |
|---|---|---|
| POST | `/api/v1/auth/{provider}` | Sign in / sign up |
| POST | `/api/v1/auth/refresh` | Rotates refresh token; reuse revokes all sessions |
| GET/PUT | `/api/v1/me` | Profile + families |
| POST | `/api/v1/families` | Create family (caller becomes Owner) |
| GET | `/api/v1/families/{id}` · `/members` | Members only |
| POST | `/api/v1/families/{id}/children` | First name only (no surname) |
| POST | `/api/v1/families/{id}/invitations` | 7-day link + prefilled WhatsApp text |
| GET | `/api/v1/invitations/{token}` | Public preview |
| POST | `/api/v1/invitations/{token}/accept` | Join as Adult |

All state-changing POSTs except auth require an `Idempotency-Key` header. Errors are RFC 9457 problem details with a `code` extension.

## Roadmap

- **Sprint 1 – Foundation** (this): solution, auth, families, invitations, design tokens, tr/en, onboarding shell, analytics boundary, CI.
- **Sprint 2 – Capture & AI proposals**: demo scan, capture sheet, share extension, extraction, approval sheet, tasks & calendar.
- **Sprint 3 – Assistant & daily loop**: chat, Today, reminders, push, helper share links, weekly card.
- **Sprint 4 – Monetization & beta**: plans, paywall, privacy & account deletion, beta pipeline.
