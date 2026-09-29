import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
  type ReactNode,
} from "react";
import {
  ApiError,
  get,
  post,
  type Company,
  type Event,
  type Invitation,
  type Job,
  type LocalInvitation,
  type Organization,
  type Session,
  type Staff,
} from "./api";

type Section =
  | "Home"
  | "Organizations"
  | "Work"
  | "Staff"
  | "People"
  | "Devices";
type Mode = "loading" | "login" | "bootstrap" | "company" | "app";
const sections: Section[] = [
  "Home",
  "Organizations",
  "Work",
  "Staff",
  "People",
  "Devices",
];
const labels: Record<Section, string> = {
  Home: "Overview",
  Organizations: "Organizations",
  Work: "Jobs & Events",
  Staff: "Staff",
  People: "Subjects",
  Devices: "Devices",
};

function invitationFromHash() {
  const query = new URLSearchParams(window.location.hash.replace(/^#/, ""));
  return { id: query.get("invite"), token: query.get("token") };
}

function dateLabel(value: string | null): string {
  if (!value) return "Date not set";
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function Banner({
  children,
  tone = "info",
}: {
  children: ReactNode;
  tone?: "info" | "error" | "success";
}) {
  return (
    <div
      className={`banner banner-${tone}`}
      role={tone === "error" ? "alert" : "status"}
    >
      {children}
    </div>
  );
}

function Empty({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="empty">
      <strong>{title}</strong>
      <p>{children}</p>
    </div>
  );
}

function PageTitle({
  kicker,
  title,
  description,
  action,
}: {
  kicker: string;
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        <p className="eyebrow">{kicker}</p>
        <h1>{title}</h1>
        <p className="page-subtitle">{description}</p>
      </div>
      {action}
    </div>
  );
}

export function App() {
  const [mode, setMode] = useState<Mode>("loading");
  const [session, setSession] = useState<Session | null>(null);
  const [activeSection, setActiveSection] = useState<Section>("Home");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [invite, setInvite] = useState(invitationFromHash);
  const [loginEmail, setLoginEmail] = useState("");
  const [loginPassword, setLoginPassword] = useState("");
  const [companyName, setCompanyName] = useState("");
  const [bootstrapEmail, setBootstrapEmail] = useState("");
  const [bootstrapPassword, setBootstrapPassword] = useState("");
  const [acceptPassword, setAcceptPassword] = useState("");
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [jobs, setJobs] = useState<Job[]>([]);
  const [events, setEvents] = useState<Event[]>([]);
  const [staff, setStaff] = useState<Staff[]>([]);
  const [invitations, setInvitations] = useState<Invitation[]>([]);
  const [staffAllowed, setStaffAllowed] = useState(true);
  const [localTools, setLocalTools] = useState(false);
  const [dataLoading, setDataLoading] = useState(false);

  const loadData = useCallback(async () => {
    setDataLoading(true);
    try {
      const [orgResult, jobResult, eventResult, staffResult, inviteResult] =
        await Promise.allSettled([
          get<Organization[]>("/api/organizations"),
          get<Job[]>("/api/jobs"),
          get<Event[]>("/api/events"),
          get<Staff[]>("/api/staff"),
          get<Invitation[]>("/api/staff/invitations"),
        ]);
      if (orgResult.status === "rejected") throw orgResult.reason;
      if (jobResult.status === "rejected") throw jobResult.reason;
      if (eventResult.status === "rejected") throw eventResult.reason;
      setOrganizations(orgResult.value);
      setJobs(jobResult.value);
      setEvents(eventResult.value);
      setStaffAllowed(staffResult.status === "fulfilled");
      setStaff(staffResult.status === "fulfilled" ? staffResult.value : []);
      setInvitations(
        inviteResult.status === "fulfilled" ? inviteResult.value : [],
      );
    } catch (cause) {
      setError(
        cause instanceof Error ? cause.message : "Could not load company data.",
      );
    } finally {
      setDataLoading(false);
    }
  }, []);

  useEffect(() => {
    let mounted = true;
    get("/api/dev/bootstrap-status")
      .then(() => {
        if (mounted) setLocalTools(true);
      })
      .catch(() => {
        if (mounted) setLocalTools(false);
      });
    get<Session>("/api/auth/me")
      .then(async (current) => {
        if (!mounted) return;
        setSession(current);
        setMode(current.activeCompanyId ? "app" : "company");
      })
      .catch(async (cause) => {
        if (!mounted) return;
        if (cause instanceof ApiError && cause.status === 401) {
          try {
            const state = await get<{ requiresBootstrap: boolean }>(
              "/api/dev/bootstrap-status",
            );
            if (mounted)
              setMode(state.requiresBootstrap ? "bootstrap" : "login");
          } catch {
            if (mounted) setMode("login");
          }
        } else {
          setError(
            cause instanceof Error ? cause.message : "HQ is unavailable.",
          );
          setMode("login");
        }
      });
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    if (mode === "app" && session?.activeCompanyId) void loadData();
  }, [mode, session?.activeCompanyId, loadData]);

  async function submit(task: () => Promise<void>) {
    setError("");
    setNotice("");
    setBusy(true);
    try {
      await task();
    } catch (cause) {
      setError(
        cause instanceof Error ? cause.message : "Something went wrong.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function login(event: FormEvent) {
    event.preventDefault();
    await submit(async () => {
      const current = await post<Session>("/api/auth/login", {
        email: loginEmail,
        password: loginPassword,
      });
      setSession(current);
      setLoginPassword("");
      setMode(current.activeCompanyId ? "app" : "company");
      if (invite.id)
        setNotice(
          "Signed in. Accept the invitation to join the additional company.",
        );
    });
  }

  async function bootstrap(event: FormEvent) {
    event.preventDefault();
    await submit(async () => {
      const current = await post<Session>("/api/dev/bootstrap", {
        companyName,
        email: bootstrapEmail,
        password: bootstrapPassword,
      });
      setSession(current);
      setBootstrapPassword("");
      setMode("app");
    });
  }

  async function chooseCompany(company: Company) {
    await submit(async () => {
      await post("/api/auth/active-company", { companyId: company.id });
      const current = await get<Session>("/api/auth/me");
      setSession(current);
      setMode("app");
      setActiveSection("Home");
    });
  }

  async function logout() {
    await submit(async () => {
      await post("/api/auth/logout", {});
      setSession(null);
      setOrganizations([]);
      setJobs([]);
      setEvents([]);
      setStaff([]);
      setMode("login");
      setActiveSection("Home");
    });
  }

  async function acceptInvitation(event: FormEvent) {
    event.preventDefault();
    await submit(async () => {
      await post("/api/auth/accept-invitation", {
        invitationId: invite.id,
        token: invite.token,
        password: acceptPassword || null,
      });
      window.history.replaceState(
        null,
        "",
        window.location.pathname + window.location.search,
      );
      setInvite({ id: null, token: null });
      setAcceptPassword("");
      const current = await get<Session>("/api/auth/me");
      setSession(current);
      setMode("app");
      setNotice("Invitation accepted. Your company workspace is ready.");
    });
  }

  async function create<T>(
    path: string,
    body: unknown,
    onSuccess: (item: T) => void,
    message: string,
  ) {
    await submit(async () => {
      const item = await post<T>(path, body);
      onSuccess(item);
      setNotice(message);
      await loadData();
    });
  }

  const company = session?.companies.find(
    (item) => item.id === session.activeCompanyId,
  );
  const hasInvite = Boolean(invite.id && invite.token);
  const upcoming = events
    .filter(
      (item) => item.startsAtUtc && new Date(item.startsAtUtc) >= new Date(),
    )
    .sort((a, b) => (a.startsAtUtc ?? "").localeCompare(b.startsAtUtc ?? ""))
    .slice(0, 5);

  if (mode === "loading")
    return (
      <div className="loading-screen" role="status">
        Loading VolumePilot HQ…
      </div>
    );

  if (mode === "login" || mode === "bootstrap" || mode === "company") {
    return (
      <div className="auth-layout">
        <div className="auth-brand">
          <img
            src="/volumepilot-logo.png"
            alt="VolumePilot. More photos. Less chaos."
          />
          <p>Plan the work. Keep the field moving.</p>
        </div>
        <main className="auth-panel">
          <div className="auth-card">
            <span className="auth-kicker">VOLUMEPILOT HQ</span>
            {hasInvite && (
              <div className="invite-callout">
                <strong>You've been invited to HQ</strong>
                <p>
                  {mode === "login"
                    ? "Sign in if you already have an account. New to HQ? Set your password below."
                    : "Your invitation is ready to accept."}
                </p>
              </div>
            )}
            {mode === "login" && (
              <>
                <h1>Sign in</h1>
                <p className="auth-description">
                  Access your company workspace.
                </p>
                <form onSubmit={login} className="form-stack">
                  <label>
                    Email
                    <input
                      type="email"
                      autoComplete="email"
                      required
                      value={loginEmail}
                      onChange={(event) => setLoginEmail(event.target.value)}
                    />
                  </label>
                  <label>
                    Password
                    <input
                      type="password"
                      autoComplete="current-password"
                      required
                      value={loginPassword}
                      onChange={(event) => setLoginPassword(event.target.value)}
                    />
                  </label>
                  <button className="primary-button" disabled={busy}>
                    Sign in
                  </button>
                </form>
                {hasInvite && (
                  <div className="auth-divider">or create a new account</div>
                )}
              </>
            )}
            {hasInvite && mode !== "bootstrap" && (
              <form
                onSubmit={acceptInvitation}
                className="form-stack invite-form"
              >
                <h2>Accept invitation</h2>
                <p>
                  New to HQ? Choose a password. If you already have an account,
                  sign in above, then accept without entering a new password.
                </p>
                <label>
                  New account password
                  <input
                    type="password"
                    minLength={12}
                    autoComplete="new-password"
                    value={acceptPassword}
                    onChange={(event) => setAcceptPassword(event.target.value)}
                    placeholder="Leave blank if already signed in"
                  />
                </label>
                <button className="secondary-button" disabled={busy}>
                  Accept invitation
                </button>
              </form>
            )}
            {mode === "bootstrap" && (
              <>
                <h1>Create your workspace</h1>
                <p className="auth-description">
                  Local first run: create the initial company owner.
                </p>
                <form onSubmit={bootstrap} className="form-stack">
                  <label>
                    Company name
                    <input
                      required
                      minLength={2}
                      maxLength={200}
                      value={companyName}
                      onChange={(event) => setCompanyName(event.target.value)}
                    />
                  </label>
                  <label>
                    Owner email
                    <input
                      type="email"
                      autoComplete="email"
                      required
                      value={bootstrapEmail}
                      onChange={(event) =>
                        setBootstrapEmail(event.target.value)
                      }
                    />
                  </label>
                  <label>
                    Password
                    <input
                      type="password"
                      autoComplete="new-password"
                      minLength={12}
                      required
                      value={bootstrapPassword}
                      onChange={(event) =>
                        setBootstrapPassword(event.target.value)
                      }
                    />
                  </label>
                  <p className="form-help">
                    Use at least 12 characters, including uppercase, lowercase,
                    a number, and a symbol.
                  </p>
                  <button className="primary-button" disabled={busy}>
                    Create workspace
                  </button>
                </form>
              </>
            )}
            {mode === "company" && (
              <>
                <h1>Choose a company</h1>
                <p className="auth-description">
                  Select the workspace you want to manage.
                </p>
                <div className="company-options">
                  {session?.companies.map((item) => (
                    <button
                      key={item.id}
                      className="company-option"
                      disabled={busy}
                      onClick={() => void chooseCompany(item)}
                    >
                      {item.name}
                      <span aria-hidden="true">→</span>
                    </button>
                  ))}
                </div>
                <button
                  className="text-button"
                  onClick={() => void logout()}
                  disabled={busy}
                >
                  Sign out
                </button>
              </>
            )}
            {error && <Banner tone="error">{error}</Banner>}
            {notice && <Banner tone="success">{notice}</Banner>}
          </div>
        </main>
      </div>
    );
  }

  return (
    <div className="app-shell">
      <aside className="sidebar" aria-label="Main navigation">
        <div className="brand">
          <div className="brand-name">
            <span>VOLUME</span>
            <strong>PILOT</strong>
            <small>HQ</small>
          </div>
          <div className="brand-tagline">MORE PHOTOS. LESS CHAOS.</div>
        </div>
        <div className="workspace-name">
          {company?.name ?? "Company workspace"}
        </div>
        <nav className="nav-list" aria-label="Workspace">
          {sections.map((item) => (
            <button
              key={item}
              className={`nav-item${activeSection === item ? " is-active" : ""}`}
              aria-current={activeSection === item ? "page" : undefined}
              onClick={() => {
                setActiveSection(item);
                setError("");
                setNotice("");
              }}
            >
              <span className="nav-symbol" aria-hidden="true">
                {item.slice(0, 1)}
              </span>
              {labels[item]}
            </button>
          ))}
        </nav>
        <div className="sidebar-footer">
          <span className="online-dot" />
          HQ workspace
        </div>
      </aside>
      <main className="main-area">
        <header className="topbar">
          <div className="breadcrumb">
            HQ <span>/</span> {labels[activeSection]}
          </div>
          <div className="topbar-actions">
            <span className="user-email">{session?.email}</span>
            {session && session.companies.length > 1 && (
              <button
                className="text-button"
                onClick={() => setMode("company")}
              >
                Switch company
              </button>
            )}
            <button
              className="text-button"
              onClick={() => void logout()}
              disabled={busy}
            >
              Sign out
            </button>
          </div>
        </header>
        <div className="page-content">
          {hasInvite && (
            <Banner>
              <strong>Pending invitation.</strong> Accept it below to join
              another company.{" "}
              <form onSubmit={acceptInvitation} className="inline-invite">
                <button className="secondary-button" disabled={busy}>
                  Accept invitation
                </button>
              </form>
            </Banner>
          )}
          {error && <Banner tone="error">{error}</Banner>}
          {notice && <Banner tone="success">{notice}</Banner>}
          {activeSection === "Home" && (
            <>
              <PageTitle
                kicker="YOUR WORKSPACE"
                title={company?.name ?? "Overview"}
                description="A clear view of the work your team is planning."
              />
              <div className="stats-grid">
                <div className="stat-card">
                  <strong>{organizations.length}</strong>
                  <span>Organizations</span>
                </div>
                <div className="stat-card">
                  <strong>{jobs.length}</strong>
                  <span>Jobs</span>
                </div>
                <div className="stat-card">
                  <strong>{events.length}</strong>
                  <span>Events</span>
                </div>
              </div>
              <section className="feature-card">
                <div>
                  <span className="eyebrow">START HERE</span>
                  <h2>Plan the work, then bring your team in.</h2>
                  <p>
                    Create a client organization, add a Job, and schedule its
                    Event. Staff can join through an invitation.
                  </p>
                  <button
                    className="primary-button"
                    onClick={() =>
                      setActiveSection(
                        organizations.length ? "Work" : "Organizations",
                      )
                    }
                  >
                    {organizations.length
                      ? "Plan a Job"
                      : "Add an organization"}
                  </button>
                </div>
              </section>
              <div className="two-column">
                <section className="surface">
                  <h2>Upcoming Events</h2>
                  {upcoming.map((item) => (
                    <div className="list-row" key={item.id}>
                      <div>
                        <strong>{item.name}</strong>
                        <small>
                          {jobs.find((job) => job.id === item.jobId)?.name ??
                            "Job"}
                        </small>
                      </div>
                      <span>{dateLabel(item.startsAtUtc)}</span>
                    </div>
                  ))}
                  {upcoming.length === 0 && (
                    <Empty title="No upcoming Events">
                      Create a Job, then add a scheduled Event.
                    </Empty>
                  )}
                </section>
                <section className="surface">
                  <h2>Field operations</h2>
                  <p className="surface-copy">
                    Pilot Capture works offline. HQ will receive capture
                    activity when synchronization is added. No field device
                    needs continuous internet to photograph subjects.
                  </p>
                </section>
              </div>
            </>
          )}
          {activeSection === "Organizations" && (
            <OrganizationsPage
              items={organizations}
              busy={busy}
              create={(body, done) =>
                void create<Organization>(
                  "/api/organizations",
                  body,
                  done,
                  "Organization added.",
                )
              }
            />
          )}
          {activeSection === "Work" && (
            <WorkPage
              organizations={organizations}
              jobs={jobs}
              events={events}
              busy={busy}
              createJob={(body, done) =>
                void create<Job>("/api/jobs", body, done, "Job created.")
              }
              createEvent={(body, done) =>
                void create<Event>("/api/events", body, done, "Event created.")
              }
            />
          )}
          {activeSection === "Staff" && (
            <StaffPage
              items={staff}
              invitations={invitations}
              allowed={staffAllowed}
              localTools={localTools}
              busy={busy}
              create={(body, done) =>
                void create<Invitation>(
                  "/api/staff/invitations",
                  body,
                  done,
                  "Invitation created.",
                )
              }
              revoke={(id) =>
                void submit(async () => {
                  await post(`/api/staff/invitations/${id}/revoke`, {});
                  await loadData();
                  setNotice("Invitation revoked.");
                })
              }
              localLink={async (id) => {
                const delivery = await get<LocalInvitation>(
                  `/api/dev/invitations/${id}`,
                );
                return `${window.location.origin}/#invite=${encodeURIComponent(id)}&token=${encodeURIComponent(delivery.token)}`;
              }}
              reportError={setError}
            />
          )}
          {(activeSection === "People" || activeSection === "Devices") && (
            <>
              <PageTitle
                kicker="COMING LATER"
                title={labels[activeSection]}
                description={
                  activeSection === "People"
                    ? "Subject records and roster review will connect to Events and Pilot Capture."
                    : "Capture station assignments and sync status will appear here."
                }
              />
              <div className="surface">
                <Empty title={`${labels[activeSection]} is on the roadmap`}>
                  This module will be built after the core planning and account
                  workflow.
                </Empty>
              </div>
            </>
          )}
          {dataLoading && (
            <p className="loading-inline" role="status">
              Refreshing company data…
            </p>
          )}
        </div>
      </main>
    </div>
  );
}

function OrganizationsPage({
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

function WorkPage({
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

function StaffPage({
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
