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
4. Use **Add Team** or **Add Player** in the Capture header. The Add Player
   form defaults to the current team and lets you choose any existing team.
   After saving, the team and player are selected for capture.
5. Tap a player in the right-hand roster and use **Edit Player Information**
   at the bottom. Changes keep the player's existing photos and leave the
   original CSV intact. Use **Export new roster** for updated roster data.

The app saves athletes and team memberships in the existing SQLite portrait
model. A roster can contain separate memberships for the same name, and
cross-team identity matches require operator confirmation.
