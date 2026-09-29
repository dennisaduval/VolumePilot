import { useEffect, useState } from 'react';

type Section = 'Home' | 'Work' | 'Organizations' | 'People' | 'Devices' | 'Settings';

const sections: Section[] = ['Home', 'Work', 'Organizations', 'People', 'Devices', 'Settings'];

const sectionDetails: Record<Exclude<Section, 'Home'>, { title: string; description: string }> = {
  Work: {
    title: 'Work',
    description: 'Jobs and Events will be organized here as the planning tools are added.',
  },
  Organizations: {
    title: 'Organizations',
    description: 'Client organizations and their teams, programs, and other units will be managed here.',
  },
  People: {
    title: 'People',
    description: 'Subjects, group memberships, and identity review will be managed here.',
  },
  Devices: {
    title: 'Devices',
    description: 'Pilot Capture installations, assignments, and synchronization status will be shown here.',
  },
  Settings: {
    title: 'Settings',
    description: 'Company account, staff access, and preferences will be configured here.',
  },
};

function SectionIcon({ section }: { section: Section }) {
  const glyphs: Record<Section, string> = {
    Home: 'H',
    Work: 'W',
    Organizations: 'O',
    People: 'P',
    Devices: 'D',
    Settings: 'S',
  };

  return <span className="nav-icon" aria-hidden="true">{glyphs[section]}</span>;
}

export function App() {
  const [activeSection, setActiveSection] = useState<Section>('Home');
  const [apiStatus, setApiStatus] = useState<'checking' | 'connected' | 'unavailable'>('checking');

  useEffect(() => {
    const controller = new AbortController();

    fetch('/api/health', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error('The HQ API returned an unsuccessful response.');
        }
        setApiStatus('connected');
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return;
        }
        setApiStatus('unavailable');
      });

    return () => controller.abort();
  }, []);

  return (
    <div className="app-shell">
      <aside className="sidebar" aria-label="Main navigation">
        <a className="brand" href="#home" onClick={() => setActiveSection('Home')} aria-label="VolumePilot HQ home">
          <span className="brand-name"><span>VOLUME</span><strong>PILOT</strong></span>
          <span className="brand-tagline">MORE PHOTOS. LESS CHAOS.</span>
          <span className="brand-product">HQ</span>
        </a>

        <div className="workspace-switcher">
          <span className="workspace-avatar" aria-hidden="true">VP</span>
          <span className="workspace-name">Your workspace</span>
          <span className="chevron" aria-hidden="true">⌄</span>
        </div>

        <nav className="nav-list">
          <span className="nav-label">WORKSPACE</span>
          {sections.map((section) => (
            <button
              className={`nav-item${activeSection === section ? ' is-active' : ''}`}
              key={section}
              type="button"
              aria-current={activeSection === section ? 'page' : undefined}
              onClick={() => setActiveSection(section)}
            >
              <SectionIcon section={section} />
              <span>{section}</span>
              {section === 'Work' && <span className="nav-count">0</span>}
            </button>
          ))}
        </nav>

        <div className="sidebar-footer">
          <span className={`status-dot status-${apiStatus}`} aria-hidden="true" />
          <span>HQ API</span>
          <span className="status-text" aria-live="polite">
            {apiStatus === 'checking' ? 'Checking' : apiStatus === 'connected' ? 'Connected' : 'Unavailable'}
          </span>
        </div>
      </aside>

      <main className="main-area">
        <header className="topbar">
          <div className="breadcrumb"><span>VolumePilot</span><span className="breadcrumb-separator">/</span><strong>{activeSection}</strong></div>
          <div className="topbar-actions">
            <span className="environment-label">PREVIEW</span>
            <button className="profile-button" type="button" aria-label="Profile menu">DD</button>
          </div>
        </header>

        {activeSection === 'Home' ? (
          <div className="page-content">
            <div className="page-heading">
              <div>
                <p className="eyebrow">YOUR STUDIO WORKSPACE</p>
                <h1>Good evening</h1>
                <p className="page-subtitle">Your work will take shape here, from planning through delivery.</p>
              </div>
              <span className="date-chip">HQ · 0.1 FOUNDATION</span>
            </div>

            <section className="welcome-card" aria-labelledby="welcome-title">
              <div className="welcome-copy">
                <span className="welcome-kicker">WELCOME TO VOLUMEPILOT HQ</span>
                <h2 id="welcome-title">More photos.<br /><span>Less chaos.</span></h2>
                <p>Bring organizations, events, rosters, and field operations into one clear workflow.</p>
              </div>
              <div className="formation-art" aria-hidden="true">
                <div className="flight-path path-one" />
                <div className="flight-path path-two" />
                <div className="flight-path path-three" />
                <div className="formation-lead" />
                <div className="formation-wing wing-one" />
                <div className="formation-wing wing-two" />
                <div className="formation-wing wing-three" />
              </div>
            </section>

            <div className="section-heading-row">
              <div>
                <h2>Get your workspace ready</h2>
                <p>Start with your client organizations, then prepare a Job and Event.</p>
              </div>
              <span className="step-count">FIRST STEPS</span>
            </div>

            <div className="setup-grid">
              <button className="setup-card" type="button" onClick={() => setActiveSection('Organizations')}>
                <span className="step-number">01</span>
                <span className="setup-icon organization-icon" aria-hidden="true">O</span>
                <span className="setup-card-title">Add an organization</span>
                <span className="setup-card-description">Set up a school, league, club, or other client.</span>
                <span className="card-arrow" aria-hidden="true">↗</span>
              </button>
              <button className="setup-card" type="button" onClick={() => setActiveSection('Work')}>
                <span className="step-number">02</span>
                <span className="setup-icon work-icon" aria-hidden="true">W</span>
                <span className="setup-card-title">Plan a Job and Event</span>
                <span className="setup-card-description">Organize booked work by date and location.</span>
                <span className="card-arrow" aria-hidden="true">↗</span>
              </button>
              <div className="setup-card setup-card-muted" aria-label="Capture setup comes after event planning">
                <span className="step-number">03</span>
                <span className="setup-icon capture-icon" aria-hidden="true">C</span>
                <span className="setup-card-title">Prepare Capture</span>
                <span className="setup-card-description">Publish an event plan to your field devices.</span>
                <span className="coming-label">AFTER EVENT SETUP</span>
              </div>
            </div>

            <div className="bottom-note">
              <span className="note-icon" aria-hidden="true">i</span>
              <p>Pilot Capture will keep working when the internet is unavailable. HQ prepares the work and receives updates when devices reconnect.</p>
            </div>
          </div>
        ) : (
          <div className="page-content section-placeholder">
            <div className="page-heading">
              <div>
                <p className="eyebrow">VOLUMEPILOT HQ</p>
                <h1>{sectionDetails[activeSection].title}</h1>
                <p className="page-subtitle">{sectionDetails[activeSection].description}</p>
              </div>
              <span className="date-chip">0.1 FOUNDATION</span>
            </div>
            <section className="empty-state" aria-labelledby="empty-title">
              <span className="empty-state-mark" aria-hidden="true">{activeSection.slice(0, 1)}</span>
              <h2 id="empty-title">{activeSection} is on the flight plan</h2>
              <p>This part of HQ will be built in the next increments. The workflow and screen requirements are documented in the project architecture.</p>
              <button className="secondary-button" type="button" onClick={() => setActiveSection('Home')}>Back to Home</button>
            </section>
          </div>
        )}
      </main>
    </div>
  );
}
