# ADR 0002: Local relational data and image assets

- Status: Accepted
- Date: 2026-09-28

## Decision

SQLite is the local source of truth for structured capture state. Image binaries are written to managed local storage; the database stores ULID, relative path, original filename, media type, byte length, checksum, dimensions when known, capture timestamp, and review/association state. Entity relationships are represented with foreign keys. Filenames are generated output and never serve as the primary identity key.

Capture/import writes that update related state use a transaction. Filesystem writes are staged and finalized with recoverable state so interruption can be detected and retried. This bootstrap defines the schema and metadata; the recoverable file writer is a later use-case implementation.

## SQLite operational settings

- `PRAGMA foreign_keys = ON` for each connection.
- WAL journal mode for local concurrent readers and a single serialized writer.
- Bounded busy timeout.
- Versioned schema migrations; no production use of `EnsureCreated` as a substitute for migrations.
- Restrictive deletes for historical capture records; removal from an active roster is a status change, not destructive deletion.

## Consequences

The database remains compact and queryable, and file transfers can be retried without losing subject/session relationships. A missing file does not erase the database record; integrity checks can report and repair or re-ingest it.

