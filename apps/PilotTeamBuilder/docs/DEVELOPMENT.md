# Pilot TeamBuilder ongoing development record

## 2026-11-26 — TB-001 / 0.1.0.dev0

### Goal and production contract

Build a separate offline production module for hundreds of composite teams.
The final panorama is landscape 26 × 8 inches / 7800 × 2400 at 300 PPI. Left
24 × 8 inches is artwork, right 2 × 8 is a rotated team identification strip.
Only one JPEG is rendered for later upload/sample routing. Athlete placement is
22 × 6 inches, initially x=1–23, y=0.75–6.75 inches (center 12,3.75).
All source photography must remain unchanged. Production defaults should be
easy to use with large previews and saved job/template settings.

### Repository review and provenance

- Reviewed `dennisaduval/VolumePilot` main at
  `01891a60e2417a7e72d8cebdd5b9c51770ea2092`: root README and architecture
  decisions 0001 and 0006. Capture uses .NET/Avalonia, local SQLite, ULIDs;
  HQ integration uses versioned contracts and offline idempotent operations.
- Reviewed `dennisaduval/Pilot-PanoBuilder` main at
  `58da7ef9f18deab9b4b2f79167eb6e313a09ef5e`: pyproject, contribution workflow,
  template catalog, face geometry, image engine, print layouts, model, workflow.
  Its current Python/Pillow baseline is a practical fit for the image engine.
- Reused/adapted PanoBuilder's font fitting and rotated right strip drawing in
  `identification.py`. JPEG encoding retains quality 95, no chroma subsampling,
  and 300-PPI metadata. Its decode cache design informs the two-layer cache.
- PanoBuilder's portrait head normalization and single-best-face detector are
  deliberately not used for team scaling or detection: both conflict with
  preserving physical proportions or identifying multiple heads for review.
- Reviewed local Pilot Hotfolder config, files, samples, and engine conventions:
  per-job folders, staged publication, stable snapshots, SQLite history. These
  are future workflow adapter candidates, not dependencies of the pure engine.
- Repository access uses the connected GitHub interface; direct shell Git/network
  access cannot resolve DNS in this environment. Local files are an additive
  module staged for a feature branch, not a full checked-out VolumePilot tree.
- The recovery ZIP could not be downloaded because of shell DNS restrictions;
  current PanoBuilder source was reviewed directly through GitHub instead.
- The supplied row CSV was read in full from its current source. The packaged
  table preserves all 45 records, including 17=(8,9), 33=(12,11,10),
  39=(14,13,12), 42=(15,14,13), and 45=(15,15,15).

### Architectural decisions

Separate Python package under `apps/PilotTeamBuilder`, with pure serializable
domain geometry, a pure composition module, filesystem/template/render adapters,
and a development CLI. No changes to existing applications or shared runtime
dependencies. Keep future UI, detection, persistence, and routing behind adapters.
No cloud calls or model downloads are required by this milestone.

`source_scale` means extraction pixels per original common capture pixel.
Crop translation alone does not change that scale. A different resize requires
explicit metadata; unknown values block composition. `relative_height` is a
validated ranking measurement supplied by the caller, not inferred from canvas
or alpha height. The detection/metadata adapter must establish both later.

Source coordinates must be EXIF-oriented pixels. Body bounds contain the full
visible silhouette; head bounds include hair. Current head rectangles also serve
as conservative face-protection regions; a later adapter must distinguish
anatomical faces from hair, eyes, shoulders, feet, and reflection boundaries.
Inputs with multiple heads are held for correction, never silently reduced to
the largest face. The renderer verifies source dimensions and alpha bounds.

Normalize source resizing back to capture coordinates, then apply exactly one
team scale to every normalized athlete. Global scale is limited by both full
body bounds and configured placement bounds. Natural starting size uses median
body height only to choose a common group size; it does not rank or individually
resize athletes. Smaller teams remain centered. Group scale is a fit fraction
in (0,1]. Offsets reserve space within the area; manual offsets that escape it
produce explicit review issues. UI controls should use 1/8-inch steps; subtle
V step remains finer (0.025 inch default, 0.25-inch edge cap).

Head spacing is a requested minimum with a safety floor of 1.1 × the widest
normalized head. Vertical row separation is 1.5 × median normalized head height.
Equal-count rows alternate by half a head-spacing step; otherwise rows center.
The whole visible group, including staggering and wide poses, is centered and
scaled together. Exact final head spacing can shrink when the group must fit.

CSV extension above 45: start with the last defined distribution. Add one
athlete to the shortest row, with ties favoring the backmost row. When every row
reaches 16, add a row and evenly redistribute the new total, assigning remainders
back-to-front. Thus 46=(16,15,15), 48=(16,16,16), 49=(13,12,12,12),
65=(13,13,13,13,13). This is a deterministic extrapolation; exact CSV entries
always win. Missing entries below the largest defined count are errors.

Rendering decodes one athlete at a time and caches at most two artwork layers.
Cache ownership is single-worker; parallel team jobs need separate caches and
a bounded queue. Template ratio must be exactly 3:1; lower-resolution templates
are resized for synthetic use. Production preflight should flag upsampling.
Publication stages and fsyncs the JPEG, verifies decoding, then atomically links
it into place without replacement. Filesystems without hard links fail visibly;
network-safe delivery is a future routing concern. Existing files always block
publication, including deliberate rebuilds until versioned destinations exist.

### Validation

- 23 unit/integration/regression tests pass on the current host, Python 3.13.1,
  Pillow 11.3.0. All CSV team sizes plus 46,47,48,49,64,65,100,257 are exercised.
- Mathematical checks: all source-body transforms fit the area, per-athlete
  source normalization followed by common scale, exact row totals/capacity,
  deterministic assignment/order, centering, staggering, V and row separation.
- Regression fixture records the exact nine-athlete rank order and geometry.
- Integration verifies 7800 × 2400 RGB JPEG, 300 PPI, nonblank identification
  strip, foreground layering, unchanged source hashes, no overwrite, cleanup,
  source geometry mismatch rejection, template cache, invalid templates.
- Existing local Pilot Hotfolder suite: 13 tests pass; no source changes there.
- Synthetic 42-athlete full render succeeds with stage timings. This benchmark
  uses small synthetic PNGs and is not a production speed estimate.
- Added GitHub Actions for Python 3.13 on Linux, macOS, Windows; remote results
  are not yet observed. macOS runner testing is not Apple Silicon field validation.

### Known limitations / outstanding work

TB-001 is a foundation milestone, not field-ready software. No production UI,
hotfolder watcher, database, crash recovery, detection model/helper, persistent
detection cache, network router, print deduplication or packaged Mac app exists.
Manual correction data is immutable and serializable, but there is no operator
editor or general-purpose saved-job loader yet. CLI is synthetic-demo-only.

Face preflight uses conservative head-vs-body rectangles; wide body boxes can
create false positives. It reports review issues rather than guaranteeing face
visibility. Renderer can write a flagged draft; no automatic approval/routing
exists. Add silhouette-aware face visibility before any unattended routing.
Do not treat bounding-box fit as proof of photographic quality.

Real cropped/resized input needs original geometry metadata, original captures,
or operator-calibrated capture scale. Current explicit scale/height values are
synthetic/manual and do not claim automatic physical-height measurement.
No Core ML/Metal/Vision acceleration or production benchmark has been attempted.
Template settings, ICC color management and very-long-name font sizing need
production validation. A single enormous PNG can still require substantial
memory; add pixel/memory budgets before accepting untrusted unattended inputs.

### Next independently testable milestones

1. **TB-002 Detection and source manifests:** versioned job/team manifest loader,
   all-head Apple Vision adapter with CPU fallback, alpha/body/hair geometry,
   capture crop/resize provenance, reduced-resolution coordinate mapping,
   confidence flags and content/model-version keyed persistent detection cache.
   Add missing/multiple-head, scale ambiguity and crop metadata fixtures.
2. **TB-003 Preview/review UI:** large panoramic preview, job/queue/status header,
   navy/royal/orange/sky/cloud/instrument palette, Inter fallback; 1/8-inch controls,
   head/height editing, rank/row/slot moves, exclusion/restore, individual offsets,
   saved template overrides, cached-layout rebuild, approve and intentional reprocess.
3. **TB-004 Production:** stable-folder/ready-manifest ingestion, validate every
   image, checksum deduplication, SQLite state/leases and crash recovery, job paths,
   single render with verified multi-destination routing, upload/sample/processed/
   review/log folders, no overwrites, no duplicate printing, interruption and
   disconnected-network fault tests. Idle-time alone is insufficient proof of
   a complete network transfer; support an explicit producer-ready handoff.
4. **TB-005 Field validation:** actual athlete PNGs, matching original captures,
   templates, expected composites; validate height accuracy, alpha-aware face
   visibility, realistic scale, colors, speed/memory on Apple Silicon, extended
   unattended events. Package a signed/notarized Mac application after validation.

Future HQ exchange should include schema revision, job/event/group/subject IDs,
installation identity, immutable source hashes and stable idempotency IDs, with
a local outbox. Do not share HQ database models or make field work cloud-dependent.
