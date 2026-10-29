# Immediate-output capture jobs

For Action bursts and delayed transfers, [buffered Action capture](action-buffered-capture.md) supersedes the earlier drain-before-switch rule when SS timing is enabled. Print modes retain their existing boundary behavior.

The current [performance schedule import](schedule-import.md) adds day/hall selection, per-schedule navigation, optional Entry Type mapping and values-only choice labels. It extends and supersedes the earlier unscoped schedule presentation below.

Dennis's hardware feedback and revised requirements, 28 October 2026, replace the assumption that every job should use roster-driven portrait review. The intro screen asks which of five jobs is being shot. These workflows extend the existing local-first SQLite/file architecture; they do not use CSV exports or the portrait role engine.

## Job chooser

1. Print on site Team Only: available.
2. Print on site Team and Individuals: available.
3. Action: available.
4. Leagues and Schools: planned, disabled for now.
5. Individual Teams: planned, disabled for now.

The prior roster workspace remains accessible through a separate compatibility button. Its presence does not mean the two future screens have been designed. Existing job-type numeric values are preserved; Individual Teams is appended as value 4.

## Print on site Team Only

Select a team, then photograph individuals without selecting or completing players. Each JPEG is automatically renamed and delivered to `<chosen team output>/<team>/`. Every photo is independent; no primary, secondary, banner or CSV is created. A replacement shot does not automatically reject another shot. Reject/restore is available from the recent-image list, with a portrait preview and adjustable closeup, without face analysis.

## Print on site Team and Individuals

Select a team. Start with Player 1, photograph as many frames as needed, and press the large Next Player button. Each team's counter is independent and persists between restarts. Used slots are displayed with a checkmark and can be selected again to review or add photos. Advancing an unused slot does nothing. No player folder is created until a photo actually needs that folder.

The first non-rejected shot is the primary. Choosing another primary replaces the previous choice. Rejecting the current primary before completion promotes the first remaining eligible shot. Restoring a rejected shot does not replace a valid primary.

Dennis explicitly chose **release primary on Next Player**, because immediately sending the first shot to a watched folder could let the downstream application process the wrong choice before the photographer changes it.

- Every accepted shot, including the current primary, goes to `<chosen individual output>/<team>/Player N/` as it arrives.
- The primary team copy remains held until Next Player.
- Next Player releases the final primary into `<chosen team output>/<team>/` and appends `_primary` immediately before `.jpg`.
- The primary also remains in the player folder and carries `_Primary.jpg`; non-primary copies have no role suffix. Present individual copies are renamed when their role changes. Consumed copies are not recreated merely to change the filename marker. The team copy retains `_primary.jpg`. This supersedes the earlier interpretation of “all other images.”
- Switching teams completes/releases a used current player before changing attribution. Returning to a team resumes its next unused slot.
- A previously photographed player can be selected again. Additional photos join that player's existing set without changing a valid primary. The operator may explicitly change or reject the primary; a changed team primary waits until the player is completed/left. Files already consumed/printed cannot be recalled.

## Action

Select a team/performance group and leave the hot folder running. Every JPEG is renamed and delivered to `<chosen team output>/<team>/`. There are no player slots, primary roles, CSVs, closeups, thumbnail decoding or previews. The filename/status list allows rejection without decoding image data. Multiple photos per performance are handled continuously; select the next team when ready.

## Rejection and file naming

Rejected copies go to `<parent of chosen team output>/Rejects/<team>/`. Both the individual copy and team copy are removed on rejection, where applicable. An earlier delivered copy is removed only if its bytes still match the stored original's SHA-256. Restoring moves delivery back to its appropriate destination. Files already consumed, printed, edited or imported by external watchers cannot be recalled by Pilot Capture; folder changes only affect files still present there.

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
- Team and Individuals: shoot three frames, change primary, verify no team delivery until Next Player; verify all accepted shots, including the primary, in Player 1, team-copy primary suffix, used-slot indicator and Player 2 after restart.
- Change teams while a JPEG is writing: the boundary must wait; no frame may cross into the new team/player.
- Action: verify many frames route while no preview or face detector runs.
- Disconnect a destination, keep source files intact, reconnect and verify pending files deliver without duplicate completed outputs.
- Exercise the actual downstream watcher against final JPEGs, `_primary` suffixes, rejects and `.partial` exclusion.
- Measure burst throughput and UI response using production JPEG sizes and actual disks/network paths. Automated correctness tests do not establish production throughput or print readiness.

## Hardware correction: shared roster and performance selection

The next hardware report clarified that **all three modes require roster/performance-order import**. “No CSV needed” meant no output CSV, not removing CSV roster input. All three now share Import roster / performance order, per-column optional mapping with sample values, ordered team/performance selection and Next team / performance. Selecting an order column sorts numeric values naturally; without it, first appearance in the file defines order. Full source rows, extra/duplicate headers and the confirmed mapping remain in SQLite. Re-importing preserves existing capture IDs and player counters; unlisted prior teams remain after the newly imported schedule so their photos are not lost. Repeated team names with different performance labels are separate selectable entries with separate numbered performance folders for new captures.

Only Team and Individuals adds a visible grid of **40 numbered player slots under the team name**. Used slots show a checkmark and remain selectable for revisits; the current slot remains active. Unused slots may be selected directly. Moving away from a used current slot releases its primary. Next Player skips slots already photographed. More slots appear if the team exceeds 40; folders still appear only when images need them.

The reported calling-thread error came from reading Avalonia form controls inside a controller background delegate. Form values are now captured as immutable job settings on the UI thread before dispatch. UI regression coverage submits a real configured form, imports a schedule, checks the 40-slot grid and starts the real Action watcher to verify output delivery.

Incoming detection also distinguishes **arrival from camera timestamp**. New jobs record the source versions already present at creation rather than rejecting every file with an older modification date. Newly copied JPEGs can therefore arrive with old camera timestamps and still be processed. The explicit “Also process JPEGs already in the hot folder when starting” checkbox includes pre-existing versions when desired. Existing upgraded jobs retain the old cutoff until that option is selected; recorded source versions always deduplicate. Migration 7 adds the source baseline, persistent schedule order and lossless import history without rewriting captured images.


## Portrait and closeup hardware correction

The fast print screens previously decoded JPEGs directly with Avalonia, bypassing the existing Windows EXIF-aware portrait decoder. Both print modes now decode on a separate background task, respect EXIF orientation and turn unmarked landscape pixel dimensions clockwise into a portrait. Turn 180° corrects a camera mounted in the opposite direction or an upside-down preview. Orientation and crop are display-only: the incoming file, archived original and delivered JPEG bytes/EXIF remain unchanged. Downstream print software must respect that orientation metadata; this change does not recompress or rotate delivered files.

A large closeup samples the already decoded portrait, with no face detector, extra image decode or cropped file. The default is a 3× view centered near the upper middle. Tap the head in the smaller full-photo preview to reposition the closeup; mouse/pen dragging also repositions it. The zoom slider ranges from 1 to 6. Position and zoom carry forward to the next image in that window, allowing quick adjustment for taller/shorter subjects. This is an operator-positioned crop, not automatic face tracking. The unmarked-camera rotation correction also carries forward within the window. Action still performs no preview decoding.

Decode work is serialized separately from capture intake; stale selections are skipped or discarded so an older preview cannot overwrite a newer selection. Full preview and closeup share one bitmap capped at 2400 pixels on its long edge. Real expression-check usefulness and latency need CAPTURE-10 testing with production JPEGs.

Migration 8 adds independent individual-copy delivery acknowledgements. Existing accepted non-primary acknowledgements move into the new field without republishing consumed files. Existing primaries queue only their newly required individual copy; acknowledged team copies stay acknowledged. Failures/restarts retry each pending destination without recreating a successfully acknowledged copy that another watcher has consumed. Reject/restore removes/restores the appropriate copies subject to the existing checksum and downstream-consumption limits.

Hardware checks for this correction:

- Shoot portrait JPEGs from both camera mounting directions. Verify upright preview; test Turn 180° for missing/wrong camera orientation metadata.
- Tap short and tall subjects' heads, change zoom and shoot again. Verify positioning persists and newest shot replaces the full/closeup views together.
- Confirm all accepted player photos, including the primary, appear in the individual folder immediately. Change primary and verify exactly one present individual copy has `_Primary.jpg`, with no consumed file republished just to rename it.
- Confirm only the selected primary reaches the team output on Next Player with `_primary.jpg`.
- Reject a photo with both copies present and verify both are removed and the reject copy appears. A consumed/printed file cannot be recalled.
- Consume one output, disconnect the other destination, reconnect and verify the consumed delivery is not repeated.


## Adaptable CSVs, performance search and numbered folders

The next CAPTURE-10 report requested adaptability to unfamiliar headers, readable review labels, live search and performance-numbered folders.

Import now opens a dedicated CSV mapping window. Each detected column is shown with its original name, a one-based column position and sample values. Duplicate and blank headers are allowed; extra columns beyond the selected header are also available. Choose a logical CSV header record when the file begins with a title/preamble. Quoted multiline cells stay in one logical record. The original raw document, including the preamble, all rows, extra fields and duplicate headers, is preserved alongside the selected header record and mapping.

Each source column can be **Ignore**, **Team name**, **Performance number**, or **Details / division / level**. Known headers suggest a use; every suggestion is editable. Team name and performance number accept at most one column each and are both optional. Any number of detail columns may be included. With no mapped team name, entries are labeled Performance N when an order value exists, otherwise Entry N from the source record number. Without an order column, source first appearance sets order. A mapped but blank team cell is retained in raw data and does not create an empty team. An ignored field remains stored but does not affect selection or duplicate grouping.

Team names and optional detail fields are separate. The identity key includes the performance label and selected detail values, so the same team in multiple divisions/clubs is not collapsed. Search displays performance number, team and labeled details. Typing part of a name, number, club, level or division narrows the list immediately; multiple search words must all match. Typing alone never changes the active capture destination. Select a specific result, or press Enter for a single match. An ambiguous match requires choosing a specific performance. The current active destination stays visible while searching. Next team / performance follows the full schedule, independent of the search filter.

New folders use **performance number + team name**, for example `023_Rockets`. Numeric performance labels are padded to at least three digits; non-numeric labels are sanitized and preserved. Without a performance number, the schedule position is used. Team output, individual output and Rejects use the same performance folder component. A collision adds details, then an ID suffix if needed. Separate performances no longer intentionally share a team folder. The standard player subfolder and primary-copy timing stay unchanged.

Re-import preserves IDs, player counters and photographed performances' existing folder names. No already delivered photo is moved or republished just to adopt the new naming rule. Existing entries with no photographs adopt the new naming on re-import. Existing group-in-name records are matched when possible, retaining their IDs and photographed folder paths. Migration 9 adds the detail text field without rewriting existing delivery state.

The capture layout gives the image list more room and reduces the preview pane. Filenames wrap on their own line; Primary/Rejected and player/sequence/pending status use a separate wrapping line. Selected image status also appears below the closeup. Primary/Reject controls fit at the supported minimum window size. The full-photo tap target is narrower, and the closeup samples a 4:5 portrait area. These are display changes and do not crop delivered files.

Additional hardware acceptance:

- Import a CSV with unfamiliar, blank or duplicate headers; choose uses by sample data. Select a later header record after a title row. Confirm ignored data remains in import history.
- Include club, division and level as Details. Search a partial team name and a partial performance number; confirm all matching divisions appear and typing alone does not change capture attribution.
- Select a result, capture, and confirm numbered folders in team, individual and Rejects output. Re-import after delivery and confirm the existing destination is not reissued.
- At the laptop's normal scaling and minimum supported window size, verify full filenames, Primary/Rejected labels, touch review buttons and the wider portrait closeup remain visible without overlap.


## Selected-player view, revisits and primary markers

The next hardware request supersedes the earlier lock on photographed player slots and the decision to omit an individual primary suffix.

In Print on site Team and Individuals, the image list, full preview, closeup, photo count and image-role actions are scoped to the **selected player on the selected team/performance**. An empty/new player has an empty list and both image views clear. Returning to a photographed player restores all that player's images, including earlier visits and rejected photos; the previous 100-recent-team limit does not apply to this mode. Team Only and Action retain their recent-team lists.

Checkmarked player buttons stay enabled. Selecting one drains incoming files for the previous player, completes/leaves that player and resumes the selected existing player number. It does not create a second identity or automatically change an existing primary. Additional shots append to the same player's set. Next Player completes the current player and skips already photographed slots while moving forward. Player selection and image associations persist across restarts.

Primary selection is allowed for any image belonging to the currently selected player, including an image from an earlier completed visit. Changing primary explicitly overrides the previous role, renames still-present individual copies and holds the new team copy until Next Player or leaving the player. Rejecting the selected player's primary promotes the first remaining eligible image and holds that replacement team copy until completion. Merely reopening a player and leaving without changes does not republish the previous team primary.

The individual primary filename ends in **`_Primary.jpg`**. Other accepted individual images have no role suffix. The team primary retains the existing `_primary.jpg` suffix. Original hot-folder and archived filenames/bytes are untouched. Role-only renames within the individual folder validate ownership and use an atomic rename where the copy still exists. If a watcher consumed it, the acknowledgement adopts the new name without recreating the file. Explicit reject/restore retains its existing semantics. A deliberate new primary may enter the team watcher after completion; it cannot cancel a prior print/import already performed downstream.

Migration 10 queues accepted existing multi-mode primaries for marker reconciliation on the next job scan. It preserves both delivery paths until reconciled and does not republish previously consumed team or individual copies.

Acceptance:

- Photograph Players 1 and 2; switch between them and confirm that each list/preview contains only that player's images. A new player must show no prior-player photo.
- Reopen Player 1, add shots and verify its primary stays selected until explicitly changed. Choose a different old/new shot as primary and check both the suffix and the delayed team copy.
- Verify a single present `_Primary.jpg` copy in each player's individual folder, checkmarked revisit buttons, restart persistence and Next Player skipping used slots.
- Consume a delivery, revisit without changing primary and confirm it is not reissued. Upgrade an existing job and verify marker-only reconciliation renames present copies without recreating consumed copies.


## Filename display/repair and 4:5 closeup correction

The next hardware report still showed a missing primary suffix and requested a 4:5 portrait closeup. The prior image list displayed the original camera filename, which does not change when an individual copy is renamed. Rows now show the acknowledged outgoing individual/reject filename first, the original camera filename on a separately labeled line, and the role/status line. Refresh signatures include delivery paths so a filename-only repair is reflected immediately.

Opening/resuming a print job, selecting a player/performance, or pressing Primary for an already selected primary reconciles the selected player's individual filename. This covers older unmarked delivery acknowledgements and a bare physical copy left behind even when SQLite already acknowledges the marked path. The shared delivery reconciler validates file ownership, renames the existing bare copy, and removes an owned duplicate if both names are present. Consumed copies are not recreated. Repair does not reset completion/release state or republish a team primary. An unavailable destination or differing bytes produces a visible error; originals remain intact. No new database migration is needed for this per-player repair.

The closeup's actual drawing rectangle and sampled crop now use **4:5 portrait** at every viewport size, with centered letterboxing when needed. It remains a display-only crop of the shared decoded portrait.

Acceptance: open an older player and inspect the actual individual output folder for `_Primary.jpg`; compare the filename shown on screen with that file and the separately labeled camera source. Check both a new primary and an existing bare copy, and verify a consumed team/individual copy is not recreated by repair. Confirm the closeup is 4:5 at normal and minimum window sizes.
