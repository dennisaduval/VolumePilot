# Performance schedule import

The operator imports one event schedule, chooses **Day**, chooses **Hall**, and works through that performance list. Team Only, Team and Individuals, and Action share this flow. Search never changes the camera destination by itself. Day/hall changes select the first performance of the chosen schedule through the existing serialized camera boundary. An open writer prevents the switch and restores the actual selection. Restart restores the active performance and its day/hall.

## Standard CSV

Use one row per appearance with a header row. Recommended columns:

| Column | Use | Required |
| --- | --- | --- |
| DAY | ISO date or consistent day label | Only for multiple days |
| HALL | Hall, stage or arena | Only for multiple locations |
| PERFORMANCEORDER | Published order or performance ID | Optional; otherwise source order |
| PROGRAM | Gym, club or school | Optional detail |
| TEAMNAME | Team or routine name | Recommended |
| DIVISION | Division, level or age band | Optional detail |
| ENTRYTYPE | Team / Performance / Break / Awards / Open / Event Break / Lunch | Optional |

Times, city/state, source page and other fields can remain in the CSV with Ignore selected. Time never controls identity or ordering. Retain source-page references when preparing a PDF conversion. ISO dates sort chronologically in the Day choices. Blank day/hall cells mean **Unspecified**; values are not filled down automatically. Include them on every appearance row.

Provider headers need not match this standard. The mapping screen shows actual headers, column positions and samples. Suggestions can be overridden. Day, Hall and Entry Type allow at most one column each; Details allows several. Every mapping is optional. A later logical header record handles files starting with titles or notes. Unknown entry types produce a source-record error. Recognized break/award/open entries stay in raw history and are excluded from capture choices. Repeated header rows are skipped when all mapped cells match their headers.

Before committing, review performance counts per day/hall and sample labels against the source schedule. Preview and import share one parser. Full source cells, preamble, headers, mapping and checksum are retained locally.

## Capture and storage

- Day/Hall selectors appear for grouped schedules. Hall choices depend on Day. Team dropdown, partial search and Next team/performance are scoped to that schedule. Next stops at its end; search does not change Next's ordering.
- Entirely numeric order values sort numerically within each day/hall; otherwise that schedule keeps source order. Repeated team names in different day/hall combinations have separate photos and player slots.
- Choices show performance number, team and detail **values only**, including for older imported jobs. Headers such as `YEAR:` and `CLASS:` stay in stored import details rather than capture labels.
- New grouped folders include day, hall, number and team, for example `2025-01-18_Hall F_002_Pink Lollipops`. Team/individual/reject destinations share this component. Photographed folders stay unchanged. Primary suffixes and completion timing stay unchanged.
- Manually added teams belong to the active day/hall. Older ungrouped performances remain under Unspecified; new imports do not delete them.

Migration 11 adds day/hall fields with empty defaults and a schedule index. Nonempty day/hall values enter the appearance key. Re-importing an unchanged mapped schedule preserves IDs, photos, player roles and photographed paths. Older ungrouped entries are adopted only through the existing unambiguous legacy match. Ambiguous photos are never assigned to a guessed day/hall.

## PDF preparation

The desktop importer accepts **CSV**, not arbitrary PDFs. Normalize provider PDFs before the event and review import counts. The supplied JAMfest example has January 18 and 19, nine day/hall schedules, 1,604 team performances and 170 other entries. Its separately prepared CSV retains all 1,774 entries and source-page references. It is not stored in the public repository, and no universal PDF parser is implied.

See the [synthetic sample CSV](samples/fast-performance-schedule.csv).

## Acceptance

1. Map unfamiliar headers and verify per-schedule counts in the preview. Times can remain ignored.
2. Choose day/hall, search names/numbers and advance. Only that schedule appears; Next stops at its end. Duplicate names remain distinguishable by program/division values without field labels.
3. Capture in different halls/days and confirm separate photos, slots and folders. Revisit, restart and re-import without losing roles or moving prior deliveries.
4. Switch while an incoming JPEG is open for writing and verify the active selection is restored. Check touch controls and the 4:5 closeup on CAPTURE-10.
