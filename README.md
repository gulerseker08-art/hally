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

## AI capture (Sprint 2)

Photos, screenshots and pasted messages go to `POST /api/v1/families/{id}/captures`. The API calls an OpenAI-compatible
model (`Ai:BaseUrl`, `Ai:ApiKey`, `Ai:VisionModel`, or `AI_*` in `.env`) and returns **proposals**. Nothing becomes a task or
event until a family member approves it.

The server never trusts the model blindly:
- Date phrases (`haftaya cuma`, `ayın 15'i`, `next Friday`) are resolved by `RelativeDateResolver` in the family time zone.
- A date or time the content does not mention is dropped and marked *needs info*.
- Children are matched only against the family's own children, and the model never picks an assignee.
- Content is wrapped as untrusted data, output is schema-validated and capped at 20 items.

Without AI settings, captures are saved with status `Failed` (`ai.unavailable`) and can be retried later.
Originals are AES-256-GCM encrypted on disk (`Storage:EncryptionKey`).

**Sharing into Hally.** Text, links and images shared from WhatsApp, Mail, Photos etc. become a capture (`source=Share`) and open the
approval screen. The Dart side is in `mobile/lib/features/share`; the native share-target setup is in
[docs/mobile/share-extension.md](docs/mobile/share-extension.md).

**Demo scan.** Before sign-up, the user scans a sample school notice and sees what Hally finds. If they continue, the sample is
saved as their first capture right after the family is created, still waiting for approval.

## Reminders & background jobs (Sprint 3)

Reminders, the evening prep summary and data retention run as Hangfire jobs. Push goes through FCM once it is configured. See
[docs/mobile/push.md](docs/mobile/push.md).

## Assistant chat (Sprint 3)

Hally answers questions such as "Yarın ne lazım?" using read tools (`get_family_context`, `list_tasks`, `list_events`). The family id is never
a tool argument, so the model can only see the caller's own family. To add something, the model calls `propose_task` or `propose_event`. These are
re-validated like captured content (the server resolves dates from the user's own words) and become **pending proposals**. Nothing is written until
a parent approves. Chats are private to each adult. Token usage is stored per message, so cost can be tracked per family.
The model is `Ai:ChatModel` (`AI_CHAT_MODEL`), or the vision model if that is not set. When no AI provider is configured or it is down, questions about a day
("yarın", "bugün", "bu hafta", "haftaya cuma") are answered straight from the family's data, including "Gerekenler" lists from school notices.

## Helper links & weekly card (Sprint 3)

Grandparents, nannies and drivers don't need the app. A parent shares one task or a whole day through a link
(`/s/{token}`, a small server-rendered page). The link is read-only except for "mark as done", expires in 72 hours by default and can be turned off.
Only the token's hash is stored. The page shows the sharer's first name, never the family name, and is sent with `no-store`, `no-referrer` and `noindex`.

The weekly card is drawn on the server with SkiaSharp and bundled OFL fonts (`backend/src/Hally.Api/Assets/Fonts`). Child names appear only if the user turns them on.

## API

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
| POST | `/api/v1/demo/scan` | Anonymous pre-signup sample scan (`sampleId`, `locale`); nothing stored, rate limited per IP (`Demo:ScansPerHour`) |
| POST | `/api/v1/families/{id}/captures` | multipart: `file` and/or `text`, `source` → proposals |
| POST | `/api/v1/families/{id}/captures/{cid}/retry` | Retry failed extraction |
| GET | `/api/v1/families/{id}/proposals?status=pending` | Waiting for approval |
| POST | `/api/v1/families/{id}/proposals/{pid}/approve` · `/reject` | Approve with optional edits |
| GET/POST/PUT | `/api/v1/families/{id}/tasks` · `/{tid}/complete` · `/reopen` · `/cancel` | Tasks |
| GET/POST/PUT/DELETE | `/api/v1/families/{id}/events` | Events, with conflict detection |
| GET | `/api/v1/families/{id}/today` | Overdue, due today, events, waiting proposals |
| GET/POST/DELETE | `/api/v1/families/{id}/reminders` | Reminders for a task or event (for me or every adult) |
| PUT/DELETE | `/api/v1/me/devices` | Register or unregister a push token |
| GET | `/api/v1/me/notifications` · POST `/read-all` | Inbox (reminders, evening prep) |
| GET/PUT | `/api/v1/me/notification-settings` | Evening prep on/off and hour (16–22) |
| GET/POST/DELETE | `/api/v1/families/{id}/share-links` | Helper links for one task or one day (72h default, max 7 days, revocable) |
| GET · POST | `/s/{token}` · `/s/{token}/tasks/{tid}/done` | Public helper page (no account); mark a shared task done |
| POST | `/api/v1/families/{id}/assistant/messages` | Chat with Hally (read tools + suggestions), rate limited per user (`Assistant:MessagesPerHour`) |
| GET | `/api/v1/families/{id}/assistant/conversations` · `/{cid}/messages` | The caller's own chat history |
| GET | `/api/v1/families/{id}/weekly-summary` · `/card.png` | Week stats and a 1080×1350 shareable card (child names only with `includeChildNames=true`) |

All state-changing POSTs except auth require an `Idempotency-Key` header. Errors are RFC 9457 problem details with a `code` extension.

## Roadmap

- **Sprint 1 – Foundation** (done): solution, auth, families, invitations, design tokens, tr/en, onboarding shell, analytics boundary, CI.
- **Sprint 2 – Capture & AI proposals** (done): demo scan, capture sheet, share extension, extraction, approval sheet, tasks & calendar.
- **Sprint 3 – Assistant & daily loop**: chat, Today, reminders, push, helper share links, weekly card.
- **Sprint 4 – Monetization & beta**: plans, paywall, privacy & account deletion, beta pipeline.
