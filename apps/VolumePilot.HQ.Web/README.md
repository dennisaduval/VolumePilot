# VolumePilot HQ web client

The first HQ interface connects to the existing cookie-authenticated API. It supports local first-owner setup, sign-in, company selection, organizations, Jobs, Events, and staff invitations. Subjects, devices, and synchronization are future modules.

## Run locally

From the repository root, start the API in one terminal:

```sh
dotnet run --project apps/VolumePilot.HQ.Api/VolumePilot.HQ.Api.csproj
```

Then start the web client in another:

```sh
cd apps/VolumePilot.HQ.Web
npm ci
npm run dev
```

Open the URL printed by Vite. Its `/api` proxy targets `http://localhost:5080` by default; set `HQ_API_ORIGIN` before running Vite if the API uses another origin. Vite serves the browser client and proxies API requests on one origin, allowing the API's HTTP-only cookies and CSRF protection to work normally. The Development API can create the first company through the setup screen.

An Owner or Admin can create a staff invitation. In Development, the Staff screen exposes a local test link; open it in a separate browser session to accept as a new user. Existing users sign in with their invited email first. Production invitation email delivery is still a separate deployment requirement, and the local inbox is unavailable there.

## Build

```sh
cd apps/VolumePilot.HQ.Web
npm ci
npm run build
```

The output is `dist/`. Production hosting of this bundle with the API on one origin is not yet wired into the deployment pipeline. Do not deploy the browser bundle at a separate origin without reviewing cookie, CSRF, and routing behavior.
