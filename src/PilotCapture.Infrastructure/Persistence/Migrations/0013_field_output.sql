ALTER TABLE fast_capture_jobs ADD COLUMN output_version INTEGER NOT NULL DEFAULT 1;
ALTER TABLE fast_capture_jobs ADD COLUMN station_code TEXT NOT NULL DEFAULT '10';
ALTER TABLE fast_capture_jobs ADD COLUMN current_color TEXT NOT NULL DEFAULT 'None';
ALTER TABLE fast_capture_teams ADD COLUMN player_slots INTEGER NOT NULL DEFAULT 25;
ALTER TABLE fast_capture_images ADD COLUMN file_identity TEXT NOT NULL DEFAULT '';
ALTER TABLE fast_capture_images ADD COLUMN color_name TEXT NOT NULL DEFAULT 'None';
ALTER TABLE local_installation ADD COLUMN custom_colors_json TEXT NOT NULL DEFAULT '[]';
ALTER TABLE capture_images ADD COLUMN secondary_opt_out INTEGER NOT NULL DEFAULT 0;
CREATE TABLE capture_file_serials (
    serial INTEGER PRIMARY KEY AUTOINCREMENT,
    image_key TEXT NOT NULL UNIQUE,
    station TEXT NOT NULL
);
