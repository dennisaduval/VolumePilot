# ADR 0004: HQ application shape and technology baseline

- Status: Accepted
- Date: 2026-09-28

## Context

VolumePilot needs a central web application for company administration, client setup, jobs, Events, rosters, Capture configuration, devices, and sync review. It must work with the existing Pilot Capture foundation and leave room for separate Pilot applications and services as product needs become concrete.

The repository already targets .NET 10, and Pilot Capture uses C#/.NET 10, Avalonia, EF Core, SQLite, and ULID identifiers. HQ is a browser application and needs a cloud relational system of record.

## Decision

- Build HQ as a browser-based application backed by an ASP.NET Core API targeting the repository's .NET 10 baseline.
- Use a TypeScript web client. Select its specific framework and component system during UX prototyping before frontend implementation.
- Use PostgreSQL for centralized relational data.
- Start as a modular monolith. Define application boundaries around accounts/access, organizations, work planning, rosters/subjects, Capture setup, and synchronization.
- Keep APIs and module interfaces explicit. Do not introduce deployable microservices until independent operation, scaling, or team ownership justifies them.
- Use ULIDs for HQ records that cross the HQ/Capture boundary, matching Pilot Capture. Internal-only database keys may be chosen during schema design if they do not leak into public contracts.
- Deploy initially using the established DigitalOcean project. Keep application instances stateless and persist data in managed services.
- Use object storage when HQ introduces durable binary media or import artifacts that need cloud availability. Do not store image bytes in PostgreSQL.

## Consequences

- The system is straightforward to deploy and operate while still having boundaries that support later extraction.
- HQ and Capture can share identifiers and versioned API contracts without sharing database entities or implementations.
- PostgreSQL supports relational constraints for tenant and event boundaries.
- A TypeScript client gives HQ a web-native UI, while the ASP.NET Core API aligns with the repository's established server-side language.
- Choosing the exact UI framework, authentication provider, and production sizing remains a prerequisite to their respective implementation work.

## Revisit when

Operational load, independent team ownership, or measured deployment constraints show that a module needs separate deployment or a different storage boundary.
