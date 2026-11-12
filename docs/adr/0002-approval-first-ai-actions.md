# ADR 0002: Approval-first AI actions

Date: 2026-11-07 · Status: accepted

## Context
Hally turns photos and messages into tasks and events. Models hallucinate dates, follow injected instructions and can repeat items.

## Decision
1. **The model only proposes.** Extraction writes `proposals`, never tasks or events. `ProposalEngine` executes a proposal only when a family member approves it. In one transaction it validates the payload, re-checks that the assignee and child belong to the family, writes the task or event, records the decision and adds an audit entry.
2. **Server-owned dates.** The model must return the source phrase (`dateText`). `RelativeDateResolver` resolves it deterministically against the family-local "today". The model's own ISO date is used only when it cites a phrase and falls inside a sane range. Otherwise the date is marked `needsInfo`. A time is kept only when the source quote contains it.
3. **No model-chosen identities.** Assignees are always picked by the user. Children are matched by first name within the family only.
4. **Exactly-once execution.** Three guards apply: an `Idempotency-Key` on approve, an `xmin` row version on proposals, and a unique index on `tasks.source_proposal_id` and `events.source_proposal_id`. Proposals expire after 7 days. Identical items (same kind, title and date) are not proposed again while one is pending or approved.
5. **Provider abstraction.** `IVisionModel` with an OpenAI-compatible implementation using `json_schema` strict output. Keys exist only in server configuration.
6. **Originals encrypted at rest** with AES-256-GCM behind `IDocumentStore`. Azure Blob can replace the file store later behind the same interface.

## Consequences
- Extraction runs synchronously in the request (up to ~60 s). It moves to a Hangfire job with push notification in Sprint 3.
- Retention cleanup for originals (30 days) lands with the worker in Sprint 3.
