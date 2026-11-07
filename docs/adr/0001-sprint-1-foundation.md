# ADR 0001: Sprint 1 foundation choices

Date: 2026-11-07 · Status: accepted

## Decisions

1. **Three backend projects (Domain, Infrastructure, Api)** instead of a full Clean Architecture stack. Business rules live in the domain entities; controllers orchestrate. An Application layer can be split out when AI proposal workflows grow (Sprint 2).
2. **Own JWT after external ID-token verification.** Google and Apple ID tokens are verified server-side; the API then issues short-lived access tokens (30 min) and rotating refresh tokens (60 days, hashed at rest). Reusing a revoked refresh token revokes all of that user's tokens.
3. **Dev identity provider** for local work and tests, accepted only when `EnableDevProvider=true` *and* the environment is Development or Testing.
4. **Family isolation through an authorization policy** (`family-member`) evaluated against the `{familyId}` route value on every family-scoped endpoint.
5. **Idempotency via a filter + `idempotency_records` table**, keyed by caller + route + key, storing the response. Known limitation: two truly concurrent requests with the same key can both execute; the second insert fails on the primary key. A row-level reservation step will be added before AI action execution (Sprint 2).
6. **Invitation tokens** are 256-bit random, stored as SHA-256 hashes, expire after 7 days and are single-use.
7. **Analytics boundary** (`IAnalytics`) writes PII-free events to `analytics_events` in the same transaction. A vendor sink can be added behind the interface.
8. **Children store first name only**, no surname, to keep PII minimal for share links and summary cards.
9. **Time zones are IANA ids** validated with `TimeZoneInfo`; Windows ids are rejected. Default `Europe/Istanbul`.
10. **Integration tests use real PostgreSQL** (Testcontainers, or `HALLY_TEST_CONNECTION`), never the EF in-memory provider.

## Deferred

- Worker service (reminders, push) arrives with Sprint 3.
- Azure Bicep templates, golden tests and bundled fonts (Bricolage Grotesque, Figtree) are not in Sprint 1.
- Flutter native folders (`android/`, `ios/`) are generated with `flutter create`; Share Extension and Universal/App Links are configured in Sprint 2.
