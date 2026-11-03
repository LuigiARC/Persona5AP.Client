from pathlib import Path
import os
import unittest

ROOT = Path(__file__).resolve().parents[2]
PRIVATE = Path(os.environ.get("P5R_PRIVATE_ROOT", ROOT.parent / "private-backups/persona-5-ap-local"))

class MementosPathTests(unittest.TestCase):
    def test_live_path_is_authoritative_even_for_zero(self):
        source = (ROOT / "src/P5RArchipelago/Native/MementosPathMonitor.cs").read_text()
        self.assertIn("GetDungeonNumberFunctionId = 0x2A;", source)
        live = source.split("public int CurrentPath", 1)[1].split("/// <summary>", 1)[0]
        self.assertIn("_getField()", live)
        self.assertIn("Marshal.ReadInt16(field + 0x1FE)", live)
        self.assertNotIn("return Volatile.Read", live)
        source = (ROOT / "src/P5RArchipelago/ArchipelagoMod.cs").read_text()
        current = source.split("private int GetCurrentMementosPath()", 1)[1].split("private long?", 1)[0]
        self.assertIn("_mementosPathMonitor is not null ? _mementosPathMonitor.CurrentPath", current)

    def test_transit_block_is_reserved_not_reused(self):
        source = (ROOT / "src/P5RArchipelago/ApClient.cs").read_text()
        self.assertIn("_iwelethChestsEnabled = clientVersion < 12", source)
        self.assertIn("DepthsAccessTier = clientVersion >= 12 ? 7 : 4", source)
        self.assertIn("if (path != 8 || _iwelethChestsEnabled)", source)

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_warps_match_native_one_based_path_numbers(self):
        flow = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        checkpoint = flow.split("void mementos_checkpoint_menu()", 1)[1].split("// Procedure Index: 26", 1)[0]
        self.assertIn("path = destination + 1", checkpoint)
        self.assertIn("destination > 0 &&", checkpoint)
        self.assertNotIn("CALL_FIELD(161", checkpoint)
        message = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.msg").read_text()
        palaces = message.split("[sel PALACE_SEL top]", 1)[1].split("[msg", 1)[0]
        self.assertLess(palaces.index("Shido"), palaces.index("Depths"))
        self.assertLess(palaces.index("Depths"), palaces.index("Maruki"))

if __name__ == "__main__": unittest.main()
