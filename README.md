# VolumePilot

## Volume Photography Workflow and Operations Platform

VolumePilot is a modular software platform designed specifically for professional volume photography operations.

The goal of VolumePilot is to provide photographers and photography companies with a unified system for planning, managing, capturing, selling, producing, fulfilling, and analyzing high-volume photography events.

VolumePilot is intended for workflows such as:

- School picture days
- Youth sports leagues
- Team and individual photography
- Dance and performing arts
- Cheerleading and competitions
- Tournaments
- On-site photography and printing
- High-volume portrait sessions
- Graduation and special events
- Other structured volume photography workflows

## Philosophy

Volume photography involves much more than taking photographs.

A typical operation may involve organizations, events, teams, rosters, subjects, schedules, photographers, capture stations, image identification, image processing, galleries, products, orders, payments, printers, production workflows, fulfillment, and reporting.

VolumePilot is designed to connect these processes while allowing each component to remain modular.

The workflow should adapt to the photography operation rather than forcing the photography operation to adapt to the software.

## Platform Architecture

VolumePilot is envisioned as a collection of interconnected Pilot applications and services.

Individual applications may operate in the cloud, locally on event computers, or through a hybrid local/cloud architecture depending on their operational requirements.

### Local-First Applications

Time-critical event applications should remain operational without an internet connection.

For example, Pilot Capture should be capable of identifying subjects, receiving photographs, associating images with subjects and groups, and managing capture workflows entirely on the local computer or event network.

Pilot Print should similarly be capable of managing local production and printer queues without depending on cloud connectivity.

When internet connectivity is available, local applications can synchronize appropriate data with VolumePilot Cloud.

### Cloud Applications

Applications that inherently require centralized or internet-connected services can operate through VolumePilot Cloud.

These may include online sales, account management, payments, billing, analytics, administration, galleries, and other centralized services.

## Modular Design

VolumePilot is being designed as a modular platform.

Potential modules and applications include:

### Pilot Core
Shared platform services, identities, studios, users, permissions, configuration, common data structures, API contracts, and system-wide business rules.

### Pilot Database
Management of organizations, rosters, subjects, teams, groups, classifications, and other structured volume-photography data.

### Pilot Events
Event creation, scheduling, locations, sessions, organizations, teams, stations, and operational event configuration.

### Pilot Capture
Local-first photography capture workflow management, including roster-driven subject selection, photographer input for unidentified subjects, JPEG ingestion and association, capture tracking, and configurable capture profiles. QR codes and barcodes are not used to identify subjects.

Pilot Capture is intended to support multiple types of volume workflows, including single-team sessions, multi-team events, known and unknown subjects, single-image capture, multi-pose capture, and custom capture profiles.

### Pilot Images
Image asset records, metadata, associations, processing state, derivatives, previews, and image-management workflows.

### Pilot Galleries
Customer-facing image discovery, galleries, proofing, and digital delivery.

### Pilot Commerce
Products, packages, pricing, discounts, sales rules, and related commerce configuration.

### Pilot POS
On-site sales and cashier workflows.

### Pilot Orders
Order creation, management, status tracking, and lifecycle processing regardless of the order source.

### Pilot Payments
Customer-to-studio payment processing and payment-provider integrations.

### Pilot Print
Local-first print production, printer management, routing, queues, retries, reprints, and production tracking.

### Pilot Fulfillment
Physical and digital fulfillment workflows and order completion.

### Pilot Analytics
Operational reporting, sales analysis, event performance, production metrics, and business intelligence.

### Pilot Billing
Billing between the VolumePilot platform and participating photography studios.

### Pilot Admin
Platform and studio administration, configuration, users, permissions, and system management.

## Capture Model

VolumePilot recognizes that there is no single volume-photography capture workflow.

A capture session may involve:

- One team or many teams
- One organization or multiple organizations
- Known rostered subjects
- Unknown walk-up subjects
- A mixture of known and unknown subjects
- One photograph per subject
- Multiple required photographs per subject
- Variable numbers of photographs
- One capture station or many simultaneous stations
- Team, sport, division, class, school, or other group associations
- Roster selection for known subjects and photographer input to create unidentified subjects. QR codes and barcodes are not used to identify subjects.

Capture behavior should therefore be configurable rather than hard-coded around a single photography business model.

## Offline-First Event Operations

A fundamental VolumePilot design principle is:

> Cloud services should enhance an event, not be required to operate the event.

Critical event workflows should continue functioning when internet service is unavailable, slow, or unreliable.

Local applications should maintain the information required to perform their work and synchronize with VolumePilot Cloud when connectivity permits.

## Data and Synchronization

VolumePilot will maintain clearly defined relationships between operational entities such as:

Organization → Event → Session → Group → Subject → Capture → Image → Order → Payment → Production → Fulfillment

The exact domain model will evolve as requirements are formally defined.

Local and cloud applications will communicate through defined interfaces and synchronization mechanisms rather than directly depending on one another's internal implementations.

## Development Principles

VolumePilot development will emphasize:

- Modular architecture
- Clearly defined module responsibilities
- Local-first event reliability
- Cloud synchronization
- Strong data integrity
- Multi-studio support
- Scalability
- Security
- Auditability
- Configurable workflows
- API-driven integration
- Maintainable code
- Automated testing
- Clear documentation

Technology choices will be made based on the requirements of each component rather than requiring every VolumePilot application to use the same programming language or framework.

## Project Status

VolumePilot has begun foundational implementation. Pilot Capture is the first application moving toward its 0.1 First Flight milestone. Its selected baseline is C#/.NET 10, Avalonia 12, SQLite with EF Core, and ULID identifiers. The application is local-first and must continue operating offline.

The initial Pilot Capture solution structure, domain model, SQLite schema, and milestone documentation are being established in this repository. Capture UI workflows, CSV import, camera integration, Windows validation, and packaging remain in progress. See [Pilot Capture documentation](docs/pilot-capture/README.md) and the [First Flight checklist](docs/pilot-capture/first-flight-checklist.md).

---

VolumePilot

A modular operating platform for professional volume photography.
