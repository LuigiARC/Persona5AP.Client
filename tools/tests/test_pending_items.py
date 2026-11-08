import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("pending_items", ROOT / "tools/build_pending_items.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PendingMenuTests(unittest.TestCase):
    def test_existing_procedures_preserved_and_menu_appended(self):
        root = "void ap_hotkey_menu()\n{\n    CMM_CHECK_ENABLE_FUNC(900135);\n}\n\n"
        suffix = "// Procedures below\nvoid existing_after() { KEEP(); }"
        messages = "[sel AP_HOTKEY_SEL top]\n[s]Warps[e]\n[s]Close[e]"
        flow, result_messages = module.transform(root + suffix, messages)
        self.assertTrue(flow.endswith(suffix + module.FLOW))
        self.assertIn("if (action == 3) { ap_pending_items_menu(); goto _ap_root; }", flow)
        self.assertIn("[s]Pending Items[e]\n[s]Close[e]", result_messages)
        self.assertEqual(module.transform(flow, result_messages), (flow, result_messages))

    def test_claims_run_after_selector_closes_and_event_ends(self):
        flow = module.FLOW
        for operation in ("900220 + action", "900214"):
            self.assertLess(flow.index("MSG_WND_CLS"), flow.index(operation))
            self.assertLess(flow.index("FLD_SMP_EVENT_END"), flow.index(operation))
        self.assertIn("action < 0 || action == 9", flow)

    def test_unexpected_root_does_not_mutate_input(self):
        with self.assertRaises(ValueError):
            module.transform("void ap_hotkey_menu() {} // Procedures below", "unrecognized")
