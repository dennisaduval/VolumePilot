ALTER TABLE capture_sets ADD COLUMN completed_at_utc TEXT NULL;
ALTER TABLE capture_images ADD COLUMN is_primary INTEGER NOT NULL DEFAULT 0 CHECK(is_primary IN (0, 1));
ALTER TABLE capture_images ADD COLUMN is_banner INTEGER NOT NULL DEFAULT 0 CHECK(is_banner IN (0, 1));

UPDATE capture_images
SET is_primary = CASE WHEN review_state = 1 THEN 1 ELSE 0 END,
    is_banner = CASE WHEN review_state = 2 THEN 1 ELSE 0 END,
    review_state = CASE
        WHEN review_state = 3 THEN 2
        WHEN review_state IN (1, 2) THEN 1
        ELSE 0
    END;

UPDATE capture_images
SET is_primary = 0
WHERE is_primary = 1
  AND sequence_number > (
      SELECT MIN(candidate.sequence_number)
      FROM capture_images AS candidate
      WHERE candidate.capture_set_id = capture_images.capture_set_id
        AND candidate.is_primary = 1
  );

CREATE UNIQUE INDEX ix_capture_images_one_primary_per_set
ON capture_images(capture_set_id)
WHERE is_primary = 1;
