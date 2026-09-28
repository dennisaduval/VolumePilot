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

- [ ] Create/select an event.
- [ ] Import a real CSV roster, preserving each source row and spelling.
- [ ] Support multi-team/group association, including a subject with multiple memberships.
- [ ] Select a known subject or create an unidentified subject from photographer input.
- [ ] Connect a photographer/camera workflow and ingest JPEG files.
- [ ] Store image files outside SQLite with generated asset-based paths and checksums.
- [ ] Persist subject, membership, session, image, and audit associations in one recoverable workflow.
- [ ] Show immediate face crop and thumbnails for portrait workflows; omit face review for action workflows.
- [ ] Implement Primary, Banner, Reject, and Next review actions.
- [ ] Verify saved state after closing and reopening the application.
- [ ] Export image-to-subject associations for operational review.

## Release validation

- [ ] Test a clean Windows 11 Pro installation.
- [ ] Run an actual multi-team capture offline from setup through export.
- [ ] Verify restart recovery and missing-file reporting.
- [ ] Package a pilot installer and record build/version information.
