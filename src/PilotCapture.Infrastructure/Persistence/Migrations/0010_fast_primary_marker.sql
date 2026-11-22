-- Existing individual primary copies adopt the marker on the next job scan.
-- The delivery reconciler renames present files and never republishes consumed copies.
UPDATE fast_capture_images SET needs_delivery = 1
WHERE is_primary = 1 AND is_rejected = 0
AND job_id IN (SELECT id FROM fast_capture_jobs WHERE job_type = 2);
