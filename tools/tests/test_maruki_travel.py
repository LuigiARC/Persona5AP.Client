"""Offline checks of the native Laboratory mask and verified safe-interior mapping."""
from pathlib import Path
import os
import unittest

ROOT = Path(__file__).resolve().parents[2]
PRIVATE = Path(os.environ.get("P5R_PRIVATE_ROOT", ROOT.parent / "private-backups/persona-5-ap-local"))


class LaboratoryTravelTests(unittest.TestCase):
    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_mask_unlocks_only_verified_destinations(self):
        unlocked = [i for i in range(17) if not (129026 & (1 << i))]
        self.assertEqual(unlocked, [0, *range(2, 11)])
        source = (PRIVATE / "src/P5RArchipelago/Common/DUNGEON.flow").read_text()
        body = source.split("void D_FastTravel_D10()", 1)[1].split("// Procedure Index: 36", 1)[0]
        self.assertIn("var81 = 129026;", body)
        for flag in range(4436, 4446):
            self.assertIn(f"BIT_ON((0x20000000 + {flag}));", body)
        self.assertIn("else if (var82 == 3)", body)
        self.assertIn("var84 = 10;", body)
        self.assertIn("else if (var82 == 6)", body)
        self.assertIn("var84 = 9;", body)

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_puzzle_overrides_are_ap_scoped(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("puzzle", ROOT / "tools/build_maruki_puzzle_overrides.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        procedures = module.ROAD_PROCEDURES + module.OBSTACLE_PROCEDURES
        original = "\n".join(f"void {name}() {{ VANILLA(); }}" for name in procedures)
        patched = module.patch_script(original)
        self.assertEqual(patched.count("CMM_CHECK_ENABLE_FUNC(900172)"), 7)
        self.assertEqual(patched.count("VANILLA();"), 7)
        self.assertEqual(patched.count("FLD_OBJ_MODEL_COLLIS(sVar29, 1);"), 4)
        self.assertEqual(patched.count("FLD_OBJ_MODEL_COLLIS(sVar29, 0);"), 3)
        source = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        lab = source.split("else if (major == 162)", 1)[1].split("// Procedure Index: 19", 1)[0]
        for flag in (4149, 4153, 4165, 4248, 4205, 4513):
            self.assertIn(f"BIT_ON((0x20000000 + {flag}));", lab)
        self.assertNotIn("BIT_ON((0x20000000 + 4105));", lab)

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_entry_does_not_prompt_for_checkpoint(self):
        source = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        self.assertNotIn("LAB_CHECKPOINT_SEL", source)
        self.assertNotIn("position = 10 + checkpoint;", source)

    def test_game_scripts_are_loaded_locally_and_never_embedded(self):
        import xml.etree.ElementTree as ET
        project = ET.parse(ROOT / "src/P5RArchipelago/P5RArchipelago.csproj")
        self.assertFalse(any(entry.attrib.get("Include", "").upper().endswith(".BF")
                             for entry in project.iter("EmbeddedResource")))
        runner = (ROOT / "src/P5RArchipelago/Native/HubFlowRunner.cs").read_text()
        self.assertIn('File.ReadAllBytes(path)', runner)
        cache = (ROOT / "src/P5RArchipelago/GameAssets/GameAssetCache.cs").read_text()
        self.assertIn('"en.cpk/script/field/FSCR8000_002_000.BF"', cache)
        self.assertIn('new HubFlowRunner(hooks, scanner, hubPath)',
                      (ROOT / "src/P5RArchipelago/ArchipelagoMod.cs").read_text())

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_question_events_and_switches_do_not_run_in_ap(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("questions", ROOT / "tools/build_maruki_question_overrides.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        original = "\n".join(f"void {name}() {{ VANILLA(); }}"
                             for name in (*module.QUIZ_PROCEDURES, *module.SWITCH_PROCEDURES, "StartUpConfirmation"))
        original += "\nvoid SimpleSeriStart() { if ((BIT_CHK((0x20000000 + 68)) == 1) && (sVar316 == 1)) { BLOCK(); } else { SeriGimickStart(); } }"
        patched = module.patch_script(original)
        self.assertIn("sVar335 = -1;", patched)
        self.assertIn("CMM_CHECK_ENABLE_FUNC(900172) != 1", patched)
        self.assertIn("SeriGimickStart();", patched)
        self.assertEqual(patched.count("VANILLA();"), len(module.QUIZ_PROCEDURES) + 11)
        for procedure in module.SWITCH_PROCEDURES:
            body = patched.split(f"void {procedure}()", 1)[1].split("void ", 1)[0]
            self.assertLess(body.index("return;"), body.index("VANILLA();"))
            self.assertIn("CMM_CHECK_ENABLE_FUNC(900172) == 1", body)
        initializer = module.patch_initializer("void MAPOBJ_SET() { ORIGINAL(); }")
        self.assertIn("FLD_SET_LOCAL_COUNT((49152 + 17), 2);", initializer)
        self.assertIn("InitAllSeriAnim();", initializer)
        self.assertIn("ORIGINAL();", initializer)
        self.assertLess(initializer.index("return;"), initializer.index("ORIGINAL();"))
        with self.assertRaises(ValueError):
            module.patch_script(patched)
        source = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        lab = source.split("else if (major == 162)", 1)[1].split("// Procedure Index: 19", 1)[0]
        for flag in (4166, 4167, 4168, 4169, 4192, 4193, 4194, 4195, 4251):
            self.assertIn(f"BIT_ON((0x20000000 + {flag}));", lab)
        self.assertIn("BIT_OFF((0x20000000 + 68));", lab)


if __name__ == "__main__":
    unittest.main()
