# ADR 0007: HQ Job and Event boundary

- Status: Accepted
- Date: 2026-09-28

## Context

Photographers may book work that spans several dates, locations, teams, or stations. Pilot Capture already uses Event as the local operational boundary for roster, group, session, and image associations. HQ must coordinate with that model without forcing Capture to redesign its local Event.

## Decision

- A **Job** represents a booked body of work for a client. It may contain one or more Events.
- An **Event** represents a specific operational occurrence, such as a photo day or tournament date at a location. It may contain multiple Groups, CaptureSessions, and assigned Capture installations.
- A **CaptureSession** is a bounded workflow at an Event, associated with one installation/station and photographer.
- Each HQ Event is mapped one-to-one to the corresponding local Pilot Capture Event.
- A Job may have shared client and planning context, but operational configuration and capture history are scoped to the Event.
- A Job can be useful for a single Event; users should not have to create an artificial multi-event structure to do ordinary work.

## Consequences

- Multi-day or multi-location work can be managed under one Job while remaining operationally clear in Capture.
- Existing Capture Event relationships remain intact.
- Sync, assignment, roster scope, and audit history use the Event ID as the operational boundary.
- The HQ UI can show a Job overview and a dedicated workspace for each Event.

## Revisit when

Field use shows that one Capture Event must span multiple operational dates or sites while preserving meaningful roster and session boundaries.
