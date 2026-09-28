CREATE TABLE events (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    name TEXT NOT NULL CHECK(length(name) <= 200),
    event_date TEXT NULL,
    created_at_utc TEXT NOT NULL,
    is_archived INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE groups (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    event_id TEXT NOT NULL,
    name TEXT NOT NULL CHECK(length(name) <= 200),
    external_key TEXT NULL CHECK(external_key IS NULL OR length(external_key) <= 200),
    created_at_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY(event_id) REFERENCES events(id) ON DELETE RESTRICT
);
CREATE INDEX ix_groups_event_id_name ON groups(event_id, name);

CREATE TABLE subjects (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    event_id TEXT NOT NULL,
    identity_status INTEGER NOT NULL DEFAULT 0 CHECK(identity_status IN (0, 1)),
    first_name TEXT NULL CHECK(first_name IS NULL OR length(first_name) <= 100),
    last_name TEXT NULL CHECK(last_name IS NULL OR length(last_name) <= 100),
    display_name TEXT NOT NULL CHECK(length(display_name) <= 250),
    external_key TEXT NULL CHECK(external_key IS NULL OR length(external_key) <= 200),
    created_at_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY(event_id) REFERENCES events(id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX ix_subjects_event_id_external_key ON subjects(event_id, external_key);

CREATE TABLE memberships (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    subject_id TEXT NOT NULL,
    group_id TEXT NOT NULL,
    roster_import_id TEXT NULL,
    roster_number TEXT NULL CHECK(roster_number IS NULL OR length(roster_number) <= 50),
    role TEXT NULL CHECK(role IS NULL OR length(role) <= 100),
    source_row_key TEXT NULL CHECK(source_row_key IS NULL OR length(source_row_key) <= 200),
    source_data_json TEXT NULL,
    created_at_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY(subject_id) REFERENCES subjects(id) ON DELETE RESTRICT,
    FOREIGN KEY(group_id) REFERENCES groups(id) ON DELETE RESTRICT,
    FOREIGN KEY(roster_import_id) REFERENCES roster_imports(id) ON DELETE RESTRICT,
    UNIQUE(subject_id, group_id),
    UNIQUE(id, subject_id)
);
CREATE INDEX ix_memberships_group_id ON memberships(group_id);
CREATE INDEX ix_memberships_roster_import_id ON memberships(roster_import_id);

CREATE TABLE photographers (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    display_name TEXT NOT NULL CHECK(length(display_name) <= 200),
    external_key TEXT NULL CHECK(external_key IS NULL OR length(external_key) <= 200),
    created_at_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE local_installation (
    installation_id TEXT NOT NULL PRIMARY KEY CHECK(length(installation_id) = 26),
    station_code TEXT NOT NULL DEFAULT 's10' CHECK(station_code IN ('s10', 's20', 's30', 's40')),
    created_at_utc TEXT NOT NULL
);

CREATE TABLE capture_profiles (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    name TEXT NOT NULL CHECK(length(name) <= 200),
    workflow_type INTEGER NOT NULL CHECK(workflow_type IN (0, 1)),
    description TEXT NULL,
    created_at_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE capture_sessions (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    event_id TEXT NOT NULL,
    capture_profile_id TEXT NOT NULL,
    photographer_id TEXT NOT NULL,
    installation_id TEXT NOT NULL CHECK(length(installation_id) = 26),
    station_code TEXT NOT NULL CHECK(station_code IN ('s10', 's20', 's30', 's40')),
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NULL,
    FOREIGN KEY(event_id) REFERENCES events(id) ON DELETE RESTRICT,
    FOREIGN KEY(capture_profile_id) REFERENCES capture_profiles(id) ON DELETE RESTRICT,
    FOREIGN KEY(photographer_id) REFERENCES photographers(id) ON DELETE RESTRICT,
    FOREIGN KEY(installation_id) REFERENCES local_installation(installation_id) ON DELETE RESTRICT
);
CREATE INDEX ix_capture_sessions_event_id_started_at_utc ON capture_sessions(event_id, started_at_utc);
CREATE INDEX ix_capture_sessions_capture_profile_id ON capture_sessions(capture_profile_id);
CREATE INDEX ix_capture_sessions_photographer_id ON capture_sessions(photographer_id);
CREATE INDEX ix_capture_sessions_installation_id ON capture_sessions(installation_id);

CREATE TABLE capture_sets (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    capture_session_id TEXT NOT NULL,
    subject_id TEXT NOT NULL,
    membership_id TEXT NULL,
    started_at_utc TEXT NOT NULL,
    FOREIGN KEY(capture_session_id) REFERENCES capture_sessions(id) ON DELETE RESTRICT,
    FOREIGN KEY(subject_id) REFERENCES subjects(id) ON DELETE RESTRICT,
    FOREIGN KEY(membership_id, subject_id) REFERENCES memberships(id, subject_id) ON DELETE RESTRICT
);
CREATE INDEX ix_capture_sets_capture_session_id_subject_id ON capture_sets(capture_session_id, subject_id);
CREATE INDEX ix_capture_sets_subject_id ON capture_sets(subject_id);
CREATE INDEX ix_capture_sets_membership_id_subject_id ON capture_sets(membership_id, subject_id);

CREATE TABLE image_assets (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    relative_path TEXT NOT NULL CHECK(length(relative_path) <= 500),
    original_file_name TEXT NOT NULL CHECK(length(original_file_name) <= 255),
    media_type TEXT NOT NULL DEFAULT 'image/jpeg' CHECK(length(media_type) <= 100),
    byte_length INTEGER NOT NULL CHECK(byte_length >= 0),
    sha256 TEXT NULL CHECK(sha256 IS NULL OR length(sha256) = 64),
    pixel_width INTEGER NULL CHECK(pixel_width IS NULL OR pixel_width > 0),
    pixel_height INTEGER NULL CHECK(pixel_height IS NULL OR pixel_height > 0),
    imported_at_utc TEXT NOT NULL,
    state INTEGER NOT NULL DEFAULT 0 CHECK(state IN (0, 1, 2, 3)),
    UNIQUE(relative_path)
);
CREATE INDEX ix_image_assets_sha256 ON image_assets(sha256);

CREATE TABLE capture_images (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    capture_set_id TEXT NOT NULL,
    image_asset_id TEXT NOT NULL UNIQUE,
    review_state INTEGER NOT NULL DEFAULT 0 CHECK(review_state IN (0, 1, 2, 3)),
    sequence_number INTEGER NOT NULL CHECK(sequence_number >= 0),
    captured_at_utc TEXT NOT NULL,
    FOREIGN KEY(capture_set_id) REFERENCES capture_sets(id) ON DELETE RESTRICT,
    FOREIGN KEY(image_asset_id) REFERENCES image_assets(id) ON DELETE RESTRICT,
    UNIQUE(capture_set_id, sequence_number)
);
CREATE INDEX ix_capture_images_capture_set_id ON capture_images(capture_set_id);

CREATE TABLE roster_imports (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    event_id TEXT NOT NULL,
    source_file_name TEXT NOT NULL CHECK(length(source_file_name) <= 255),
    source_sha256 TEXT NOT NULL CHECK(length(source_sha256) = 64),
    imported_at_utc TEXT NOT NULL,
    rows_read INTEGER NOT NULL CHECK(rows_read >= 0),
    rows_imported INTEGER NOT NULL CHECK(rows_imported >= 0),
    rows_skipped INTEGER NOT NULL CHECK(rows_skipped >= 0),
    FOREIGN KEY(event_id) REFERENCES events(id) ON DELETE RESTRICT
);
CREATE INDEX ix_roster_imports_event_id ON roster_imports(event_id);

CREATE TABLE audit_entries (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    entity_type TEXT NOT NULL CHECK(length(entity_type) <= 100),
    entity_id TEXT NOT NULL CHECK(length(entity_id) = 26),
    action TEXT NOT NULL CHECK(length(action) <= 100),
    installation_id TEXT NOT NULL CHECK(length(installation_id) = 26),
    station_code TEXT NOT NULL CHECK(station_code IN ('s10', 's20', 's30', 's40')),
    correlation_id TEXT NULL CHECK(correlation_id IS NULL OR length(correlation_id) = 26),
    details_json TEXT NULL,
    occurred_at_utc TEXT NOT NULL,
    FOREIGN KEY(installation_id) REFERENCES local_installation(installation_id) ON DELETE RESTRICT
);
CREATE INDEX ix_audit_entries_entity_type_entity_id_occurred_at_utc ON audit_entries(entity_type, entity_id, occurred_at_utc);
CREATE INDEX ix_audit_entries_correlation_id ON audit_entries(correlation_id);
CREATE INDEX ix_audit_entries_installation_id ON audit_entries(installation_id);
