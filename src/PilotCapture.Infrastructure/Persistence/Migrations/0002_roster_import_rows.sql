CREATE TABLE roster_import_rows (
    id TEXT NOT NULL PRIMARY KEY CHECK(length(id) = 26),
    roster_import_id TEXT NOT NULL,
    source_record_number INTEGER NOT NULL CHECK(source_record_number > 0),
    membership_id TEXT NULL,
    source_data_json TEXT NOT NULL,
    FOREIGN KEY(roster_import_id) REFERENCES roster_imports(id) ON DELETE RESTRICT,
    FOREIGN KEY(membership_id) REFERENCES memberships(id) ON DELETE RESTRICT,
    UNIQUE(roster_import_id, source_record_number)
);
CREATE INDEX ix_roster_import_rows_membership_id ON roster_import_rows(membership_id);
