ALTER TABLE local_installation ADD COLUMN smart_shooter_output_path TEXT NULL CHECK(smart_shooter_output_path IS NULL OR length(smart_shooter_output_path) <= 2000);
ALTER TABLE image_assets ADD COLUMN source_path TEXT COLLATE NOCASE NULL CHECK(source_path IS NULL OR length(source_path) <= 2000);
ALTER TABLE image_assets ADD COLUMN source_last_write_utc TEXT NULL;
CREATE UNIQUE INDEX ix_image_assets_source_path_last_write ON image_assets(source_path, source_last_write_utc) WHERE source_path IS NOT NULL AND source_last_write_utc IS NOT NULL;
