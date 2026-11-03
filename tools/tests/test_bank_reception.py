"""Offline tests of the reception patch recipe; not live-game verification."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "bank_patch", ROOT / "tools/build_bank_reception_overrides.py"
)
patch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(patch)


class ReceptionPatchTests(unittest.TestCase):
    def test_guards_are_scoped_and_keep_original_body(self):
        for name in patch.GUARD_PROCEDURES:
            original = f"void {name}()\n{{\n    FLD_SMP_EVENT_BEGIN(0);\n}}"
            result = patch.insert_guard(original, name, "")
            self.assertLess(result.index("CMM_CHECK_ENABLE_FUNC(900171)"),
                            result.index("FLD_SMP_EVENT_BEGIN(0)"))
            self.assertIn("        return;", result)

    def test_doors_open_only_on_ap_guard(self):
        for name in patch.DOOR_PROCEDURES:
            original = f"void {name}()\n{{\n    NOT_OPEN();\n}}"
            result = patch.insert_guard(original, name, "        FLD_OPEN_DOOR();\n")
            self.assertIn("FLD_OPEN_DOOR();\n        return;", result)
            self.assertIn("NOT_OPEN();", result)

    def test_missing_or_duplicate_procedure_rejected(self):
        with self.assertRaises(ValueError):
            patch.insert_guard("", "missing", "")
        with self.assertRaises(ValueError):
            patch.insert_guard("void a() {}\nvoid a() {}", "a", "")


if __name__ == "__main__":
    unittest.main()
