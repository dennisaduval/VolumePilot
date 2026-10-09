import pathlib
import sqlite3
import unittest

MIGRATIONS = pathlib.Path(__file__).resolve().parents[1] / 'src/PilotCapture.Infrastructure/Persistence/Migrations'


class TeamIndividualMigrationTests(unittest.TestCase):
    def test_upgrade_preserves_prior_choices_and_enforces_roles_across_visits(self):
        db = sqlite3.connect(':memory:')
        for path in sorted(MIGRATIONS.glob('000[1-4]*.sql')):
            db.executescript(path.read_text())
        # Scope migration itself can be exercised without unrelated seed data.
        db.execute('PRAGMA foreign_keys=OFF')
        for n in range(2):
            db.execute('INSERT INTO capture_sets(id,capture_session_id,subject_id,membership_id,started_at_utc) VALUES(?,?,?,?,?)',
                       (f'set{n}'.ljust(26,'s'), 'S'*26, 'P'*26, 'M'*26, '2026-10-09'))
        for n in range(4):
            db.execute('INSERT INTO capture_images(id,capture_set_id,image_asset_id,review_state,sequence_number,captured_at_utc,is_primary,is_banner) VALUES(?,?,?,?,?,?,?,?)',
                       (f'photo{n}'.ljust(26,'p'), f'set{n//2}'.ljust(26,'s'), f'asset{n}'.ljust(26,'a'), 2 if n == 3 else 1, n % 2, '2026-10-09', int(n in (0, 2)), int(n in (1, 2))))
        db.executescript((MIGRATIONS / '0005_team_individual.sql').read_text())
        rows = db.execute('SELECT selection_scope_id,is_primary,is_secondary,is_banner FROM capture_images ORDER BY id').fetchall()
        self.assertEqual([('M'*26,1,0,0), ('M'*26,0,1,1), ('M'*26,0,0,0), ('M'*26,0,0,0)], rows)
        for role in ('is_primary','is_secondary','is_banner'):
            with self.assertRaises(sqlite3.IntegrityError):
                db.execute(f'UPDATE capture_images SET {role}=1 WHERE id LIKE "photo2%"')
        columns = {r[1] for r in db.execute('PRAGMA table_info(image_assets)')}
        self.assertTrue({'edited_path','edited_sha256','edited_at_utc'} <= columns)
        db.close()

    def test_fresh_database_applies_all_migrations(self):
        db = sqlite3.connect(':memory:')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            db.executescript(path.read_text())
        self.assertEqual([], db.execute('PRAGMA foreign_key_check').fetchall())
        self.assertIn('master_media_path', {r[1] for r in db.execute('PRAGMA table_info(local_installation)')})
        db.close()
