# Windows Pilot Preview Package

The **Pilot Capture Windows Preview** workflow builds a self-contained Windows x64 package so a pilot laptop does not need the .NET SDK installed. The workflow runs for pull requests that change the desktop application or its dependencies, and can also be started manually from the repository's Actions page.

The workflow publishes the desktop project in Release mode for `win-x64` with the .NET runtime included. It uploads a ZIP archive, a SHA-256 checksum file, and a `BUILD-INFO.txt` file containing the preview version, source commit, and build time. The workflow artifact is retained for 14 days.

## Running the preview

1. In GitHub, open **Actions** and select **Pilot Capture Windows Preview**.
2. Choose **Run workflow** on `main` and wait for the job to finish.
3. Open the completed run, download its artifact, and extract the ZIP on a Windows 11 x64 laptop.
4. Compare the ZIP against its `.sha256` sidecar, then launch `PilotCapture.Desktop.exe`.

The archive is a portable preview package. It does not install the application, add a shortcut, or establish the final installer/update behavior. The final installer format stays open until the preview has been exercised on the target laptops.

Pilot Capture stores its local database and managed media under `%LOCALAPPDATA%\VolumePilot\PilotCapture`; the preview package does not contain event data.
