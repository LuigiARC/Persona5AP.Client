from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class MessageWindowGuardTests(unittest.TestCase):
    def test_only_null_ap_windows_are_deferred(self):
        source = (ROOT / "src/P5RArchipelago/Native/MessageWindowGuard.cs").read_text()
        self.assertIn("window == IntPtr.Zero && _apScope()", source)
        self.assertIn("return 1;", source)
        self.assertIn("return _hook.OriginalFunction(window)", source)
        self.assertNotIn("Marshal.Write", source)

    def test_guard_does_not_change_apworld_or_message_scripts(self):
        source = (ROOT / "src/P5RArchipelago/ArchipelagoMod.cs").read_text()
        self.assertIn("_messageWindowGuard = new MessageWindowGuard", source)
        self.assertIn("Volatile.Read(ref _hubMenuOpen) != 0 || GetActiveApPalace() is not null", source)
        self.assertIn("_messageWindowGuard?.Dispose()", source)

if __name__ == "__main__": unittest.main()
