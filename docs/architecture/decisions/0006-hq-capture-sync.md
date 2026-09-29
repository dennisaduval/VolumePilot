# ADR 0006: HQ and Pilot Capture synchronization boundary

- Status: Accepted
- Date: 2026-09-28

## Context

Pilot Capture is local-first and must remain operational without an internet connection. HQ is the central system of record for company, client organization, Job, Event, roster, and Capture configuration data. The applications need reliable exchange without sharing a database or requiring continuous connectivity.

Pilot Capture already uses ULIDs, preserves local roster sources, distinguishes Subject from Membership, and records Event, CaptureSession, CaptureSet, and image metadata.

## Decision

- HQ and Pilot Capture communicate through versioned public API contracts. They do not share a database or persistence entity assembly.
- HQ publishes immutable, versioned Event configuration snapshots. A snapshot includes its HQ Event ID and revision, the assigned groups, subject IDs, memberships, capture profile references, and required operational settings.
- A Capture installation stores its assigned snapshot locally. Offline operation continues against the installed snapshot.
- Capture-generated records keep their ULIDs and include the originating installation ID, HQ Event ID, configuration revision, and relevant subject/membership IDs.
- Capture creates a durable local outbox. Each sync batch and each operation has a stable idempotency ID.
- HQ records accepted operations and results. A retried operation returns its prior result and does not create a duplicate.
- HQ validates company, Event assignment, referenced IDs, and payload version before acceptance. Invalid records receive explicit errors that can be reviewed and retried after correction.
- Capture history remains associated with the IDs and revision used at capture time. A later roster or configuration update does not rewrite historical capture context.
- New or unidentified subjects created offline arrive as separate records. HQ may suggest possible duplicates to an operator; names alone never merge identities.
- Sync initially transfers structured records and image manifests only. JPEG binaries remain local. A later media-transfer design can use a resumable upload to object storage with checksum validation.

## Consequences

- Capture does not lose work during an outage and can retry safely after connectivity returns.
- HQ can show per-installation sync status and per-batch validation results.
- Versioned snapshots keep offline operations explainable after HQ data changes.
- API contract versioning, operation payload shape, batch limits, retry policy, authentication flow, and retention of sync logs must be specified before sync implementation.
- Capture and HQ require compatibility tests around shared ULIDs, Event identity, membership references, and configuration revisions.

## Revisit when

Field testing exposes a concrete concurrency or transfer requirement that needs a different operation or media-transfer protocol.
