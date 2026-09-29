import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Banner, Empty, PageTitle, dateLabel } from "./ui";
import { OrganizationsPage, WorkPage, StaffPage } from "./WorkspacePages";
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
