# Field output changes, November 2026

These decisions follow the event-weekend test and replace earlier Primary naming
and split print-output requirements.

## Filenames and destinations

The operator-facing pose is **Team**, stored internally as IsPrimary for schema
compatibility. New print jobs write **all accepted photos to one team folder**
under the chosen output root. They no longer require a separate individual root.
Rejects use the existing sibling Rejects folder. Existing jobs retain their old
output layout and files to avoid silently moving or republishing consumed output.
Start a new print job to adopt the new layout.

Examples:

- Team Only: `WildCats_10-01_Red.jpg`.
- Team and Individuals: `WildCats_10-01_P01_team_Red.jpg`.
- Team Volume export: `John_Doe_Tigers_Senior_15_10-01_Team_Secondary_Banner.jpg`.

New Action jobs also use short team/station/serial filenames. No role or color
is required for Action. Folder grouping continues to include performance order
and schedule context.

Station is the short station identifier, e.g. 10 from CAPTURE-10 or Pilot Capture
10. New print/Action setup exposes it explicitly. Portrait exports use the
station recorded with each capture session, never the exporting PC's name.
**Use different station numbers on different PCs in a combined job.**
This preview cannot guarantee collision-free names if two PCs use the same
station number or if a cleared/cloned database restarts its local sequence.
An existing file with different content is never overwritten.

Each image gets a persistent local serial: 01–99, A0–AZ, B0–ZZ, then A00 onward,
without wrapping. The serial is allocated across this local installation, not
restarted for every team or export. Repeat exports retain it. File parts use
underscores for spaces; filename role tokens use Team, Secondary and Banner
for Team Volume and lowercase team for print. The misspelled “Seconday” and double
underscore in the request's example are normalized to Secondary and a single
separator. Internal master originals and batch-editing files retain their stable
asset IDs so edited PNG association remains intact. Human-readable filenames
and role tokens apply to normal JPEG/PNG exports; SPA references exactly those
exported filenames.

## Participants and colors

Team and Individuals initially shows 25 player slots, followed by one Head
Coach (HC01), four Assistant Coaches (AC01–AC04), four Managers (M01–M04), and
four Trainers (T01–T04). Add five players on demand. **Next Player** selects
the next unphotographed player and never advances through staff roles. Staff
slots are explicit, reopenable selections with independent Team images.

Both print modes have a swatch dropdown for new photos, default None.
Switching color first drains settled incoming frames under the previous color.
Use “Apply this color to selected photo” to change a captured photo's indicator.
A filename gets only the color name without spaces, preceded by an underscore.
Names and hex colors can be added locally on site. Defaults preserve the
requested spellings, including Auqa. Maroon uses #800000 to correct the
five-digit value supplied in the request. None adds no suffix.

## Team Volume role selection

The first non-rejected image becomes Team. The second automatically becomes
Secondary. Banner is never assigned automatically. Each role still has at most
one image in a player's team membership, and one image can hold all three roles.
Team cannot be toggled off while any eligible photo exists. Secondary and Banner
toggle off when pressed on the selected role image. A removed Secondary stays
off when more images arrive; choosing another explicitly enables it again.
Rejecting the Team image selects another eligible Team image. Historical choices
are preserved; existing Banner assignments are not erased on upgrade.

## Action queue

The file intake queue counts JPEG versions awaiting local archive, including the
current batch. A periodic recovery pass checks files left in the incoming folder
against persisted archive receipts and the job's startup baseline before enqueueing.
Archived photos, including held photos, are not put back on the intake queue.
Pending output copies and held-for-review counts remain separate. The intake count
returns to zero when the known files have been archived. It does not represent
a camera's invisible buffer. Sources are left in place.

Regression coverage exercises output layout, colors, staff/Next Player behavior,
stable export filenames, role toggles, serial rollover, queue recovery and restart.
