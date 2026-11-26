from dataclasses import replace
import math
from pathlib import Path
import random
import tempfile
import unittest

from pilot_teambuilder.composition import center_out_slots, compose
from pilot_teambuilder.model import CompositionSettings, Corrections, Rect, ReviewRequired
from pilot_teambuilder.rows import RowTable
from pilot_teambuilder.synthetic import athletes

TABLE = Path(__file__).parents[1] / "src/pilot_teambuilder/data/row_counts.csv"


class CompositionTests(unittest.TestCase):
    def setUp(self):
        self.table = RowTable.load(TABLE)

    def test_authoritative_distributions(self):
        self.assertEqual(len(self.table.entries), 45)
        for n, expected in {17:(8,9), 33:(12,11,10), 34:(11,12,11),
                            39:(14,13,12), 42:(15,14,13), 45:(15,15,15)}.items():
            self.assertEqual(self.table.for_count(n), expected)

    def test_all_csv_sizes_and_extended_sizes_fit_and_preserve_scale(self):
        for count in list(range(1, 46)) + [46, 47, 48, 49, 64, 65, 100, 257]:
            with self.subTest(count=count):
                people = athletes(count)
                layout = compose(people, self.table)
                self.assertEqual(len(layout.placements), count)
                self.assertEqual(sum(layout.row_counts), count)
                self.assertLessEqual(max(layout.row_counts), 16)
                area = CompositionSettings().area
                self.assertTrue(all(area.contains(p.body) for p in layout.placements))
                self.assertTrue(all(math.isclose(p.scale, layout.global_scale) for p in layout.placements))
                self.assertEqual(len({p.athlete_id for p in layout.placements}), count)

    def test_pattern_extension(self):
        for n, rows in {46:(16,15,15),47:(16,16,15),48:(16,16,16),
                        49:(13,12,12,12),65:(13,13,13,13,13)}.items():
            self.assertEqual(self.table.for_count(n), rows)

    def test_missing_csv_entry_does_not_invent_layout(self):
        with self.assertRaisesRegex(ValueError, "No CSV"):
            RowTable({1:(1,), 3:(3,)}).for_count(2)

    def test_center_out_rank_examples(self):
        ranks = [0]*9
        for rank, slot in enumerate(center_out_slots(9), 1):
            ranks[slot] = rank
        self.assertEqual(ranks, [8,6,4,2,1,3,5,7,9])
        self.assertEqual(center_out_slots(8)[:2], (3,4))

    def test_height_rows_deterministic_and_layering(self):
        people = athletes(42)
        first = compose(people, self.table)
        random.Random(4).shuffle(people)
        self.assertEqual(first, compose(people, self.table))
        heights = {a.id:a.relative_height for a in people}
        for row in range(2):
            self.assertGreaterEqual(min(heights[p.athlete_id] for p in first.placements if p.row == row),
                                    max(heights[p.athlete_id] for p in first.placements if p.row == row+1))
        row = [p for p in first.placements if p.row == 0]
        self.assertEqual(row[-1].slot, 7)

    def test_resize_metadata_restores_common_capture_scale(self):
        a = athletes(1)[0]
        b = replace(a, id="second", image_size=(800,2000), source_scale=2,
                    body=Rect(*(v*2 for v in vars(a.body).values())),
                    head=Rect(*(v*2 for v in vars(a.head).values())))
        layout = compose([a,b], self.table)
        p = {p.athlete_id:p for p in layout.placements}
        self.assertAlmostEqual(p[a.id].body.height, p[b.id].body.height)
        self.assertAlmostEqual(p[a.id].scale, p[b.id].scale*2)

    def test_small_team_is_centered_without_spreading(self):
        layout = compose(athletes(2), self.table)
        left, right = min(p.body.left for p in layout.placements), max(p.body.right for p in layout.placements)
        self.assertAlmostEqual((left+right)/2, 3600)
        self.assertLess(right-left, 2000)

    def test_equal_rows_stagger_and_center_complete_group(self):
        people = [replace(athletes(1)[0], id=str(i)) for i in range(18)]
        layout = compose(people,self.table)
        centers = [[p.head.center_x for p in layout.placements if p.row==r] for r in (0,1)]
        spacing = sorted(centers[0])[1]-min(centers[0])
        self.assertAlmostEqual(min(centers[1])-min(centers[0]),spacing/2)
        self.assertAlmostEqual((min(p.body.left for p in layout.placements)+max(p.body.right for p in layout.placements))/2,3600)

    def test_odd_even_rows_center_without_stagger(self):
        people = [replace(athletes(1)[0],id=str(i)) for i in range(17)]
        layout = compose(people,self.table)
        for row in (0,1):
            centers = [p.head.center_x for p in layout.placements if p.row==row]
            self.assertAlmostEqual((min(centers)+max(centers))/2,3600)

    def test_inverted_v_and_row_separation(self):
        people = [replace(athletes(1)[0],id=str(i)) for i in range(18)]
        layout = compose(people,self.table)
        by_slot = {p.slot:p for p in layout.placements if p.row==0}
        self.assertLess(by_slot[4].head.top,by_slot[0].head.top)
        row_centers = [next(p for p in layout.placements if p.row==r and p.slot==4) for r in (0,1)]
        self.assertAlmostEqual(row_centers[1].head.top-row_centers[0].head.top,
                               row_centers[0].head.height*1.5)

    def test_uncertain_detections_block_without_omission(self):
        a = athletes(1)[0]
        for changes in ({"head":None},{"confidence":0.4},{"head_count":2},
                        {"source_scale":None},{"relative_height":None},{"source_scale":float("nan")}):
            with self.subTest(changes=changes), self.assertRaises(ReviewRequired):
                compose([replace(a,**changes)],self.table)

    def test_corrections_exclusion_and_restore(self):
        people = athletes(3)
        ids = tuple(a.id for a in people)
        corrected = compose(people,self.table,corrections=Corrections(rows=((ids[2],ids[0]),),excluded=(ids[1],)))
        self.assertEqual(corrected.row_counts,(2,))
        self.assertEqual(len(compose(people,self.table).placements),3)
        moved = compose(people,self.table,corrections=Corrections(offsets_in={ids[0]:(100,0)}))
        self.assertTrue(any("outside" in i for i in moved.issues))
        with self.assertRaises(ValueError):
            compose(people,self.table,corrections=Corrections(rows=((ids[0],ids[0],ids[2]),)))

    def test_wide_pose_all_transparent_bounds_fit(self):
        people = athletes(42)
        people[0] = replace(people[0], image_size=(1800,1000),body=Rect(0,100,1800,1000))
        layout = compose(people,self.table)
        self.assertTrue(all(CompositionSettings().area.contains(p.body) for p in layout.placements))

    def test_invalid_settings_and_csv(self):
        for setting in (CompositionSettings(width_in=-1),CompositionSettings(x_offset_in=20),
                        CompositionSettings(group_scale=float("nan")),CompositionSettings(left_in=-1)):
            with self.assertRaises(ValueError):
                compose(athletes(1),self.table,setting)
        for body in ("1,2,0,0,0", "1,0,1,0,0", "1,1,0,0,0\n1,1,0,0,0",
                     "17,17,0,0,0", "1,1,0,0,0,extra"):
            with tempfile.TemporaryDirectory() as folder:
                path=Path(folder)/"bad.csv"
                path.write_text("Athlete Count,Row 1,Row 2,Row 3,Row 4\n"+body)
                with self.assertRaises(ValueError):
                    RowTable.load(path)

    def test_collision_is_explicit_review_issue(self):
        layout = compose(athletes(9),self.table,CompositionSettings(head_spacing_in=0.125))
        self.assertTrue(any("obstruction" in issue for issue in layout.issues))


if __name__ == "__main__":
    unittest.main()
