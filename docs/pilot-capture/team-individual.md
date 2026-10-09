# Team and Individual Photoday

Status: implementation for the next Windows preview; CAPTURE-10 acceptance testing is pending.
Source: Dennis's capture-screen sketch and requirements, 9 October 2026. This document supersedes the earlier Primary/Banner-only review behavior. The local-first architecture, stable IDs, lossless roster source retention, and separate HQ responsibilities remain in force.

## Job models

The domain records four job types: Team and Individual Photoday; Print on site teams only; Print on site team and individual; Action Photography. This increment implements Team and Individual. Dedicated printing and action screens, processing rules, and fulfillment are deferred. The older Portrait/Action session profile remains separate from job type.

A job contains one or more teams. Processing occurs later in third-party software. “Team Photo Images” means primary individual portraits for team assembly, not group photographs.

## Capture screen and selection

The supplied sketch is the layout reference: large portrait expression closeup on the left; league/division, searchable team and athlete selection on the right; photographed/total count; complete-subject action; horizontal portrait filmstrip below; large Primary, Secondary, Banner, and Reject buttons across the bottom.

Dropdowns sort alphabetically. Mapping choices keep their original source column indexes when sorted. Search above the selection controls begins on partial input, searches names, numbers and teams across the current job, narrows as more words are typed, and includes membership context for ambiguous names. Team selection can be filtered by league and partial team name. Name choices display a photographed indicator. A photographed membership means at least one captured image, including subsequently rejected images.

Selecting another athlete completes the current visit. Complete subject chooses the next unphotographed athlete in the same team in alphabetical display order, wraps once, and leaves no active subject if none remain. Completing without taking a photo does not mark an athlete photographed. Adding partially identified or unidentified subjects remains supported; imported files are never rewritten.

Each visit remains a CaptureSet for audit and intake timing. Review spans all visits to the athlete's membership, including other sessions. Re-selecting an athlete restores the filmstrip and latest closeup. New images append. Selection does not depend on filenames or names matching.

## Image roles

Dennis explicitly confirmed that an athlete's different team/sport memberships have independent primary, secondary and banner choices. For subjects without membership, choices are scoped to the subject ID.

- First eligible image automatically receives all three roles.
- Second eligible image becomes secondary; the first remains primary and banner.
- Later images do not replace existing choices.
- Each role has exactly one image whenever there is an eligible image; if all images are rejected, there are no role selections.
- Primary and secondary differ whenever two or more non-rejected images exist. When only one eligible image remains, it carries all three roles.
- Choosing primary/secondary replaces the previous choice; selecting the other role's image swaps the two so they remain distinct.
- Banner is a single selection and may share primary or secondary. Pressing Banner selects it; it does not clear it.
- Reject toggles rejection. Rejected photos retain their records/files but hold no roles. Missing roles are filled in capture order from eligible images. Restoring an image does not displace valid choices.

SQLite migration 5 adds a membership/subject selection scope and secondary role, with unique partial indexes for all three roles. Existing repeated-visit choices retain the earliest selected primary/banner, and missing roles are filled. Role updates clear old flags and assign new flags inside one transaction to satisfy SQLite's immediate uniqueness checks.

## Portrait display

The latest imported image is selected automatically. Both thumbnails and closeup respect EXIF orientation; landscape presentations rotate clockwise into portrait. Rotation and crops affect display only. The existing on-device face detector produces a portrait head crop for expression review. If detection finds no face or is unavailable, show the complete portrait. Originals and edited files retain their bytes. Horizontal scrolling has a visible scrollbar and uses the framework's touch panning; touch behavior and rotation direction require CAPTURE-10 verification.

## Media storage and processing

SQLite and staged originals remain on the capture laptop so server/network availability is not required to photograph. The operator configures the fixed master VP database folder in Session & files. It may be a mounted server share or a directory on the master PC. SQLite is not moved onto that share.

Master paths:

- `Original Images/<job ID>/<image asset ID>.jpg`
- `Extracted Images/<job ID>/<image asset ID>.png`

The stable master filename is deliberately separate from the original camera filename, which remains in SQLite. This prevents camera filename reuse between stations/jobs and makes extension-only derivative matching unambiguous. Image asset IDs do not change on export. Publish originals copies every captured original, including rejected images; capture automatically attempts publication after ingest when a master folder is configured. Failures preserve local intake and report that publication is pending. Publish originals / retry completes copies after an outage. Existing master originals must match the recorded SHA-256 and are not overwritten. Local files are retained; no automatic deletion is included.

For batch editing, choose **Batch editing · original photos**. The resulting job folder contains `original photos` with all non-rejected JPEGs using stable asset-ID stems. Keep these stems through third-party processing and put resulting PNGs in the master job's Extracted Images folder. Associate edited PNGs validates the PNG signature and records the path, SHA-256 and association time against the original asset. Missing/unmatched PNGs are not guessed by athlete names. Re-running association refreshes links/checksums after edits.

## Final export

Choose Original JPEGs or Edited PNGs, then a destination. A new job folder is created; existing exports are not overwritten. Edited exports preflight every eligible image and fail before writing if any derivative is missing or has changed since association. No silent original substitution occurs. Partial failed exports are cleaned up.

Export hierarchy:

- Job folder
  - Team Photo Images
    - Team folder: the primary individual photo for each photographed membership, with SPA.csv
  - All Images
    - Team folder: all non-rejected photos, with SPA.csv

Ungrouped subjects use Unassigned. Team-folder and photo-name collisions are resolved with numeric suffixes. Filename characters invalid on Windows, reserved device names, and traversal characters are sanitized. Human-readable final filenames use `FIRSTNAME_LASTNAME_TEAMNAME_NUMBER.png`, or CLASS when TEAMNAME is unavailable; omit missing parts. Multiple images/duplicate names get `_02`, `_03`, etc. Original export uses `.jpg`, with actual JPEG bytes. SPA references the actual delivered filename, including `.jpg` for original exports and `.png` for edited exports; an original is never mislabeled PNG. Primary copies use the same filename in both collections.

Each team collection has one CSV row for each image actually delivered. SPA is always present. Available roster/job fields are included in this order: FIRSTNAME, LASTNAME, NAME, NUMBER, POSITION, TEAMNAME, LEAGUENAME, SCHOOLNAME, CLASS, YEAR, SPATEXT1, SPATEXT2, SPATEXT3, SPATEXT4, SPATEXT5. Values are CSV-escaped and UTF-8. Source text, leading-zero numbers, position, school and class stay separate. New roster exports also retain these canonical fields alongside stable VolumePilot IDs.

The importer recognizes these headers ignoring spacing/punctuation/case, plus CLASSNAME as CLASS. Operator-confirmed name/number mappings populate canonical values; every original source cell, including duplicate headers and skipped rows, is retained unchanged. Duplicate canonical header names use the first matching column for export; source JSON retains all occurrences for later explicit remapping.

## Acceptance checks on CAPTURE-10

1. Start the preview; resume an existing database and confirm migration preserves subjects/images.
2. Import a roster with the SPA fields; verify team/name alphabetical ordering and partial job-wide search.
3. Capture one, two and four portraits; verify defaults, swaps, banner replacement, multiple rejections and restore.
4. Switch away/back and restart; verify old images/choices, append without replacing choices, and photographed count.
5. Complete an athlete with later photographed athletes in the team; verify skip and wrap, then no active subject when all are photographed.
6. Test portrait EXIF, landscape orientation, closeup alignment, scrollbar and finger panning at laptop display scaling.
7. Disconnect the master share, capture locally, reconnect and publish; verify every master original and local original remains intact.
8. Export batch originals; process to same-stem PNGs; associate; export final hierarchy and import SPA.csv into SPA.
9. Check multiple photos, duplicate names, reserved/invalid filename characters, missing derivatives and changed PNGs.

This preview does not implement a Windows installer, background station synchronization, automated image extraction/editing, or print fulfillment. Master copying currently shares the capture-operation queue; verify its throughput on the actual server connection before production use.
