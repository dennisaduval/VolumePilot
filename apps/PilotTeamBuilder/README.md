# Pilot TeamBuilder

**VolumePilot — MORE PHOTOS. LESS CHAOS.**

Version **0.1.0.dev0**, foundation milestone TB-001. This is an executable
composition engine and synthetic rendering harness, not a production desktop app.

## Run and test

Python 3.13+ and Pillow 11.3.0 are required. From `apps/PilotTeamBuilder`:

```sh
python -m pip install -e .
python -m unittest discover -s tests -v
pilot-teambuilder --demo 42 --output /tmp/teambuilder-demo
```

Without installation (when Pillow is already installed):

```sh
PYTHONPATH=src python -m pilot_teambuilder.cli --demo 42 --output /tmp/teambuilder-demo
```

The output directory must be new. The harness writes synthetic source PNGs,
placeholder templates, **one 7800 × 2400 JPEG at 300 PPI**, and `layout.json` with
input geometry, corrections, settings, layout, review flags, and stage timings.
It performs no upload, printing, archiving, or hotfolder monitoring.

Use `--rows /path/to/row_counts.csv` to select an external row table.

## Implemented

- Exact supplied CSV distributions for 1–45 athletes; all occupied rows ≤16.
- Height sorting; tallest back row; center-out row ordering; deterministic ties.
- Head-centered spacing, subtle inverted-V formation, median head-based row
  separation, equal-row staggering, outside-first/center-last painting.
- Common capture-coordinate normalization, one global team scale, proportional
  placement within the default 22 × 6-inch area at (1, 0.75) inches.
- Inch-based composition settings and explicit manual row/slot assignment,
  exclusions/restoration, and per-athlete inch offsets in the engine API.
- Case-insensitive `_default` template discovery, duplicate detection, 3:1
  validation, alpha validation, and two-layer decoded cache.
- Serial PNG compositing, rotated identification strip adapted from PanoBuilder,
  verified JPEG publication, source-bound checks, no overwrite, stage timing.
- Conservative face-obstruction review flags and hard failures for missing,
  multiple, low-confidence heads or unknown capture scale/height.

## Architecture and continuation

See [development record](docs/DEVELOPMENT.md) for repository review, source
provenance, extension algorithm, assumptions, validation, and milestone backlog.
No existing Capture, HQ, PanoBuilder, or Hotfolder code is changed.
