# Team Volume

Choose **Team Volume** on the job screen for rostered portrait jobs that will be
processed later. This uses the local portrait workspace, including the image
review, selection roles, originals and job export workflow. It has no print
output folder.

1. Open a roster CSV, name the job and confirm the mapping. Use **Full name** or
   separate **First name** and **Last name** columns, plus **Team / school**.
   With `Name,Grade,School,Team,Role`, the app suggests the full name, team,
   school and grade. School and optional division context distinguish repeated
   team names; grade and role appear with the athlete. Blank-name rows remain
   in the saved import record and can be entered on site once names are known.
2. Start the portrait session and choose the JPEG output folder used by Smart
   Shooter. Choose a team and tap an athlete in its roster, then start monitoring.
   Let Smart Shooter finish sending one athlete's photos before switching.
3. Use **Search all teams** for a partial name and tap the correct school or
   division. Choosing a different team completes the current subject and waits
   for an explicit athlete selection. Use the athlete search to look across
   the entire job; each result shows its team. Selecting a result switches both
   the team and athlete. Photographed athletes remain selectable for more photos
   and review.
4. Use **Add team** to create and select a new team. Enter a school or division
   when another team has the same name. Use **Add new / unrostered subject**
   to add someone to the selected team and start their capture. The original
   CSV stays intact; use **Export new roster** for updated roster data.

The app saves athletes and team memberships in the existing SQLite portrait
model. A roster can contain separate memberships for the same name, and
cross-team identity matches require operator confirmation.
