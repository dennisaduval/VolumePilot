# ADR 0005: HQ tenant isolation

- Status: Accepted
- Date: 2026-09-28

## Context

VolumePilot HQ is a multi-tenant commercial service. Each photography company can have multiple users, client organizations, Jobs, Events, subjects, devices, and operational records. A company must not be able to access another company's data.

The first release should keep operations simple while making tenant isolation explicit and testable.

## Decision

- Start with one PostgreSQL database and shared schema.
- Every tenant-owned entity carries a tenant ID and a globally unique ULID.
- Company accounts are the top-level isolation boundary. Client organizations are customer records owned by a company, not tenants of VolumePilot.
- Subjects are owned by one company account. Do not create cross-company person identity links.
- Every human request resolves an authenticated user, active company membership, and role before executing tenant data access. Do not trust a tenant ID supplied only by a request body or query string.
- Device credentials resolve to a registered Capture installation, its company account, and its assigned Event scope.
- Tenant scope is applied in application queries and authorization policies. Relational keys and constraints include tenant scope where needed to prevent cross-tenant references.
- Add automated authorization tests that attempt cross-tenant reads and writes for each tenant-owned module.
- Deletion, retention, and account closure behavior must be defined before general commercial launch.

## Consequences

- A shared database keeps the initial deployment and backup model small.
- Tenant identity is present in records and API contracts, enabling future migration of a large company to isolated infrastructure if measured needs justify it.
- The design avoids cross-company matching of children or other subjects.
- Isolation depends on consistently applying tenant scope. Code review, relational constraints, and cross-tenant tests are required before release.
- PostgreSQL row-level security may be evaluated during schema implementation as additional defense, after validating its interaction with connection pooling and application data access. It is not a substitute for application authorization.

## Revisit when

A tenant's data residency, contractual isolation, scale, or security requirements make a separate database or deployment necessary.
