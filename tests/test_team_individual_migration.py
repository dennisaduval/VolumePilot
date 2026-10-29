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

class FastCaptureMigrationTests(unittest.TestCase):
    def test_action_timing_upgrade_preserves_existing_jobs_and_enforces_receipt_identity(self):
        db = sqlite3.connect(':memory:')
        db.execute('PRAGMA foreign_keys=ON')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            if path.name < '0012':
                db.executescript(path.read_text())
        db.execute("INSERT INTO fast_capture_jobs(id,name,job_type,source_folder,output_folder,started_at_ticks) VALUES ('job','Action',3,'source','output',0)")
        db.execute("INSERT INTO fast_capture_teams(id,job_id,name,folder_name,current_player) VALUES ('team','job','A','Old_A',1)")
        db.executescript((MIGRATIONS / '0012_action_capture_timing.sql').read_text())
        self.assertEqual((0,), db.execute('SELECT action_timing_enabled FROM fast_capture_jobs').fetchone())
        self.assertEqual(('Old_A',), db.execute('SELECT folder_name FROM fast_capture_teams').fetchone())
        db.execute("INSERT INTO action_receipts(id,job_id,source_path,path_key,source_ticks,source_length,stored_path,sha256,state,issue) VALUES ('photo','job','source.jpg','SOURCE.JPG',1,4,'archive.jpg','hash','Held','metadata')")
        with self.assertRaises(sqlite3.IntegrityError):
            db.execute("INSERT INTO action_receipts(id,job_id,source_path,path_key,source_ticks,source_length,stored_path,sha256,state,issue) VALUES ('other','job','source.jpg','SOURCE.JPG',1,4,'archive.jpg','hash','Held','metadata')")
        self.assertEqual([], db.execute('PRAGMA foreign_key_check').fetchall())
        db.close()

    def test_day_hall_upgrade_retains_previous_output_and_unscoped_schedule(self):
        db = sqlite3.connect(':memory:')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            if path.name < '0011':
                db.executescript(path.read_text())
        db.execute("INSERT INTO fast_capture_jobs(id,name,job_type,source_folder,output_folder,started_at_ticks) VALUES ('job','Test',2,'source','output',0)")
        db.execute("INSERT INTO fast_capture_teams(id,job_id,name,folder_name,current_player) VALUES ('team','job','Falcons','Old_Falcons',7)")
        db.executescript((MIGRATIONS / '0011_fast_schedule_groups.sql').read_text())
        self.assertEqual(('Old_Falcons', 7, '', ''), db.execute('SELECT folder_name,current_player,day,hall FROM fast_capture_teams').fetchone())
        self.assertEqual([], db.execute('PRAGMA foreign_key_check').fetchall())
        db.close()

    def test_fast_jobs_upgrade_and_unique_delivery_records(self):
        db = sqlite3.connect(':memory:')
        db.execute('PRAGMA foreign_keys=ON')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            db.executescript(path.read_text())
        db.execute("INSERT INTO fast_capture_jobs(id,name,job_type,source_folder,output_folder,individuals_folder,current_team_id,started_at_ticks) VALUES ('job','Test',2,'source','output','individuals',NULL,0)")
        db.execute("INSERT INTO fast_capture_teams(id,job_id,name,folder_name,current_player) VALUES ('team','job','Falcons','Falcons',1)")
        row = ('image','job','team',1,1,'source.jpg',1,4,'local.jpg','hash',1,0,0,1,None)
        db.execute('INSERT INTO fast_capture_images(id,job_id,team_id,player_number,sequence,source_path,source_ticks,source_length,stored_path,sha256,is_primary,is_rejected,released,needs_delivery,delivered_path) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',row)
        with self.assertRaises(sqlite3.IntegrityError):
            db.execute('INSERT INTO fast_capture_images(id,job_id,team_id,player_number,sequence,source_path,source_ticks,source_length,stored_path,sha256,is_primary,is_rejected,released,needs_delivery,delivered_path) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',('other',)+row[1:])
        self.assertEqual([],db.execute('PRAGMA foreign_key_check').fetchall())
        self.assertEqual(1,db.execute('SELECT current_player FROM fast_capture_teams').fetchone()[0])
        db.close()

    def test_primary_copy_upgrade_preserves_existing_acknowledgements(self):
        db = sqlite3.connect(':memory:')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            if path.name < '0008':
                db.executescript(path.read_text())
        db.execute("INSERT INTO fast_capture_jobs(id,name,job_type,source_folder,output_folder,individuals_folder,current_team_id,started_at_ticks) VALUES ('job','Test',2,'source','output','individuals',NULL,0)")
        db.execute("INSERT INTO fast_capture_teams(id,job_id,name,folder_name,current_player) VALUES ('team','job','Falcons','Falcons',1)")
        for n, primary, rejected, path in [(1,1,0,'teams/primary.jpg'), (2,0,0,'players/other.jpg'), (3,0,1,'Rejects/rejected.jpg')]:
            db.execute('INSERT INTO fast_capture_images(id,job_id,team_id,player_number,sequence,source_path,source_ticks,source_length,stored_path,sha256,is_primary,is_rejected,released,needs_delivery,delivered_path) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',
                       (str(n),'job','team',1,n,str(n)+'.jpg',1,4,'local.jpg','hash',primary,rejected,1,0,path))
        db.executescript((MIGRATIONS / '0008_individual_primary_copy.sql').read_text())
        rows = db.execute('SELECT delivered_path, individual_delivered_path, needs_delivery FROM fast_capture_images ORDER BY id').fetchall()
        self.assertEqual([('teams/primary.jpg',None,1), (None,'players/other.jpg',0), ('Rejects/rejected.jpg',None,0)], rows)
        db.close()

    def test_performance_details_upgrade_preserves_existing_names_and_deliveries(self):
        db = sqlite3.connect(':memory:')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            if path.name < '0009':
                db.executescript(path.read_text())
        db.execute("INSERT INTO fast_capture_jobs(id,name,job_type,source_folder,output_folder,current_team_id,started_at_ticks) VALUES ('job','Test',3,'source','output',NULL,0)")
        db.execute("INSERT INTO fast_capture_teams(id,job_id,name,folder_name,current_player,performance_key,performance_label) VALUES ('team','job','Rockets / Junior','Rockets_Junior',1,'23','23')")
        db.executescript((MIGRATIONS / '0009_fast_performance_details.sql').read_text())
        self.assertEqual(('Rockets / Junior','Rockets_Junior','23',''), db.execute('SELECT name,folder_name,performance_label,details FROM fast_capture_teams').fetchone())
        self.assertEqual([], db.execute('PRAGMA foreign_key_check').fetchall())
        db.close()

    def test_primary_marker_upgrade_queues_only_accepted_multi_mode_primaries(self):
        db = sqlite3.connect(':memory:')
        for path in sorted(MIGRATIONS.glob('*.sql')):
            if path.name < '0010':
                db.executescript(path.read_text())
        db.execute("INSERT INTO fast_capture_jobs(id,name,job_type,source_folder,output_folder,current_team_id,started_at_ticks) VALUES ('job','Test',2,'source','output',NULL,0)")
        db.execute("INSERT INTO fast_capture_teams(id,job_id,name,folder_name,current_player) VALUES ('team','job','Rockets','023_Rockets',1)")
        for n, primary, rejected in [(1,1,0), (2,0,0), (3,0,1)]:
            db.execute('INSERT INTO fast_capture_images(id,job_id,team_id,player_number,sequence,source_path,source_ticks,source_length,stored_path,sha256,is_primary,is_rejected,released,needs_delivery,delivered_path,individual_delivered_path) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',
                       (str(n),'job','team',1,n,str(n)+'.jpg',1,4,'local.jpg','hash',primary,rejected,1,0,'team.jpg','individual.jpg'))
        db.executescript((MIGRATIONS / '0010_fast_primary_marker.sql').read_text())
        self.assertEqual([(1,), (0,), (0,)], db.execute('SELECT needs_delivery FROM fast_capture_images ORDER BY id').fetchall())
        self.assertEqual([('team.jpg','individual.jpg')]*3, db.execute('SELECT delivered_path,individual_delivered_path FROM fast_capture_images').fetchall())
        db.close()
