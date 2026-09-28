# Pilot Capture

## Purpose

Pilot Capture is the desktop, local-first capture application in VolumePilot. It manages event setup, people and team/group associations, photographer-driven subject selection, image intake, review, and reliable local records. The application must remain usable at an event when internet access is absent or unreliable. Cloud synchronization is a later capability and must not be required to capture.

## Decisions carried forward

- The application is a Windows desktop application for the initial release, built with C#/.NET 10 and Avalonia 12.
- SQLite is the local database, accessed through Entity Framework Core.
- IDs are ULIDs. Each installation has a persistent immutable installation ID; capture station codes are `s10`, `s20`, `s30`, and `s40`.
- Roster data and photographer input identify subjects. QR codes and barcodes are not used to identify subjects.
- Filenames are generated automatically from structured records. Filenames are not identity, and image binaries are stored as files rather than database blobs.
- The core relationship is Subject → Membership → Capture Session → Images. Sessions can capture one team or multiple teams/groups, known or unknown subjects, and one or multiple images per subject.
- Sales galleries are outside capture and must remain separate from pricing and cart behavior.
- JPEG is the expected capture format. Camera image-size configuration and monitoring (Large, Medium, Small) belong to a future Job/Capture Profile capability.
- Portrait workflows immediately show a face crop and image thumbnails. There is no face-detection toggle in the capture flow. Face preview does not apply to action workflows.
- OpenCV/ONNX may support later image analysis. NetMQ/ZeroMQ may support later station messaging; filesystem-based handoff remains a fallback. Neither is a 0.1 dependency.

## First Flight: 0.1 scope

### Included

1. Install and run on Windows 11 Pro.
2. Create/select a local event and configure the capture station and photographer.
3. Import an organization roster from CSV and preserve source values without silently changing spelling.
4. Support a multi-team capture workflow, including subject-to-team membership.
5. Select a subject from the roster or create an unknown subject through photographer input; associate capture records with the selected subject and membership where applicable.
6. Ingest JPEGs from the capture workflow and associate image metadata to the active subject/session. Store image files outside SQLite and persist durable relative paths and metadata in SQLite.
7. In portrait workflows, show face crop and thumbnails immediately. Support Primary, Banner, Reject, and Next actions.
8. Resume after application restart without losing event, roster, session, subject, or image associations.
9. Export image-to-subject associations for verification and downstream workflows.

### Explicitly outside 0.1

- Online sales, pricing, carts, payments, and billing.
- Cloud-required operation or synchronization as a prerequisite for capture.
- QR/barcode subject identification.
- Action-photo face cropping.
- Camera image-size control/monitoring, advanced image processing, OpenCV/ONNX inference, and network station messaging.

## Roster CSV mapping guidance

The first supplied sample roster had `NUMBER`, `FIRSTNAME`, `LASTNAME`, `TEAM/SCHOOL`, `SPORT`, and `CLASS` columns. Header names produce editable mapping suggestions; they do not force a schema for every organization. For this shape, the initial suggestions are:

- `FIRSTNAME` and `LASTNAME` for the displayed subject name.
- `TEAM/SCHOOL` and `SPORT` together for the group path.
- `NUMBER` as the membership roster number only. Jersey numbers are not unique subject identifiers.
- `CLASS` retained as a source category. The sample uses it for both student class years and staff labels, so it must not be silently converted to one role or grade field.

The sample has no stable person ID. Exact names that appear under more than one group are flagged as possible matches for operator review, never merged automatically. The import flow must retain the selected mapping and every original source cell so a mistaken suggestion can be corrected without rewriting imported source data.

## Domain model

The initial model separates event/session context, people, their group memberships, capture organization, and image-file metadata:

```text
Event ──< CaptureSession ──< CaptureSet ──< CaptureImage ──> ImageAsset
                         └──< CaptureSet >── Subject
Group ──< Membership >── Subject
CaptureSet ── optional Membership
```

- **Event** is the operational boundary for a photography job.
- **Group** represents a team or other roster grouping within an event.
- **Subject** represents a person or an unknown/unidentified person being photographed.
- **Membership** associates a subject with a group and carries roster identity/number details. A subject can have memberships in multiple groups.
- **RosterImportRow** preserves one original source record per CSV row, including skipped rows and repeated records that resolve to the same membership.
- **CaptureSession** represents a bounded capture workflow at an event and station, with photographer and workflow profile context.
- **CaptureSet** groups one or more images for a subject within a session, may point to the membership used for the capture, and records when that subject's turn is complete.
- **CaptureImage** is a relational record for one captured image, with a pending/accepted/rejected state and independent Primary and Banner roles.
- **ImageAsset** stores file location and integrity/format metadata. Image bytes remain on the filesystem.

All records use ULIDs stored as canonical lowercase text. Relationships are explicit foreign keys. Local event data uses restrictive deletes; a roster removal must not silently erase past capture history. Unrecognized people are represented explicitly, not inferred from a filename.

Roster imports retain every original CSV row as source JSON in a separate `RosterImportRow`, in addition to the import file name and checksum. This includes rows skipped because required name or group values are blank, and repeated rows that resolve to one subject/membership. Parsed display fields are stored for fast selection; the original row preserves spelling, blank values, and unmapped columns for audit and later mapping improvements. Exact-name matches across groups are unchecked review suggestions; only an operator-confirmed cluster shares one subject. All other rows create separate subjects, and roster numbers never identify people.

## Local storage and SQLite

The initial database is `pilot-capture.db` under `%LOCALAPPDATA%/VolumePilot/PilotCapture` on Windows. Captured JPEGs live under a managed media directory, partitioned by event/session and generated asset ID. The database contains relative paths so an event package can be moved or backed up as a unit. No image bytes are placed in SQLite.

SQLite connections enable foreign keys, WAL mode, and a bounded busy timeout. Schema changes are versioned migrations. Multi-record capture and import operations use transactions. Database writes preserve source/import details and timestamps needed for audit and future sync. The local installation ID is created once and never regenerated during ordinary upgrades; station code is separately configurable.

Future sync must use stable IDs, idempotent operations, retryable transfers, conflict visibility, and an audit trail. Those sync mechanisms are not required to complete First Flight.

## Repository layout

```text
apps/
  PilotCapture.Desktop/       Avalonia desktop shell
src/
  PilotCapture.Domain/        Entities and domain rules
  PilotCapture.Application/   Use cases and ports
  PilotCapture.Infrastructure/SQLite, EF Core, local files
tests/
  PilotCapture.Domain.Tests/
  PilotCapture.Infrastructure.Tests/
docs/
  architecture/decisions/
  pilot-capture/
```

## Acceptance checks for First Flight

- A clean Windows 11 Pro install can create a local event and start a capture session without internet.
- A real multi-team CSV roster imports with all rows and the expected membership relationships intact.
- A photographer can select a rostered subject or create an unidentified subject, capture/import multiple JPEGs, and see correct associations.
- Portrait review shows face crop and thumbnails without a toggle; action workflow does not run portrait face review.
- Primary/Banner/Reject updates survive closing and reopening the application.
- Generated image filenames are deterministic enough for operations but identity and relationships remain correct if a file is renamed.
- An export lists image records and subject/session/group associations, including missing or unavailable file states.

## Implementation status

The repository establishes the project structure, domain records, EF Core SQLite mappings, versioned SQL migrations, persistent installation identity, and an Avalonia desktop shell that initializes the local database. Roster intake reads quoted CSV data while retaining original headers and cells, including duplicate or blank headers and rows whose field counts differ. The desktop provides editable column mapping, a 20-row preview, and an import action that saves the event, groups, subjects, memberships, import metadata, and every source row locally. Exact-name cross-group matches remain unchecked until an operator confirms a cluster. Rows with blank name or group values remain preserved in the import record and are counted as skipped.

The capture setup screen selects an imported event and group, records the photographer and station (`s10`–`s40`), and starts a portrait or action session. It can select a roster membership or create an explicitly unidentified subject. The current subject's active capture set is recovered with the active session after restart. Station codes and the Smart Shooter 5 output folder are stored per installation. Pilot Capture polls that folder for new top-level JPEG files, waits for each file to become stable, validates its JPEG header, and copies it into managed media storage without changing or deleting the source. The settled source timestamp is used with the source path for duplicate detection, and files older than the selected capture set are skipped so delayed scans are not assigned to a later subject. Each managed asset has a generated internal ID, checksum, source path, and durable capture-set association. The final externally visible filename convention remains open; the generated media path is storage identity, not subject identity.

The image review tab retains thumbnails and shows a selected JPEG. Portrait sessions use the Windows on-device face detector to show an enlarged crop; action sessions show the full image without face review. If no face is detected, review continues with the full image. Primary and Banner are independent roles, the first imported image is Primary by default, and rejection remains a persistent review state without deleting the image. Next completes the current subject's capture set and advances through the displayed group roster order. Each successful image ingest now writes an audit entry in the same SQLite transaction as its image and asset records. The Capture tab can export all image-to-subject associations for the selected event as CSV, including group, roster, session, photographer, review roles, checksums, managed relative paths, and a live file-exists flag. Windows 11 workflow validation and Windows packaging remain outstanding. See the [First Flight checklist](first-flight-checklist.md) and [face detection decision](../architecture/decisions/0003-windows-local-face-detection.md) for details.

## Build

Requires the .NET 10 SDK. From the repository root, restore and build with `dotnet build VolumePilot.sln`. The SQLite schema checks run with `python -m unittest discover -s tests -p 'test_*.py'`. Windows build and runtime validation are tracked in the First Flight checklist.
