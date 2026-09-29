import { useState, type FormEvent } from "react";
import type { Event, Invitation, Job, Organization, Staff } from "./api";
import { Banner, Empty, PageTitle, dateLabel } from "./ui";

export function OrganizationsPage({
  items,
  busy,
  create,
}: {
  items: Organization[];
  busy: boolean;
  create: (body: unknown, done: (item: Organization) => void) => void;
}) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [type, setType] = useState("");
  function save(event: FormEvent) {
    event.preventDefault();
    create({ name, organizationType: type || null }, () => {
      setOpen(false);
      setName("");
      setType("");
    });
  }
  return (
    <>
      <PageTitle
        kicker="CLIENTS"
        title="Organizations"
        description="Schools, leagues, clubs, and other clients you work with."
        action={
          <button className="primary-button" onClick={() => setOpen(!open)}>
            {open ? "Close form" : "Add organization"}
          </button>
        }
      />
      {open && (
        <form className="surface form-panel" onSubmit={save}>
          <h2>New organization</h2>
          <div className="form-grid">
            <label>
              Name
              <input
                required
                maxLength={200}
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="e.g. Lakeside Youth League"
              />
            </label>
            <label>
              Type
              <input
                maxLength={80}
                value={type}
                onChange={(event) => setType(event.target.value)}
                placeholder="School, league, club…"
              />
            </label>
          </div>
          <div className="form-actions">
            <button className="primary-button" disabled={busy}>
              Save organization
            </button>
          </div>
        </form>
      )}
      <div className="surface list-surface">
        <div className="table-head">
          <strong>Name</strong>
          <strong>Type</strong>
          <strong>Added</strong>
        </div>
        {items.map((item) => (
          <div className="table-row" key={item.id}>
            <strong>{item.name}</strong>
            <span>{item.organizationType || "—"}</span>
            <span>{new Date(item.createdAtUtc).toLocaleDateString()}</span>
          </div>
        ))}
        {items.length === 0 && (
          <Empty title="No organizations yet">
            Add your first client organization to start planning Jobs.
          </Empty>
        )}
      </div>
    </>
  );
}

export function WorkPage({
  organizations,
  jobs,
  events,
  busy,
  createJob,
  createEvent,
}: {
  organizations: Organization[];
  jobs: Job[];
  events: Event[];
  busy: boolean;
  createJob: (body: unknown, done: (item: Job) => void) => void;
  createEvent: (body: unknown, done: (item: Event) => void) => void;
}) {
  const [tab, setTab] = useState<"jobs" | "events">("jobs");
  const [open, setOpen] = useState(false);
  const [orgId, setOrgId] = useState("");
  const [jobName, setJobName] = useState("");
  const [reference, setReference] = useState("");
  const [jobId, setJobId] = useState("");
  const [eventName, setEventName] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [zone, setZone] = useState(
    Intl.DateTimeFormat().resolvedOptions().timeZone,
  );
  const [location, setLocation] = useState("");
  function saveJob(event: FormEvent) {
    event.preventDefault();
    createJob(
      {
        clientOrganizationId: orgId,
        name: jobName,
        internalReference: reference || null,
      },
      () => {
        setOpen(false);
        setJobName("");
        setReference("");
      },
    );
  }
  function saveEvent(event: FormEvent) {
    event.preventDefault();
    createEvent(
      {
        jobId,
        name: eventName,
        startsAtUtc: start ? new Date(start).toISOString() : null,
        endsAtUtc: end ? new Date(end).toISOString() : null,
        timeZoneId: zone || null,
        locationName: location || null,
      },
      () => {
        setOpen(false);
        setEventName("");
        setStart("");
        setEnd("");
        setLocation("");
      },
    );
  }
  return (
    <>
      <PageTitle
        kicker="PLANNING"
        title="Jobs & Events"
        description="A Job organizes client work. An Event is a scheduled capture occasion within it."
        action={
          <button
            className="primary-button"
            onClick={() => setOpen(!open)}
            disabled={tab === "jobs" ? !organizations.length : !jobs.length}
          >
            {open ? "Close form" : tab === "jobs" ? "Create Job" : "Add Event"}
          </button>
        }
      />
      <div className="tabs" role="tablist" aria-label="Work type">
        <button
          role="tab"
          aria-selected={tab === "jobs"}
          className={tab === "jobs" ? "is-active" : ""}
          onClick={() => {
            setTab("jobs");
            setOpen(false);
          }}
        >
          Jobs <span>{jobs.length}</span>
        </button>
        <button
          role="tab"
          aria-selected={tab === "events"}
          className={tab === "events" ? "is-active" : ""}
          onClick={() => {
            setTab("events");
            setOpen(false);
          }}
        >
          Events <span>{events.length}</span>
        </button>
      </div>
      {open && tab === "jobs" && (
        <form className="surface form-panel" onSubmit={saveJob}>
          <h2>New Job</h2>
          <div className="form-grid">
            <label>
              Organization
              <select
                required
                value={orgId}
                onChange={(event) => setOrgId(event.target.value)}
              >
                <option value="">Choose an organization</option>
                {organizations.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Job name
              <input
                required
                maxLength={200}
                value={jobName}
                onChange={(event) => setJobName(event.target.value)}
                placeholder="e.g. Fall Picture Day"
              />
            </label>
            <label>
              Internal reference <span className="optional">Optional</span>
              <input
                maxLength={100}
                value={reference}
                onChange={(event) => setReference(event.target.value)}
              />
            </label>
          </div>
          <div className="form-actions">
            <button className="primary-button" disabled={busy}>
              Create Job
            </button>
          </div>
        </form>
      )}
      {open && tab === "events" && (
        <form className="surface form-panel" onSubmit={saveEvent}>
          <h2>New Event</h2>
          <div className="form-grid">
            <label>
              Job
              <select
                required
                value={jobId}
                onChange={(event) => setJobId(event.target.value)}
              >
                <option value="">Choose a Job</option>
                {jobs.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Event name
              <input
                required
                maxLength={200}
                value={eventName}
                onChange={(event) => setEventName(event.target.value)}
                placeholder="e.g. Photo Day"
              />
            </label>
            <label>
              Starts <span className="optional">Optional</span>
              <input
                type="datetime-local"
                value={start}
                onChange={(event) => setStart(event.target.value)}
              />
            </label>
            <label>
              Ends <span className="optional">Optional</span>
              <input
                type="datetime-local"
                min={start || undefined}
                value={end}
                onChange={(event) => setEnd(event.target.value)}
              />
            </label>
            <label>
              Time zone
              <input
                maxLength={100}
                value={zone}
                onChange={(event) => setZone(event.target.value)}
              />
            </label>
            <label>
              Location
              <input
                maxLength={200}
                value={location}
                onChange={(event) => setLocation(event.target.value)}
              />
            </label>
          </div>
          <p className="form-help">
            Times are entered in your browser's local time zone and stored as
            UTC. The time zone field describes the event location.
          </p>
          <div className="form-actions">
            <button className="primary-button" disabled={busy}>
              Add Event
            </button>
          </div>
        </form>
      )}
      <div className="surface list-surface">
        {tab === "jobs" ? (
          <>
            <div className="table-head work-table">
              <strong>Job</strong>
              <strong>Organization</strong>
              <strong>Status</strong>
            </div>
            {jobs.map((item) => (
              <div className="table-row work-table" key={item.id}>
                <div>
                  <strong>{item.name}</strong>
                  {item.internalReference && (
                    <small>{item.internalReference}</small>
                  )}
                </div>
                <span>
                  {organizations.find(
                    (org) => org.id === item.clientOrganizationId,
                  )?.name ?? "Organization"}
                </span>
                <span className="pill">{item.status}</span>
              </div>
            ))}
            {jobs.length === 0 && (
              <Empty title="No Jobs yet">
                {organizations.length
                  ? "Create a Job for one of your organizations."
                  : "Add an organization first, then create a Job."}
              </Empty>
            )}
          </>
        ) : (
          <>
            <div className="table-head work-table">
              <strong>Event</strong>
              <strong>Job</strong>
              <strong>Start</strong>
            </div>
            {events.map((item) => (
              <div className="table-row work-table" key={item.id}>
                <div>
                  <strong>{item.name}</strong>
                  {item.locationName && <small>{item.locationName}</small>}
                </div>
                <span>
                  {jobs.find((job) => job.id === item.jobId)?.name ?? "Job"}
                </span>
                <span>{dateLabel(item.startsAtUtc)}</span>
              </div>
            ))}
            {events.length === 0 && (
              <Empty title="No Events yet">
                {jobs.length
                  ? "Add an Event to a Job to schedule capture work."
                  : "Create a Job first, then add an Event."}
              </Empty>
            )}
          </>
        )}
      </div>
    </>
  );
}

export function StaffPage({
  items,
  invitations,
  allowed,
  localTools,
  busy,
  create,
  revoke,
  localLink,
  reportError,
}: {
  items: Staff[];
  invitations: Invitation[];
  allowed: boolean;
  localTools: boolean;
  busy: boolean;
  create: (body: unknown, done: (item: Invitation) => void) => void;
  revoke: (id: string) => void;
  localLink: (id: string) => Promise<string>;
  reportError: (message: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<"Admin" | "Staff">("Staff");
  const [link, setLink] = useState("");
  function save(event: FormEvent) {
    event.preventDefault();
    create({ email, role }, async (item) => {
      setOpen(false);
      setEmail("");
      if (localTools) {
        try {
          setLink(await localLink(item.id));
        } catch {
          setLink("");
        }
      }
    });
  }
  async function showLink(id: string) {
    try {
      setLink(await localLink(id));
    } catch (cause) {
      reportError(
        cause instanceof Error
          ? cause.message
          : "Local invitation inbox unavailable.",
      );
    }
  }
  return (
    <>
      <PageTitle
        kicker="TEAM ACCESS"
        title="Staff"
        description="Invite colleagues to the active company and review their access."
        action={
          allowed && (
            <button className="primary-button" onClick={() => setOpen(!open)}>
              {open ? "Close form" : "Invite staff"}
            </button>
          )
        }
      />
      {!allowed ? (
        <Banner tone="error">
          Only a company Owner or Admin can manage staff access.
        </Banner>
      ) : (
        <>
          {open && (
            <form className="surface form-panel" onSubmit={save}>
              <h2>Invite a colleague</h2>
              <div className="form-grid">
                <label>
                  Email
                  <input
                    type="email"
                    required
                    maxLength={254}
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                  />
                </label>
                <label>
                  Role
                  <select
                    value={role}
                    onChange={(event) =>
                      setRole(event.target.value as "Admin" | "Staff")
                    }
                  >
                    <option value="Staff">Staff · view company work</option>
                    <option value="Admin">Admin · manage work and staff</option>
                  </select>
                </label>
              </div>
              <p className="form-help">
                Invitation links expire after seven days. A new invitation to
                the same email replaces earlier pending invitations.
              </p>
              <div className="form-actions">
                <button className="primary-button" disabled={busy}>
                  Create invitation
                </button>
              </div>
            </form>
          )}
          {link && (
            <Banner tone="success">
              <strong>Local test invitation</strong>
              <p>
                Copy this link for development testing. It is available only
                while the local API runs.
              </p>
              <input
                aria-label="Invitation link"
                readOnly
                value={link}
                onFocus={(event) => event.currentTarget.select()}
              />
              <button
                className="secondary-button"
                onClick={() => void navigator.clipboard.writeText(link)}
              >
                Copy link
              </button>
            </Banner>
          )}
          <section className="surface list-surface">
            <h2>Active staff</h2>
            <div className="table-head staff-table">
              <strong>Email</strong>
              <strong>Role</strong>
              <strong>Joined</strong>
            </div>
            {items.map((item) => (
              <div className="table-row staff-table" key={item.membershipId}>
                <strong>{item.email}</strong>
                <span className="pill">{item.role}</span>
                <span>{new Date(item.joinedAtUtc).toLocaleDateString()}</span>
              </div>
            ))}
            {items.length === 0 && (
              <Empty title="No staff listed">
                Invite your first colleague to this workspace.
              </Empty>
            )}
          </section>
          <section className="surface list-surface">
            <h2>Invitations</h2>
            <div className="table-head invitation-table">
              <strong>Email</strong>
              <strong>Role</strong>
              <strong>Status</strong>
              <strong>Actions</strong>
            </div>
            {invitations.map((item) => {
              const status = item.acceptedAtUtc
                ? "Accepted"
                : item.revokedAtUtc
                  ? "Revoked"
                  : new Date(item.expiresAtUtc) < new Date()
                    ? "Expired"
                    : "Pending";
              return (
                <div className="table-row invitation-table" key={item.id}>
                  <strong>{item.email}</strong>
                  <span>{item.role}</span>
                  <span>{status}</span>
                  <div className="row-actions">
                    {status === "Pending" && (
                      <>
                        {localTools && (
                          <button
                            className="text-button"
                            onClick={() => void showLink(item.id)}
                          >
                            Local link
                          </button>
                        )}
                        <button
                          className="text-button danger"
                          onClick={() => revoke(item.id)}
                          disabled={busy}
                        >
                          Revoke
                        </button>
                      </>
                    )}
                  </div>
                </div>
              );
            })}
            {invitations.length === 0 && (
              <Empty title="No invitations yet">
                Create an invitation to add staff.
              </Empty>
            )}
          </section>
        </>
      )}
    </>
  );
}
