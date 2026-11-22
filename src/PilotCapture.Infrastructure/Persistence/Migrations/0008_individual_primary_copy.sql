-- Independent acknowledgements prevent recreating either copy after a downstream watcher consumes it.
ALTER TABLE fast_capture_images ADD COLUMN individual_delivered_path TEXT NULL;
-- Existing accepted non-primary deliveries already live in the player folder.
UPDATE fast_capture_images SET individual_delivered_path = delivered_path, delivered_path = NULL
WHERE is_rejected = 0 AND is_primary = 0
AND job_id IN (SELECT id FROM fast_capture_jobs WHERE job_type = 2);
-- Add the newly required player-folder copy for existing primaries on the next scan.
UPDATE fast_capture_images SET needs_delivery = 1
WHERE is_rejected = 0 AND is_primary = 1
AND job_id IN (SELECT id FROM fast_capture_jobs WHERE job_type = 2);
