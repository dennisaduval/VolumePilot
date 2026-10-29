# Action: buffered capture and camera timing

This extends Action only. Print-on-site selection, delivery and previews retain their existing behavior.

## Operator flow

1. Import the performance schedule and choose day/hall/team as usual. Set SS's JPEG download directory to the job's incoming folder.
2. In SS 5 Options, enable External API. This preview connects locally to publisher `tcp://127.0.0.1:54543` and request server `tcp://127.0.0.1:54544` (SS defaults). Keep SS filenames unique across cameras and allow JPEG downloads. Network API endpoints/custom ports are not configurable in this preview.
3. Click **Connect SS**, then **Verify cameras (takes test photos)** while the cameras and SS are idle. Each attached camera receives a marked test exposure. Keep lenses pointed appropriately. Verification frames stay archived and never enter team output.
4. Confirm every camera says verified. Start watching. Photograph a performance, then press Next performance after its last exposure and before the next performance's first exposure. Prefer the middle of the 15–30 second gap.
5. Next records the button's timestamp immediately without draining the incoming folder or waiting for archive/output copies. Delayed images continue into the team associated with their capture time, even across several later changeovers.
6. Counts distinguish delivered/pending files and held photos. Camera buffer totals remain unknown; a quiet folder or equal camera counters is not proof that the physical buffer is empty.

Repeat verification is enabled in the labeled checkbox. It takes additional marked test exposures approximately every 15 minutes when all reported cameras are Ready, there have been no reported photo events for five seconds, and SS reports no known camera-resident downloads. It defers while those checks fail. Reconnected/unverified cameras are retried during idle opportunities. Uncheck it to stop automatic test exposures. The explicit verification button remains available. No clock-setting command is sent; the software compensates independently for each camera's full date/time difference, including incorrect years.

All cameras in one job share its active performance. Cameras photographing different performances simultaneously require separate station/job capture contexts; per-camera team selection in one window is not part of this preview.

## Timing contract

SS supplies PhotoKey, CameraKey, full actual PhotoFilename and PhotoDateCaptured. Match files using the actual full path, never an original/computed filename guess. Camera identities distinguish identical camera models. Preserve partial metadata across updates and recover state with Synchronise. Use camera-only heartbeats between full snapshots; event-sequence gaps, missing file metadata and periodic recovery trigger full photo synchronization. Ignore live-view pixels and unrelated SS events. A reused source path, multiple photo identities, or changing camera/path/capture-time metadata holds the photo rather than guessing.

The SS published API documents CheckClocks and SyncClocks, but does not define clock-check measurement response fields/units. CameraDateTimeOffset is described as a synchronization parameter and is not assumed to be a measured error. A successful CheckClocks reply alone never marks a camera verified. The bridge requests CheckClocks for diagnostics, then uses a uniquely marked Shoot request and its actual photo metadata to establish a bounded time sample.

For a successful test, original capture time must arrive within two seconds of the request. The routing offset is the UTC midpoint of that request/observation interval minus the camera's wall-clock capture time. Uncertainty includes half the interval plus one second for timestamp precision. A delayed or failed test leaves the camera unverified; no arrival-lag estimate is used for normal photos. This must be verified on actual SS 5/camera hardware, especially the Canon R6 Mark II, before production acceptance. A camera that omits the requested PhotoOrigin cannot be automatically calibrated with this method.

Samples are per camera and persist as timing history. They apply to captures after verification and expire after two hours unless replaced by another verification. Reconnect/disconnect invalidates future use until reverified. Normal rechecks close the prior interval; a material offset change invalidates its uncertain interval for unassigned images. Already assigned/delivered images are not silently moved. Unobserved manual clock changes between checks cannot be guaranteed detectable immediately; unexpected timing must be reviewed. A computer clock change during a verification invalidates the test.

Resolve the corrected capture interval against saved team changeovers. If its uncertainty crosses different teams, the photo is held. Missing capture time, unknown camera timing, captures before the first boundary, conflicting clock history and missing SS metadata are also held. A nonempty PhotoOrigin is required before automatic release so a partial update cannot accidentally publish a still-unmarked verification frame. Do not retroactively apply a new correction to all old buffered images.

## Queue, storage and recovery

Migration 12 adds opt-in job timing state, changeovers, cameras, samples, SS metadata and durable file receipts. Connecting SS enables it for that Action job and establishes the first boundary; existing FastImages and output acknowledgements remain intact. Existing service-only legacy Action routing remains for compatibility, but the desktop watch control requires buffered routing to be enabled.

Action team-change commands use short independent database scopes. Intake, reject/restore and explicit held-photo release serialize file mutations separately, so they cannot race a delivery acknowledgement while team changes stay responsive. A single worker handles intake in batches of eight. A FIFO file-event queue coalesces path notifications; periodic enumeration recovers missed events. Incomplete files retry at the back of the queue. No preview decoding occurs. Archive copy and SHA-256 happen in one streaming pass after obtaining read access that denies active writers. Receipts persist original source version, archive, hash, metadata identity, routing decision and clock sample. Sources remain unchanged.

Only assigned receipts create FastImages and enter the existing atomic `.partial` delivery/acknowledgement path. Delivery batches are bounded; slow output does not block team selection. Acknowledged consumed files are not reissued after restart. SS metadata and routing continue to recover after temporary disconnects. Held JPEGs remain in the managed archive and can be explicitly assigned from **Review held photos** (multiple selection, all performances with day/hall visible). Manual assignment is a deliberate override and does not alter camera timestamps.

## Protocol sources and acceptance

- [SS 5 External API guide](https://smartshooter.com/v5/external_api.html)
- [Version 5 API schema/documentation](https://github.com/kuvacode/smartshooter-api/blob/v5/external_api.rst)
- [Vendor request construction and default endpoints](https://github.com/kuvacode/smartshooter-api/blob/v5/smartshooter/context.py)

Regression coverage includes 400 late frames from two cameras (one date 400 days off), multiple changeovers, restart/delivery idempotence, open-file changes, ambiguous timestamps, archived holds/manual release, calibration exclusion, rejected slow calibration, reconnect invalidation and real ZeroMQ request/event transport against a simulated SS publisher/server. Simulation proves our protocol handling, not real hardware timing or camera support.

CAPTURE-10 acceptance: enable SS API, connect the R6 Mark II, verify its camera status and excluded test frame, shoot a 200–400-frame burst and change teams while transfers remain. Confirm no cross-team files, UI responsiveness and counts. Repeat with a second camera with an incorrect date, a reconnect, paused/incomplete transfers and a restarted application. Record any missing PhotoOrigin or metadata and actual verification latency. Test the real downstream watcher and touch controls. Retain print-on-site regression checks.
