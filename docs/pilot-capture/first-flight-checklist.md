# Pilot Capture 0.1 – First Flight

This checklist tracks the first usable end-to-end Windows capture build. Status reflects the current bootstrap, not completed product behavior.

## Foundation

- [x] Select .NET 10, Avalonia 12, SQLite/EF Core, and ULIDs.
- [x] Establish solution and project boundaries.
- [x] Define the initial subject, membership, session, image, asset, and audit records.
- [x] Add an initial versioned SQLite schema and first-run database initialization.
- [x] Persist a local installation ID and initialize station `s10`.
- [x] Apply the VolumePilot color system and establish 48-pixel minimum touch targets for the desktop controls.
- [ ] Build and launch on Windows 11 Pro.
- [x] Add a Windows x64 preview-package workflow that records version, commit, build time, and SHA-256.
- [ ] Confirm the preview package restores, publishes, and launches successfully on Windows 11 Pro.

## First Flight workflow

- [x] Import and select a local event.
- [x] Map, preview, and import a CSV roster locally, preserving each source row and spelling.
- [x] Import multiple team/group associations and explicitly confirm any cross-group same-name subject link.
- [ ] Verify the supplied sample roster imports with expected rows and memberships on Windows 11 Pro.
- [x] Start a session with a photographer, workflow profile, and persistent station code.
- [x] Select a rostered membership or create an unidentified subject; resume the active session and selected capture set after restart.
- [x] Connect Smart Shooter 5's local JPEG output folder and ingest stable JPEG files into the selected capture set.
- [x] Copy image files outside SQLite with generated asset-based paths and SHA-256 checksums while preserving source files.
- [x] Store JPEG pixel dimensions when available and show dimensions plus file size in image review for camera-output monitoring.
- [x] Warn when JPEG dimensions vary within a capture set while treating portrait/landscape rotations as the same output size.
- [x] Persist the image asset and capture image association together in SQLite with stable source-path/timestamp duplicate protection; skip files older than the selected capture set.
- [x] Add an audit entry in the same SQLite transaction as each image ingest.
- [x] Show an immediate on-device face crop and thumbnails for portrait workflows; show the full image for action workflows and fall back to the full image if detection fails.
- [x] Implement independent Primary and Banner roles, Reject, and Next subject actions with persistent local state.
- [x] Guard Next against rapid repeat taps and verify roster-order advancement and active-subject recovery in automated tests.
- [ ] Verify saved state after closing and reopening the application.
- [x] Export event image-to-subject associations as CSV, including review roles and missing-file status.

## Release validation

- [ ] Test a clean Windows 11 Pro installation.
- [ ] Review the touch layout at common Windows display scaling settings on the supplied 2-in-1 laptops.
- [ ] Validate portrait face crops and the no-detection fallback with pilot images on Windows 11 hardware.
- [ ] Run an actual multi-team capture offline from setup through export.
- [ ] Verify restart recovery and missing-file reporting.
- [ ] Test the preview package on the target laptops, then choose and package the final installer/update approach.
