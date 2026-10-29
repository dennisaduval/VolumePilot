-- Each appearance belongs to a day/hall. Existing jobs retain one ungrouped schedule.
ALTER TABLE fast_capture_teams ADD COLUMN day TEXT NOT NULL DEFAULT '';
ALTER TABLE fast_capture_teams ADD COLUMN hall TEXT NOT NULL DEFAULT '';
CREATE INDEX ix_fast_capture_teams_schedule ON fast_capture_teams(job_id, day, hall, sort_order);
