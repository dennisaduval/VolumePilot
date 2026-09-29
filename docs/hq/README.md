# VolumePilot HQ

## Purpose

VolumePilot HQ is the central cloud application and system of record for photography companies. It prepares and coordinates work for Pilot applications. Local event applications remain able to operate without a continuous connection to HQ.

This document defines the first HQ release boundary and the architecture that will guide implementation. It complements the platform overview in the repository root and the Pilot Capture decisions in `docs/pilot-capture/README.md`.

## MVP outcome

A photography company can create and prepare work in HQ, run that work in Pilot Capture while offline, and synchronize structured session results back to HQ.

### Included

1. Create a company account, invite staff, and assign initial roles.
2. Create client organizations and configurable organization-unit hierarchies.
3. Create a Job that can contain one or more Events.
4. Assign groups, subjects, memberships, schedules, and capture stations to an Event.
5. Import a roster through a reviewable mapping flow that preserves the source file and source rows.
6. Build and publish a versioned Capture configuration for an Event.
7. Assign Capture installations to an Event and show their last sync and configuration version.
8. Receive capture/session metadata and new or unidentified subjects from Capture.
9. Review validation issues, possible duplicate subjects, and sync status.
10. Export a revised roster separately from the original import.

### Outside the first HQ release

- Customer-facing galleries and proofing
- Products, packages, orders, discounts, payments, and billing
- Image processing, printing, and fulfillment
- Uploading full JPEG files to cloud storage
- Advanced workflow builders or a generic role-permission editor

The first sync will include image manifests and metadata needed for traceability. Pilot Capture retains local image files. A later media-transfer feature can upload JPEGs to object storage without changing capture identity or association records.

## Core workflow

```text
Company and staff
  -> client organization
  -> Job
  -> Event, groups, subjects, memberships
  -> roster import and review
  -> versioned Capture configuration
  -> assigned Capture installations
  -> offline sessions and captures
  -> idempotent sync to HQ
  -> HQ review and separate roster export
```

An Event configuration is a versioned snapshot. A Capture installation can continue using its assigned snapshot offline. When HQ publishes a later revision, Capture can receive it when online. Capture history records the revision and stable entity IDs that were actually used.

## Domain vocabulary

- **Company account / tenant:** A photography company using VolumePilot. Tenant-owned data is isolated from every other company.
- **Client organization:** A school, league, club, business, or other customer of a photography company.
- **Organization unit:** A configurable node in a client structure, such as campus, program, sport, division, or team.
- **Job:** The booked body of work for a client. It may contain one or more Events.
- **Event:** A specific operational occurrence, with a date or date range and location. It is the HQ boundary assigned to Pilot Capture.
- **Capture session:** A bounded capture workflow at an Event, associated with a station and photographer.
- **Group:** A team or other grouping relevant to an Event.
- **Subject:** A person record owned by one company account. A subject may have multiple group memberships.
- **Membership:** The relationship between a Subject and a Group, including roster-specific details.
- **Capture installation:** A registered Pilot Capture installation. Its immutable installation ID and station identity are distinct from a human user.

The existing Pilot Capture local Event maps to one HQ Event. A Job can group multiple HQ Events. This preserves the Capture app's established operational boundary.

## Capture workflow configuration

Capture behavior is represented by configuration values and profiles, not by a fixed set of job types. The first release must cover:

- One or multiple groups in an Event
- Known rostered subjects, photographer-entered subjects, and explicitly unidentified subjects
- One or multiple images per subject
- Portrait or action capture context
- One or multiple assigned stations
- Required or optional subject/group associations

Subjects and memberships use ULIDs, matching the current Pilot Capture foundation. Names, roster numbers, and filenames are data or display values; they are never primary identity keys.

## Synchronization boundary

- HQ publishes a versioned Event configuration containing only the data needed by assigned Capture installations.
- Capture stores that configuration locally and remains fully operational without HQ.
- Capture creates local records with stable ULIDs and queues sync work durably.
- HQ accepts a batch idempotently. Retrying an already accepted batch returns its prior result.
- Sync results include accepted records and actionable validation errors. HQ does not silently discard a record.
- An offline Capture record retains the configuration revision, subject ID, membership ID, and group context used at capture time.
- New offline subjects arrive as distinct records for review. Names alone never cause automatic merging.
- The first HQ increment receives structured capture/session records and image manifests. Full image bytes remain local until a separate upload feature is designed.

Transport and payload schemas will be defined in a later implementation decision before sync code is written.

## Initial application shape

HQ is a browser-based application backed by an ASP.NET Core API. It starts as a modular monolith with clear internal boundaries for:

- Accounts and access
- Client organizations
- Jobs and Events
- Subjects, memberships, and roster imports
- Capture configuration and device assignments
- Synchronization and audit history

The initial relational system of record is PostgreSQL. The first deployment target follows the established DigitalOcean setup. The application tier remains stateless; persistent data lives in managed database and, when introduced, object storage services.

Pilot Capture and HQ communicate through versioned API contracts. They do not share persistence entities or access each other's databases.

## Security and integrity requirements

- Every tenant-owned record is scoped to a company account.
- Access is checked using the authenticated user's company membership and role.
- Capture installations have their own identity and event assignment; they do not use a photographer's interactive login as a device credential.
- Tenant boundaries are enforced in query paths and relational constraints and verified with cross-tenant tests.
- Material edits, imports, exports, configuration publications, sync attempts, and subject merges are attributable to a user or device and timestamped.
- Destructive removal is avoided for records referenced by capture history. Use status changes or explicit audited corrections.
- Data retention, deletion, and customer export policies must be defined before broad commercial onboarding.

## First HQ screens

1. **Home / work queue:** upcoming work, Events needing setup, sync attention.
2. **Organizations:** client organizations and their unit hierarchies.
3. **Jobs:** booked work and linked Events.
4. **Event workspace:** roster, groups, stations, schedule, and Capture readiness.
5. **Roster import:** source file, column mapping, validation, review, and import history.
6. **Capture setup:** configurable profiles and published configuration revisions.
7. **Devices and sync:** device assignments, last contact, configuration revision, pending sync, and errors.
8. **Subjects:** tenant-owned directory, memberships, and review queue for new or possible duplicate records.

The UI uses the VolumePilot brand system: Navy and neutral working surfaces, Royal Blue for interaction, and Flight Orange for deliberate emphasis. Use standard software labels when aviation terms would slow comprehension.

## First release acceptance

- A company can create a Job with multiple Events and assign people, groups, and stations.
- A roster import preserves every source row and does not overwrite the source file.
- Capture can receive a published Event snapshot and use it offline.
- Known, partially identified, and unidentified subjects can be represented using stable IDs.
- A Capture installation can retry sync without duplicating accepted records.
- A changed roster or configuration does not rewrite the historical context of captures made on an older snapshot.
- HQ exposes sync outcomes and review work instead of hiding conflicts.
- Tenant boundary checks prevent access to another company's records.
