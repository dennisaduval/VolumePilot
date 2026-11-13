# Team Volume

Choose **Team Volume** on the job screen for rostered portrait jobs that will be
processed later. This uses the local portrait workspace, including the image
review, selection roles, originals and job export workflow. It has no print
output folder.

1. Open a roster CSV, name the job and confirm the mapping. Use **Full name** or
   separate **First name** and **Last name** columns, plus **Team / school**.
   With `Name,Grade,School,Team,Role`, the app suggests the full name, team,
   school and grade. School and optional division context distinguish repeated
   team names; grade and role appear with the athlete. Blank-name rows with a team and role are selectable placeholders. Each row
   stays separate. Use **Edit Player Information** when you learn the name.
   For rosters imported before this change, import the CSV into a new job to
   create the coach placeholders.
2. Start the portrait session and choose the JPEG output folder used by Smart
   Shooter. Choose a team and tap an athlete in its roster, then start monitoring.
   Start monitoring before shooting. Team Volume skips JPEGs already in the
   folder at that point. Let Smart Shooter finish sending one athlete's
   photos before switching. End the active session before importing a new job,
   and use a fresh Smart Shooter output folder for each job when possible.
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

To clear this workstation, return to the job chooser and select **Reset local
Pilot Capture data**. Read the warning and type **DELETE LOCAL DATA**. This
deletes every local job, roster, assignment, setting and stored photo copy in
the Pilot Capture data folder. The app closes; reopen it to start fresh.
Files in external Smart Shooter, export and master folders are unaffected.
