import { useState, type FormEvent } from "react";
import type { Event, Invitation, Job, Organization, Staff } from "./api";
import { Banner, Empty, PageTitle, dateLabel } from "./ui";
import { PlanningHistory } from "./PlanningHistory";

function localDateTime(value: string): string {
  const date = new Date(value);
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 23);
}

export function OrganizationsPage({
  items,
  busy,
  allowed,
  create,
  update,
}: {
  items: Organization[];
  busy: boolean;
  allowed: boolean;
  create: (body: unknown, done: (item: Organization) => void) => void;
  update: (item: Organization, body: unknown) => Promise<boolean>;
}) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [type, setType] = useState("");
  const [editing, setEditing] = useState<Organization | null>(null);
  const [historyId, setHistoryId] = useState<string | null>(null);
  const historyItem = items.find((item) => item.id === historyId);
  function toggleForm() {
    setEditing(null);
    setName("");
    setType("");
    setOpen(!open);
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (editing) {
      if (await update(editing, { name, organizationType: type || null, revision: editing.revision })) setEditing(null);
      return;
    }
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
          allowed && <button className="primary-button" disabled={busy} onClick={toggleForm}>
            {open ? "Close form" : "Add organization"}
          </button>
        }
      />
      {(open || editing) && (
        <form className="surface form-panel" onSubmit={save}>
          <h2>{editing ? "Edit organization" : "New organization"}</h2>
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
              {editing ? "Save changes" : "Save organization"}
            </button>
            {editing && <button type="button" className="secondary-button" disabled={busy} onClick={() => setEditing(null)}>Cancel</button>}
          </div>
        </form>
      )}
      {historyItem && <PlanningHistory entityType="Organization" item={historyItem} onClose={() => setHistoryId(null)} />}
      <div className="surface list-surface">
        <div className="table-head editable-table">
          <strong>Name</strong>
          <strong>Type</strong>
          <strong>Added</strong>
          <strong>Action</strong>
        </div>
        {items.map((item) => (
          <div className="table-row editable-table" key={item.id}>
            <strong>{item.name}</strong>
            <span>{item.organizationType || "—"}</span>
            <span>{new Date(item.createdAtUtc).toLocaleDateString()}</span>
            <div className="row-actions">
              {allowed && <button className="text-button" disabled={busy} onClick={() => { setOpen(false); setEditing(item); setName(item.name); setType(item.organizationType ?? ""); }}>Edit</button>}
              <button className="text-button" onClick={() => setHistoryId(item.id)}>History</button>
            </div>
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
  allowed,
  createJob,
  createEvent,
  updateJob,
  updateEvent,
}: {
  organizations: Organization[];
  jobs: Job[];
  events: Event[];
  busy: boolean;
  allowed: boolean;
  createJob: (body: unknown, done: (item: Job) => void) => void;
  createEvent: (body: unknown, done: (item: Event) => void) => void;
  updateJob: (item: Job, body: unknown) => Promise<boolean>;
  updateEvent: (item: Event, body: unknown) => Promise<boolean>;
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
  const [editingJob, setEditingJob] = useState<Job | null>(null);
  const [editingEvent, setEditingEvent] = useState<Event | null>(null);
  const [history, setHistory] = useState<{ entityType: "Job" | "Event"; id: string } | null>(null);
  const historyItem = history?.entityType === "Job" ? jobs.find((item) => item.id === history.id) : events.find((item) => item.id === history?.id);
  function resetForm() {
    setEditingJob(null);
    setEditingEvent(null);
    setOrgId("");
    setJobName("");
    setReference("");
    setJobId("");
    setEventName("");
    setStart("");
    setEnd("");
    setZone(Intl.DateTimeFormat().resolvedOptions().timeZone);
    setLocation("");
  }
  async function saveJob(event: FormEvent) {
    event.preventDefault();
    if (editingJob) {
      if (await updateJob(editingJob, { name: jobName, internalReference: reference || null, revision: editingJob.revision })) setEditingJob(null);
      return;
    }
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
  async function saveEvent(event: FormEvent) {
    event.preventDefault();
    if (editingEvent) {
      if (await updateEvent(editingEvent, { name: eventName, startsAtUtc: start ? new Date(start).toISOString() : null, endsAtUtc: end ? new Date(end).toISOString() : null, timeZoneId: zone || null, locationName: location || null, revision: editingEvent.revision })) setEditingEvent(null);
      return;
    }
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
          allowed && <button
            className="primary-button"
            onClick={() => { resetForm(); setOpen(!open); }}
            disabled={busy || (tab === "jobs" ? !organizations.length : !jobs.length)}
          >
            {open ? "Close form" : tab === "jobs" ? "Create Job" : "Add Event"}
          </button>
        }
      />
      <div className="tabs" role="tablist" aria-label="Work type">
        <button
          role="tab"
          aria-selected={tab === "jobs"}
          disabled={busy}
          className={tab === "jobs" ? "is-active" : ""}
          onClick={() => {
            setTab("jobs");
            setOpen(false);
            resetForm();
          }}
        >
          Jobs <span>{jobs.length}</span>
        </button>
        <button
          role="tab"
          aria-selected={tab === "events"}
          disabled={busy}
          className={tab === "events" ? "is-active" : ""}
          onClick={() => {
            setTab("events");
            setOpen(false);
            resetForm();
          }}
        >
          Events <span>{events.length}</span>
        </button>
      </div>
      {(open || editingJob) && tab === "jobs" && (
        <form className="surface form-panel" onSubmit={saveJob}>
          <h2>{editingJob ? "Edit Job" : "New Job"}</h2>
          <div className="form-grid">
            <label>
              Organization
              <select
                required
                disabled={Boolean(editingJob)}
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
              {editingJob ? "Save changes" : "Create Job"}
            </button>
            {editingJob && <button type="button" className="secondary-button" disabled={busy} onClick={() => setEditingJob(null)}>Cancel</button>}
          </div>
        </form>
      )}
      {(open || editingEvent) && tab === "events" && (
        <form className="surface form-panel" onSubmit={saveEvent}>
          <h2>{editingEvent ? "Edit Event" : "New Event"}</h2>
          <div className="form-grid">
            <label>
              Job
              <select
                required
                disabled={Boolean(editingEvent)}
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
                step="any"
                value={start}
                onChange={(event) => setStart(event.target.value)}
              />
            </label>
            <label>
              Ends <span className="optional">Optional</span>
              <input
                type="datetime-local"
                step="any"
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
              {editingEvent ? "Save changes" : "Add Event"}
            </button>
            {editingEvent && <button type="button" className="secondary-button" disabled={busy} onClick={() => setEditingEvent(null)}>Cancel</button>}
          </div>
        </form>
      )}
      {history && historyItem && <PlanningHistory entityType={history.entityType} item={historyItem} onClose={() => setHistory(null)} />}
      <div className="surface list-surface">
        {tab === "jobs" ? (
          <>
            <div className="table-head work-table editable-table">
              <strong>Job</strong>
              <strong>Organization</strong>
              <strong>Status</strong>
              <strong>Action</strong>
            </div>
            {jobs.map((item) => (
              <div className="table-row work-table editable-table" key={item.id}>
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
                <div className="row-actions">
                  {allowed && <button className="text-button" disabled={busy} onClick={() => { setOpen(false); setEditingEvent(null); setEditingJob(item); setOrgId(item.clientOrganizationId); setJobName(item.name); setReference(item.internalReference ?? ""); }}>Edit</button>}
                  <button className="text-button" onClick={() => setHistory({ entityType: "Job", id: item.id })}>History</button>
                </div>
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
            <div className="table-head work-table editable-table">
              <strong>Event</strong>
              <strong>Job</strong>
              <strong>Start</strong>
              <strong>Action</strong>
            </div>
            {events.map((item) => (
              <div className="table-row work-table editable-table" key={item.id}>
                <div>
                  <strong>{item.name}</strong>
                  {item.locationName && <small>{item.locationName}</small>}
                </div>
                <span>
                  {jobs.find((job) => job.id === item.jobId)?.name ?? "Job"}
                </span>
                <span>{dateLabel(item.startsAtUtc)}</span>
                <div className="row-actions">
                  {allowed && <button className="text-button" disabled={busy} onClick={() => { setOpen(false); setEditingJob(null); setEditingEvent(item); setJobId(item.jobId); setEventName(item.name); setStart(item.startsAtUtc ? localDateTime(item.startsAtUtc) : ""); setEnd(item.endsAtUtc ? localDateTime(item.endsAtUtc) : ""); setZone(item.timeZoneId ?? ""); setLocation(item.locationName ?? ""); }}>Edit</button>}
                  <button className="text-button" onClick={() => setHistory({ entityType: "Event", id: item.id })}>History</button>
                </div>
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
