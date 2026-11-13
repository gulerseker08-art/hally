# Closed beta

## What the beta workflow builds

`.github/workflows/beta.yml` runs from the Actions tab ("Beta build" → Run workflow) or on a `beta-*` tag:

| Job | Output |
|---|---|
| `api-image` | `ghcr.io/<owner>/hally/api:beta` and `:beta-<run>` |
| `android` | `app-release.aab` as a workflow artifact; uploaded to Play **internal testing** when the Play secrets exist |
| `ios` | Release build without signing (compile check) |

Beta builds always set `HALLY_DEV_SIGN_IN=false` and `HALLY_ENV=beta`.

## Repository variables (Settings → Secrets and variables → Actions → Variables)

| Name | Example |
|---|---|
| `BETA_API_URL` | `https://api-beta.hally.app` |
| `GOOGLE_SERVER_CLIENT_ID` | OAuth web client id used by Google Sign-In |
| `SENTRY_DSN_MOBILE` | Sentry DSN of the mobile project (optional) |

## Secrets (only what you have; missing ones skip that step)

| Name | Used for |
|---|---|
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` | Play upload key. Create once: `keytool -genkey -v -keystore upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload`, then `base64 -w0 upload.jks` |
| `PLAY_SERVICE_ACCOUNT_JSON` | Uploading to the internal testing track. The first upload of a new app must be done by hand in Play Console. |

## App identity

The working name is Hally (trademark check pending). Android `applicationId` and the iOS bundle id are `app.hally`.
Change both before the first store upload if the name changes: `android/app/build.gradle.kts` and
`PRODUCT_BUNDLE_IDENTIFIER` in `ios/Runner.xcodeproj/project.pbxproj`. After the first upload they can't change.

## iOS / TestFlight

Needs an Apple Developer account. Then:
1. Create the App ID `app.hally` with Sign in with Apple and Push Notifications, and the app in App Store Connect.
2. Add an App Store Connect API key and the distribution certificate and profile as secrets, and extend the `ios` job
   with signing (`flutter build ipa --export-options-plist`) and an upload step (for example `xcrun altool --upload-app`).
3. The share extension and push capability need Xcode steps: see `docs/mobile/share-extension.md` and `docs/mobile/push.md`.

## Error monitoring

Sentry is optional and off without a DSN.
- API: `Sentry__Dsn` (plus optional `Sentry__Environment`, `Sentry__Release`, `Sentry__TracesSampleRate`).
- App: `--dart-define=SENTRY_DSN=...`.

Reports carry no PII. There is no user identity, no request bodies, cookies or auth headers, and no screenshots. Helper and invite tokens in URLs are masked.

## Metrics

`GET /api/v1/admin/metrics?days=30` with the header `X-Admin-Key: <Admin:ApiKey>` returns daily event counts and the activation funnel.
The funnel counts distinct families at each step: family created, partner joined, first capture, proposal approved, helper link, paywall seen, trial, subscription.
The endpoint does not exist when no key (24+ characters) is set. It returns aggregates only.
For charts, point Metabase or Grafana at the `analytics_events` table (`name`, `family_id`, `occurred_at`, `properties`).

## Beta checklist

- [ ] API deployed from the `beta` image, with `ASPNETCORE_ENVIRONMENT=Production`, `Database__MigrateOnStartup=true` and all secrets from `.env.example`
- [ ] `ExternalAuth` client ids for Google and Apple
- [ ] AI key and model; `Privacy__AiProviderNoTraining` only if the provider's terms guarantee it
- [ ] Firebase project (push) and store credentials (billing), when available
- [ ] `Admin__ApiKey` for metrics, `Sentry__Dsn` for errors
- [ ] Testers added to Play internal testing / TestFlight
