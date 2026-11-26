"""Stable behavioral regression fixtures independent of rendered JPEG bytes."""

from dataclasses import replace
from pathlib import Path
import unittest

from pilot_teambuilder.composition import compose
from pilot_teambuilder.model import CompositionSettings
from pilot_teambuilder.rows import RowTable
from pilot_teambuilder.synthetic import athletes


class RegressionTests(unittest.TestCase):
    def test_identical_nine_athletes_geometry(self):
        base=athletes(1)[0]
        people=[replace(base,id=f"{i:02}",relative_height=100-i) for i in range(9)]
        layout=compose(people,RowTable({9:(9,)}),CompositionSettings(v_step_in=0))
        self.assertEqual([p.athlete_id for p in sorted(layout.placements,key=lambda p:p.slot)],
                         ["07","05","03","01","00","02","04","06","08"])
        self.assertEqual(layout.placements[-1].athlete_id,"00")
        self.assertAlmostEqual(layout.global_scale,1800/base.body.height)
        self.assertAlmostEqual(min(p.body.top for p in layout.placements),225)
        self.assertAlmostEqual(max(p.body.bottom for p in layout.placements),2025)
        self.assertAlmostEqual(next(p for p in layout.placements if p.slot==4).head.center_x,3600)

    def test_head_spacing_safety_floor(self):
        people=athletes(16)
        layout=compose(people,RowTable({16:(16,)}),CompositionSettings(head_spacing_in=0.125))
        ordered=sorted(layout.placements,key=lambda p:p.head.center_x)
        self.assertTrue(all(left.head.right<right.head.left for left,right in zip(ordered,ordered[1:])))


if __name__ == "__main__":
    unittest.main()
