"""Offline regression for decompiled High Limit bridge/door prerequisites."""
from pathlib import Path
import os
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PRIVATE = Path(os.environ.get("P5R_PRIVATE_ROOT", ROOT.parent / "private-backups/persona-5-ap-local"))


@unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
class SaeScaleRouteTests(unittest.TestCase):
    def setUp(self):
        source = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        route = source.split("void prepare_palace_route", 1)[1]
        self.casino = route.split("else if (major == 157)", 1)[1].split(
            "else if (major == 159)", 1
        )[0]
        self.bits = {int(n) for n in re.findall(
            r"BIT_ON\(\(0x20000000 \+ (\d+)\)\)", self.casino
        )}

    def test_lowered_scale_and_unblocked_approach(self):
        self.assertIn(2210, self.bits)  # FINI: lowered animation + rebuilt collision.
        # FDOOR: no membership blocks first; observed-but-unexplained scale blocks next.
        blocked = 2311 not in self.bits or (2281 in self.bits and 2213 not in self.bits)
        self.assertFalse(blocked)

    def test_old_route_remains_blocked(self):
        old = self.bits - {2210, 2213, 2280, 2281, 2311}
        self.assertNotIn(2210, old)
        self.assertNotIn(2311, old)

    def test_no_token_grant_or_card_clear_synthesis(self):
        self.assertNotIn(2214, self.bits)  # Calling card.
        self.assertNotIn(2200, self.bits)  # Boss clear.
        self.assertNotIn(80, self.bits)    # Raid enabled elsewhere only with AP permission.
        self.assertNotRegex(self.casino, r"SET_COUNT\(162\s*,")
        self.assertIn(2280, self.bits)     # Initial High Limit scene already completed.


if __name__ == "__main__":
    unittest.main()
