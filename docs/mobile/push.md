# Push notifications

Reminders and the evening prep summary are always written to the in-app inbox (`GET /api/v1/me/notifications`).
Push is an extra channel on top of that, sent through Firebase Cloud Messaging (FCM HTTP v1, which also reaches iOS through APNs).

## Server

| Setting | Meaning |
|---|---|
| `Push:FcmProjectId` | Firebase project id. If it is empty, pushes are only logged (no content in the log). |
| `Push:FcmServiceAccountJson` | Service account JSON with the *Firebase Cloud Messaging API* role. **Secret:** set it through the environment (`Push__FcmServiceAccountJson`) and never commit it. |

Tokens that FCM reports as unregistered are deleted automatically.

## Background jobs (Hangfire, PostgreSQL storage, schema `hangfire`)

| Job | Schedule | What it does |
|---|---|---|
| `reminders` | every minute | Sends due reminders. A reminder for a task that is done, or for a deleted item, is cancelled. Reminders more than 12 hours late are cancelled instead of sent. |
| `evening-prep` | every 15 min | At each adult's chosen hour (16–22, family time zone), sends a summary of tomorrow. Nothing is sent for an empty day. |
| `retention` | 03:30 daily | Deletes capture originals after `Storage:RetentionDays` (default 30) and keeps the extracted data. Also prunes old idempotency records, expired refresh tokens, and notifications older than 90 days. |

Every job is safe to rerun: each inbox entry has a unique dedup key. Set `Jobs:Enabled=false` to run the API without the job server, as the tests do.

## App

After `flutter create` adds the native folders:
1. Add `firebase_core` and `firebase_messaging`, then run `flutterfire configure`. The generated `google-services.json` and `GoogleService-Info.plist` are not secrets, but keep them out of public repos.
2. After sign-in and on every token refresh, call `PUT /api/v1/me/devices` with `{ "platform": "android" | "ios", "token": "<fcm token>" }`.
3. On sign-out, call `DELETE /api/v1/me/devices?token=<fcm token>`.
4. Tapping a push carries `kind`, `targetKind` and `targetId` in `data`: open `/tasks` or `/calendar`.
