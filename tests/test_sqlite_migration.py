import pathlib
import sqlite3
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
MIGRATION = ROOT / "src" / "PilotCapture.Infrastructure" / "Persistence" / "Migrations" / "0001_initial.sql"


class InitialMigrationTests(unittest.TestCase):
    def setUp(self):
        self.connection = sqlite3.connect(":memory:")
        self.connection.execute("PRAGMA foreign_keys = ON")
        self.connection.execute(
            "CREATE TABLE schema_migrations (version INTEGER NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL)"
        )
        self.connection.executescript(MIGRATION.read_text(encoding="utf-8"))

    def tearDown(self):
        self.connection.close()

    def test_migration_creates_expected_tables_and_valid_foreign_keys(self):
        expected = {
            "events", "groups", "subjects", "memberships", "photographers",
            "local_installation", "capture_profiles", "capture_sessions",
            "capture_sets", "image_assets", "capture_images", "roster_imports",
            "audit_entries", "schema_migrations",
        }
        actual = {
            row[0]
            for row in self.connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'table'"
            )
        }
        self.assertTrue(expected.issubset(actual))
        self.assertEqual([], self.connection.execute("PRAGMA foreign_key_check").fetchall())

    def test_capture_membership_must_belong_to_the_captured_subject(self):
        ids = {key: key * 26 for key in "ABCDEFGH"}
        self.connection.execute(
            "INSERT INTO events(id, name, created_at_utc) VALUES (?, ?, ?)",
            (ids["A"], "Pilot event", "2026-09-28T00:00:00Z"),
        )
        self.connection.execute(
            "INSERT INTO groups(id, event_id, name, created_at_utc) VALUES (?, ?, ?, ?)",
            (ids["B"], ids["A"], "Team A", "2026-09-28T00:00:00Z"),
        )
        for subject_id in (ids["C"], ids["D"]):
            self.connection.execute(
                "INSERT INTO subjects(id, event_id, display_name, created_at_utc) VALUES (?, ?, ?, ?)",
                (subject_id, ids["A"], "Rostered subject", "2026-09-28T00:00:00Z"),
            )
        self.connection.execute(
            "INSERT INTO memberships(id, subject_id, group_id, created_at_utc) VALUES (?, ?, ?, ?)",
            (ids["E"], ids["C"], ids["B"], "2026-09-28T00:00:00Z"),
        )
        self.connection.execute(
            "INSERT INTO capture_profiles(id, name, workflow_type, created_at_utc) VALUES (?, ?, ?, ?)",
            (ids["F"], "Portrait", 0, "2026-09-28T00:00:00Z"),
        )
        self.connection.execute(
            "INSERT INTO photographers(id, display_name, created_at_utc) VALUES (?, ?, ?)",
            (ids["G"], "Pilot photographer", "2026-09-28T00:00:00Z"),
        )
        self.connection.execute(
            "INSERT INTO local_installation(installation_id, station_code, created_at_utc) VALUES (?, ?, ?)",
            (ids["H"], "s10", "2026-09-28T00:00:00Z"),
        )
        session_id = "I" * 26
        self.connection.execute(
            "INSERT INTO capture_sessions(id, event_id, capture_profile_id, photographer_id, installation_id, station_code, started_at_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
            (session_id, ids["A"], ids["F"], ids["G"], ids["H"], "s10", "2026-09-28T00:00:00Z"),
        )
        with self.assertRaises(sqlite3.IntegrityError):
            self.connection.execute(
                "INSERT INTO capture_sets(id, capture_session_id, subject_id, membership_id, started_at_utc) VALUES (?, ?, ?, ?, ?)",
                ("J" * 26, session_id, ids["D"], ids["E"], "2026-09-28T00:00:00Z"),
            )

    def test_station_codes_are_limited_to_the_agreed_values(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.connection.execute(
                "INSERT INTO local_installation(installation_id, station_code, created_at_utc) VALUES (?, ?, ?)",
                ("K" * 26, "s50", "2026-09-28T00:00:00Z"),
            )


if __name__ == "__main__":
    unittest.main()
