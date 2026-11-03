"""Offline regression for the decompiled FINI_153_005 branch ordering.

This validates AP route preparation, not a live-game boss encounter.
"""
from pathlib import Path
import os
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PRIVATE = Path(os.environ.get("P5R_PRIVATE_ROOT", ROOT.parent / "private-backups/persona-5-ap-local"))
FLOW = PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow"


def boss_trigger(bits, position=2, counter=83000):
    # Original Central Garden initializer checks these story branches first.
    if 603 in bits and 604 not in bits:
        return False
    if 607 in bits and 608 not in bits:
        return False
    if 605 in bits and 611 not in bits:
        return False
    return 80 in bits and 600 not in bits and position == 2 and counter < 83050


@unittest.skipUnless(FLOW.is_file(), "Optional private game sources unavailable")
class MadarameRouteTests(unittest.TestCase):
    def setUp(self):
        source = FLOW.read_text()
        preparation = source.split("void prepare_palace_route", 1)[1]
        museum = preparation.split("else if (major == 153)", 1)[1].split(
            "else if (major == 154)", 1
        )[0]
        self.bits = {int(n) for n in re.findall(
            r"BIT_ON\(\(0x20000000 \+ (\d+)\)\)", museum
        )}
        self.assertIn("BIT_OFF((0x20000000 + 600))", museum)

    def test_missing_prerequisite_reproduces_block(self):
        self.assertFalse(boss_trigger((self.bits | {80}) - {611}))

    def test_prepared_raid_reaches_native_boss(self):
        self.assertTrue(boss_trigger(self.bits | {80}))

    def test_preparation_does_not_grant_card_or_raid(self):
        self.assertNotIn(609, self.bits)
        self.assertNotIn(80, self.bits)
        self.assertFalse(boss_trigger(self.bits))

    def test_native_completion_and_position_guards_remain(self):
        self.assertFalse(boss_trigger(self.bits | {80, 600}))
        self.assertFalse(boss_trigger(self.bits | {80}, counter=83050))
        self.assertFalse(boss_trigger(self.bits | {80}, position=0))


if __name__ == "__main__":
    unittest.main()
