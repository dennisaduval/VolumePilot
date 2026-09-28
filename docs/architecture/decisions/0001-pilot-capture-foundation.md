# ADR 0001: Pilot Capture foundation

- Status: Accepted
- Date: 2026-09-28

## Context

VolumePilot is a modular platform for professional volume photography. Event capture must remain available without internet and support multiple stations, structured identity and group relationships, large image files, retries, future sync, and auditability. Sales and billing are online-only platform areas and must not become dependencies of capture.

## Decision

- Build Pilot Capture as a Windows desktop application using C#/.NET 10 and Avalonia 12.
- Use SQLite with Entity Framework Core for local relational state.
- Use ULIDs for persistent entity and installation identifiers.
- Store JPEG binaries in the filesystem and relationally store image metadata and associations.
- Identify subjects through roster data and photographer input. Do not use QR codes or barcodes for subject identification.
- Keep capture local-first, with cloud sync as a later capability.
- Use a persistent immutable installation ID and station codes `s10`, `s20`, `s30`, and `s40`.
- Keep Sales gallery concerns separate from pricing and cart behavior in the capture application.
- Defer OpenCV/ONNX and NetMQ/ZeroMQ to later work; retain a filesystem handoff fallback.

## Consequences

- The application can capture and preserve associations while offline.
- Filesystem assets can be transferred and backed up independently of SQLite while remaining connected by stable IDs and relative paths.
- Future synchronization can merge structured records using stable IDs rather than relying on filenames.
- The first release carries local database and media lifecycle responsibilities. Backup, integrity checks, and missing-file visibility must be handled explicitly.
- The first release must validate .NET/Avalonia packaging on Windows 11 Pro.

## Revisit when

The Windows pilot reveals performance or deployment limits, or a concrete sync/multi-station need requires selecting and validating transport, conflict-resolution, or image-transfer protocols.

