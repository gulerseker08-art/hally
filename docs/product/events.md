# Analytics event dictionary

Events never contain PII: no names, emails, child names or free text. Ids are internal GUIDs, and retention is counted per family.

| Event | Where | Properties |
|---|---|---|
| `onboarding_started` | app | |
| `onboarding_trust_accepted` | app | |
| `onboarding_demo_scanned` | api, app | `count` (app), `locale` (api) |
| `auth_sign_in_started` | app | `provider` |
| `auth_signed_up` / `auth_signed_in` | api | `provider` |
| `family_created` | api, app | `time_zone` |
| `invitation_created` | api | |
| `invitation_shared` | app | `channel` (whatsapp, copy) |
| `invitation_accepted` | api, app | |
| `child_added` | api | |
| `capture_started` | api, app | `source` (camera, gallery, screenshot, share, text) |
| `document_scanned` | api | `type` (image, text) |
| `proposal_shown` | api | `count` |
| `proposal_approved` | api, app | `kind`, `edited`, `count` |
| `proposal_rejected` | api, app | |
| `task_created` / `event_created` | api | `source` (manual) |
| `task_completed` | api | |
| `reminder_created` | api | `target` (task, event), `for_everyone` |
| `reminder_sent` | api | `target` |
| `evening_prep_sent` | api | `tasks`, `events` (counts) |

Planned for later sprints: `onboarding_completed`, `paywall_*`, `share_link_*`.
