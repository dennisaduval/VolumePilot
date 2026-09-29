# ADR 0008: HQ web client

- Status: Accepted
- Date: 2026-09-28

## Context

HQ is an authenticated operations application for studio owners and staff. Its first users need organization, roster, Job, Event, and device workflows. Pilot Capture remains a separate local-first desktop application. HQ does not need public search-engine rendering for its initial operational screens.

The React project guidance recommends starting with a framework for most new apps, while also documenting a build-tool setup when application constraints call for it. The HQ browser client is a focused client for an ASP.NET Core API and is deployed with that API.

## Decision

- Use React with TypeScript for the HQ browser interface.
- Use Vite for local development and static production builds.
- Treat the frontend as a same-origin browser client for the ASP.NET Core API. The API remains the authority for identity, permissions, validation, and data.
- Keep the first client client-rendered. Add server rendering only if a concrete product requirement justifies a server runtime.
- Use VolumePilot design tokens from the approved guide: Navy `#0B1F3A`, Royal Blue `#175CD3`, Flight Orange `#F97316`, Sky Blue `#38BDF8`, Cloud `#F5F7FA`, and Instrument `#111827`.
- Keep interactions accessible by keyboard, communicate state with text and color, and use standard software terms when aviation language would reduce clarity.

## Consequences

- The UI can evolve independently from Capture while using the documented HQ API contracts.
- The web bundle can be served by the API from the same origin, simplifying cookie authentication and local API proxying.
- No Node server is required to serve the production HQ client.
- Route and data-fetching libraries will be selected when the first multi-screen data workflow is implemented, using the current React guidance and the actual navigation needs.

## References

- [React: Creating a React App](https://react.dev/learn/creating-a-react-app)
- [React: Build a React App from Scratch](https://react.dev/learn/build-a-react-app-from-scratch)
- [Vite Guide](https://vite.dev/guide/)
