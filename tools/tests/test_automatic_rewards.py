import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("automatic_rewards", ROOT / "tools/build_automatic_rewards.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class AutomaticRewardFlowTests(unittest.TestCase):
    def test_only_reserved_body_changes_and_transform_is_idempotent(self):
        old = "void grant_connected_confidant_abilities()\n{\n    CMM_CHECK_ENABLE_FUNC(900138);\n}"
        original = "void existing_before() { KEEP(); }\n" + old + "\nvoid existing_after() { KEEP(); }"
        result = module.transform(original)
        self.assertEqual(result, original.replace(old, module.FLOW))
        self.assertEqual(module.transform(result), result)

    def test_safe_begin_precedes_mutations_and_completion_follows_them(self):
        flow = module.FLOW
        self.assertLess(flow.index("900200"), flow.index("reset_ap_location_flags"))
        for operation in ("900130", "apply_ap_party_recruits", "apply_ap_party_levels"):
            self.assertLess(flow.index(operation), flow.index("900201"))
        for forbidden in ("MSG_WND", "SEL(", "FADE", "CALL_FIELD", "CALL_AT_DUNGEON", "WAIT("):
            self.assertNotIn(forbidden, flow)

    def test_unknown_or_duplicate_source_is_rejected(self):
        with self.assertRaises(ValueError):
            module.transform("void unrelated() {}")
        old = "void grant_connected_confidant_abilities() { CMM_CHECK_ENABLE_FUNC(900138); }"
        with self.assertRaises(ValueError):
            module.transform(old + old)
