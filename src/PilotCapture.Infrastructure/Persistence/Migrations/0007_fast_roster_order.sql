ALTER TABLE fast_capture_jobs ADD COLUMN ignored_source_versions_json TEXT NULL;
ALTER TABLE fast_capture_teams ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;
ALTER TABLE fast_capture_teams ADD COLUMN performance_key TEXT NOT NULL DEFAULT '';
ALTER TABLE fast_capture_teams ADD COLUMN performance_label TEXT NOT NULL DEFAULT '';
DROP INDEX ix_fast_team_name;
DROP INDEX ix_fast_team_folder;
CREATE UNIQUE INDEX ix_fast_team_performance ON fast_capture_teams(job_id,name,performance_key);
CREATE TABLE fast_capture_roster_imports (id TEXT PRIMARY KEY NOT NULL, job_id TEXT NOT NULL REFERENCES fast_capture_jobs(id), source_name TEXT NOT NULL, sha256 TEXT NOT NULL, source_data_json TEXT NOT NULL, mapping_json TEXT NOT NULL);
