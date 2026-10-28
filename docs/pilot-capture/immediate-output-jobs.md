# Immediate-output capture jobs

Dennis's hardware feedback and revised requirements, 28 October 2026, replace the assumption that every job should use roster-driven portrait review. The intro screen asks which of five jobs is being shot. These workflows extend the existing local-first SQLite/file architecture; they do not use CSV exports or the portrait role engine.

## Job chooser

1. Print on site Team Only: available.
2. Print on site Team and Individuals: available.
3. Action: available.
4. Leagues and Schools: planned, disabled for now.
5. Individual Teams: planned, disabled for now.

The prior roster workspace remains accessible through a separate compatibility button. Its presence does not mean the two future screens have been designed. Existing job-type numeric values are preserved; Individual Teams is appended as value 4.

## Print on site Team Only

Select a team, then photograph individuals without selecting or completing players. Each JPEG is automatically renamed and delivered to `<chosen team output>/<team>/`. Every photo is independent; no primary, secondary, banner or CSV is created. A replacement shot does not automatically reject another shot. Reject/restore is available from the recent-image list, with a small selected-image preview and no face analysis.

## Print on site Team and Individuals

Select a team. Start with Player 1, photograph as many frames as needed, and press the large Next Player button. Each team's counter is independent and persists between restarts. Used slots are displayed with a checkmark and cannot be selected for reuse. Advancing an unused slot does nothing. No player folder is created until a photo actually needs that folder.

The first non-rejected shot is the primary. Choosing another primary replaces the previous choice. Rejecting the current primary before completion promotes the first remaining eligible shot. Restoring a rejected shot does not replace a valid primary.

Dennis explicitly chose **release primary on Next Player**, because immediately sending the first shot to a watched folder could let the downstream application process the wrong choice before the photographer changes it.

- The current primary remains in managed local storage until Next Player.
- Non-primary shots go to `<chosen individual output>/<team>/Player N/` as they arrive.
- Next Player releases the final primary into `<chosen team output>/<team>/` and appends `_primary` immediately before `.jpg`.
- The primary is not duplicated into the player's individual folder. This follows “all other images” in the supplied requirements. If the intended sales workflow needs every shot there, that is a separate routing change.
- Switching teams completes/releases a used current player before changing attribution. Returning to a team resumes its next unused slot.
- Completed players' primary choices are locked to prevent duplicate printing. Their images may still be rejected/restored, but that cannot undo downstream processing already performed.

## Action

Select a team/performance group and leave the hot folder running. Every JPEG is renamed and delivered to `<chosen team output>/<team>/`. There are no player slots, primary roles, CSVs, closeups, thumbnail decoding or previews. The filename/status list allows rejection without decoding image data. Multiple photos per performance are handled continuously; select the next team when ready.

## Rejection and file naming

Rejected copies go to `<parent of chosen team output>/Rejects/<team>/`. The earlier delivered copy is removed only if its bytes still match the stored original's SHA-256. Restoring moves delivery back to its appropriate destination. Files already consumed, printed, edited or imported by external watchers cannot be recalled by Pilot Capture; folder changes only affect files still present there.

Names include the sanitized team name, player number where applicable, a per-team capture sequence and an immutable image ID. The ID avoids collisions across jobs/stations and camera filename reuse. Camera source files remain untouched. Existing different destination bytes are never overwritten. JPEG extensions remain JPEG; these fast modes do not convert to PNG.

## Local durability and speed

Migration 6 introduces fast jobs, teams and images in the existing local SQLite database. Records persist source path/version, sequence, player/primary/rejection state, original checksum, local archive and delivery status. Original JPEGs are kept under the managed `media/fast/<job ID>` directory. The normal original/edited export workflow remains separate from these immediate routing jobs.

The fast controller runs on a background thread with a separate DI/database scope per operation. It serializes scans, team switches and player boundaries so UI rendering cannot delay intake or change an in-flight batch's attribution. FileSystemWatcher wakes it on incoming changes; a two-second rescan recovers missed events, with quicker retries for unsettled files. Known source versions are loaded as one set per pass rather than issuing one query per existing photo. Only deliveries marked pending are reconciled. There is no artificial 500 ms settling delay per image, face detection, roster search or complete-thumbnail-strip rebuild on this path. A single selected-image preview is refreshed independently for print jobs. Action skips decoding altogether.

An incoming file must be opened without sharing writes before it is copied. If a writer is still open, the file is retried and Next Player/team switching waits rather than assigning that image to the next player. Files not yet created by the camera application cannot be attributed in advance: wait for the incoming image/count before advancing.

Deliveries are written as `.partial` and renamed to their final `.jpg` only after copying finishes. Downstream watchers must watch final JPEGs and ignore `.partial` files. Output, source, individuals and Rejects directories cannot overlap, including recursive containment. Create folders for a dedicated job and keep the source hot folder separate from watched destinations.

New jobs record the JPEG source versions present at creation and ignore those versions unless explicitly included. Later arrivals are accepted even if they retain an old camera timestamp. Older upgraded jobs use their previous time cutoff until Include existing is selected. Paused monitoring stops the background loop; Next Player/team switching explicitly drains outstanding files before completing a boundary. Completed delivery records are not republished merely because a downstream watcher moved a file away. Pending copies retry after output errors or restart. As with any filesystem handoff, a crash between final rename and SQLite acknowledgement can cause a retry; downstream consumers should use the stable output name as their idempotency key.

## Hardware acceptance

- Start each of the three modes without importing a roster; verify five intro choices and future labels.
- Team Only: shoot a burst, verify per-team files without operator advancement, then reject/restore.
- Team and Individuals: shoot three frames, change primary, verify no team delivery until Next Player; verify only non-primary shots in Player 1, primary suffix, used-slot indicator and Player 2 after restart.
- Change teams while a JPEG is writing: the boundary must wait; no frame may cross into the new team/player.
- Action: verify many frames route while no preview or face detector runs.
- Disconnect a destination, keep source files intact, reconnect and verify pending files deliver without duplicate completed outputs.
- Exercise the actual downstream watcher against final JPEGs, `_primary` suffixes, rejects and `.partial` exclusion.
- Measure burst throughput and UI response using production JPEG sizes and actual disks/network paths. Automated correctness tests do not establish production throughput or print readiness.

## Hardware correction: shared roster and performance selection

The next hardware report clarified that **all three modes require roster/performance-order import**. “No CSV needed” meant no output CSV, not removing CSV roster input. All three now share Import roster / performance order, editable team/group/order column mapping, ordered team/performance selection and Next team / performance. Selecting an order column sorts numeric values naturally; without it, first appearance in the file defines order. Full source rows, extra/duplicate headers and the confirmed mapping remain in SQLite. Re-importing preserves existing capture IDs and player counters; unlisted prior teams remain after the newly imported schedule so their photos are not lost. Repeated team names with different performance labels are separate selectable entries and share the team's output folder.

Only Team and Individuals adds a visible grid of **40 numbered player slots under the team name**. Used slots show a checkmark and cannot be selected again; the current slot remains active. Unused slots may be selected directly. Moving away from a used current slot releases its primary. Next Player skips slots already photographed. More slots appear if the team exceeds 40; folders still appear only when images need them.

The reported calling-thread error came from reading Avalonia form controls inside a controller background delegate. Form values are now captured as immutable job settings on the UI thread before dispatch. UI regression coverage submits a real configured form, imports a schedule, checks the 40-slot grid and starts the real Action watcher to verify output delivery.

Incoming detection also distinguishes **arrival from camera timestamp**. New jobs record the source versions already present at creation rather than rejecting every file with an older modification date. Newly copied JPEGs can therefore arrive with old camera timestamps and still be processed. The explicit “Also process JPEGs already in the hot folder when starting” checkbox includes pre-existing versions when desired. Existing upgraded jobs retain the old cutoff until that option is selected; recorded source versions always deduplicate. Migration 7 adds the source baseline, persistent schedule order and lossless import history without rewriting captured images.
