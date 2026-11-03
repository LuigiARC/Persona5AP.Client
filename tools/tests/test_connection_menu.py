from pathlib import Path
import os
import unittest

ROOT = Path(__file__).resolve().parents[2]
PRIVATE = Path(os.environ.get("P5R_PRIVATE_ROOT", ROOT.parent / "private-backups/persona-5-ap-local"))

class ConnectionMenuTests(unittest.TestCase):
    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_native_fields_use_confirmed_item_name_type(self):
        flow = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        self.assertIn("SET_MSG_VAR(0, 0x4FE0, 5)", flow)
        self.assertIn("SET_MSG_VAR(0, 0x4FE4, 5)", flow)
        self.assertNotIn("CALL_NAME_ENTRY", flow)
        self.assertNotIn("CALL_PHANTOM_NAME_ENTRY", flow)
        for procedure in ("ap_connection_menu", "ap_connection_edit", "ap_connection_chars"):
            self.assertIn(f"void {procedure}(", flow)
        self.assertIn("void ap_warps_menu()", flow)
        self.assertIn("CMM_CHECK_ENABLE_FUNC(900188)", flow)
        self.assertIn("CMM_CHECK_ENABLE_FUNC(900186)", flow)

    def test_dynamic_callbacks_do_not_bind_to_obsolete_client(self):
        source = (ROOT / "src/P5RArchipelago/ArchipelagoMod.cs").read_text()
        self.assertIn("flag => _apClient.ChestRewardName(flag)", source)
        self.assertIn("() => _apClient.ConsumePendingDeathLinkDamagePercent()", source)
        self.assertIn("() => _apClient.RequeuePendingDeathLink()", source)
        self.assertNotIn("+= _apClient.SendDeathLinkForPartyWipe", source)

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_character_picker_is_deprecated_and_editor_opens_directly(self):
        flow = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        message = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.msg").read_text()
        self.assertNotIn("CONNECTION_CHAR_", flow + message)
        self.assertNotIn("CONNECTION_EDIT_SEL", flow + message)
        self.assertEqual(flow.count("ap_connection_chars("), 1) # Stable unused procedure37.
        title = message.split("[msg MSG_CONNECTION_KEYBOARD]", 1)[1].split("[sel", 1)[0]
        self.assertNotIn("[n]", title) # Generic header is single-line, avoiding overlap.

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_menus_go_directly_to_selectors_and_release_field_event(self):
        flow = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        root = flow.split("void ap_hotkey_menu()", 1)[1].split("// Procedure Index: 31", 1)[0]
        self.assertNotIn("MSG(MSG_AP_HOTKEY", root)
        self.assertLess(root.index("FLD_SMP_EVENT_BEGIN(0)"), root.index("SEL_GENERIC(MSG_AP_HOTKEY, AP_HOTKEY_SEL)"))
        self.assertLess(root.index("MSG_WND_CLS()"), root.index("FLD_SMP_EVENT_END(0)"))
        generated = flow.split("// BEGIN GENERATED CONNECTION MENUS", 1)[1]
        for title in ("MSG_CONNECTION_STATUS", "MSG_CONNECTION_DRAFT", "MSG_AP_WARPS"):
            self.assertNotIn(f"MSG({title}", generated)
        # One extra END belongs to the unsupported-keyboard early-return branch.
        self.assertEqual(generated.count("FLD_SMP_EVENT_BEGIN(0)") + 1, generated.count("FLD_SMP_EVENT_END(0)"))
        message = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.msg").read_text()
        self.assertIn("SEL_GENERIC(MSG_CONNECTION_STATUS, CONNECTION_SEL)", generated)
        self.assertIn("960000 + MSG_CONNECTION_KEYBOARD", generated)
        self.assertIn("CMM_CHECK_ENABLE_FUNC(900199)", generated)
        for row in ("Host: [var 1]", "Slot: [var 2]", "Status: [var 0]", "Edit: [var 0]"):
            self.assertIn(row, message)

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_keyboard_is_scoped_and_does_not_poll_from_worker(self):
        source = (ROOT / "src/P5RArchipelago/Native/ConnectionKeyboard.cs").read_text()
        self.assertIn("process != (uint)Environment.ProcessId", source)
        self.assertIn("Marshal.ReadInt32(context + 0x28) != _header", source)
        self.assertIn("return _input.OriginalFunction(context)", source)
        self.assertIn("_setVariable(window, 0, 0x4FE4, 5)", source)
        self.assertNotIn("Task.Run", source)
        self.assertIn("_actions.OriginalFunction(query, controller, mask)", source)
        self.assertIn("(current & ~FieldLockMask) | _previousLocks", source)
        self.assertIn("Marshal.ReadIntPtr(owner + 0x48) == _lockedField", source)
        flow = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        editor = flow.split("void ap_connection_edit(", 1)[1].split("void ap_connection_chars", 1)[0]
        self.assertLess(editor.index("FLD_SET_PAD_LOCK(1)"), editor.index("SEL_GENERIC("))
        self.assertIn("FLD_SET_MOVE_LOCK(1)", editor)
        self.assertNotIn("FLD_SET_PAD_LOCK(0)", editor) # Original lock state must survive.

    @unittest.skipUnless((PRIVATE / "src/P5RArchipelago/Hub").is_dir(), "Optional private game sources unavailable")
    def test_warp_lists_use_scrolling_selector_and_keep_all_destinations(self):
        flow = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.flow").read_text()
        message = (PRIVATE / "src/P5RArchipelago/Hub/FSCR8000_002_000.msg").read_text()
        for header, selector, rows in (("MSG_PALACE", "PALACE_SEL", 10),
            ("MSG_PALACE_ACTION", "PALACE_ACTION_SEL", 4),
            ("MSG_MEMENTOS_CHECKPOINT", "MEMENTOS_CHECKPOINT_SEL", 10)):
            self.assertIn(f"SEL_GENERIC({header}, {selector})", flow)
            self.assertNotIn(f"SEL({selector})", flow)
            choices = message.split(f"[sel {selector} top]", 1)[1].split("[msg", 1)[0]
            self.assertEqual(choices.count("[s]"), rows)
        self.assertIn("destination < 0 || destination == 9", flow)

if __name__ == "__main__": unittest.main()
