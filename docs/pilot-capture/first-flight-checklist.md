# Pilot Capture 0.1 – First Flight

This checklist tracks the first usable end-to-end Windows capture build. Status reflects the current bootstrap, not completed product behavior.

## Foundation

- [x] Select .NET 10, Avalonia 12, SQLite/EF Core, and ULIDs.
- [x] Establish solution and project boundaries.
- [x] Define the initial subject, membership, session, image, asset, and audit records.
- [x] Add an initial versioned SQLite schema and first-run database initialization.
- [x] Persist a local installation ID and initialize station `s10`.
- [ ] Build and launch on Windows 11 Pro.
- [ ] Confirm the agreed package versions restore and publish successfully.

## First Flight workflow

- [x] Import and select a local event.
- [x] Map, preview, and import a CSV roster locally, preserving each source row and spelling.
- [x] Import multiple team/group associations and explicitly confirm any cross-group same-name subject link.
- [ ] Verify the supplied sample roster imports with expected rows and memberships on Windows 11 Pro.
- [x] Start a session with a photographer, workflow profile, and persistent station code.
- [x] Select a rostered membership or create an unidentified subject; resume the active session and selected capture set after restart.
- [x] Connect Smart Shooter 5's local JPEG output folder and ingest stable JPEG files into the selected capture set.
- [x] Copy image files outside SQLite with generated asset-based paths and SHA-256 checksums while preserving source files.
- [x] Persist the image asset and capture image association together in SQLite with source-path duplicate protection.
- [ ] Add audit entries for image ingest.
- [ ] Show immediate face crop and thumbnails for portrait workflows; omit face review for action workflows.
- [ ] Implement Primary, Banner, Reject, and Next review actions.
- [ ] Verify saved state after closing and reopening the application.
- [ ] Export image-to-subject associations for operational review.

## Release validation

- [ ] Test a clean Windows 11 Pro installation.
- [ ] Run an actual multi-team capture offline from setup through export.
- [ ] Verify restart recovery and missing-file reporting.
- [ ] Package a pilot installer and record build/version information.
