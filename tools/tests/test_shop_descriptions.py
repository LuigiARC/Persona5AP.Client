from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class ShopDescriptionTests(unittest.TestCase):
    def test_renderer_is_prepared_but_not_activated_on_load(self):
        source = (ROOT / "src/P5RArchipelago/Native/ChestMonitor.cs").read_text()
        setup = source.split("private void InitializeShopDescriptions", 1)[1].split("public void SetShopDescriptionsEnabled", 1)[0]
        self.assertIn("CreateHook<DrawItemHelp>", setup)
        self.assertNotIn(".Activate()", setup)
        self.assertIn("if (_drawItemHelpHook.IsHookActivated) _drawItemHelpHook.Enable()", source)
        callback = source.split("private long DrawItemHelpImpl", 1)[1].split("private static IEnumerable", 1)[0]
        self.assertIn("_confidantShopActive()", callback)
        self.assertIn("category is 0 or 10", callback)
        self.assertIn("TryGetConfidantShopCmmId", callback)
        self.assertIn("OriginalFunction", callback)

    def test_hook_is_scoped_to_shop_and_disabled_before_buffer_cleanup(self):
        source = (ROOT / "src/P5RArchipelago/ArchipelagoMod.cs").read_text()
        opening = source.split("if (featureId == BeginConfidantShopFeatureId)", 1)[1].split("if (featureId == EndConfidantShopFeatureId)", 1)[0]
        closing = source.split("if (featureId == EndConfidantShopFeatureId)", 1)[1].split("if (featureId == ConsumeConfidantShopReturnFeatureId)", 1)[0]
        self.assertIn("SetShopDescriptionsEnabled(true)", opening)
        self.assertLess(closing.index("SetShopDescriptionsEnabled(false)"), closing.index("ClearConfidantShopNames()"))
        loaded = source.split("private void OnSaveLoaded", 1)[1].split("_loadedSaveSlot", 1)[0]
        self.assertIn("SetShopDescriptionsEnabled(false)", loaded)

    def test_description_uses_existing_scout_flags_and_recipient(self):
        source = (ROOT / "src/P5RArchipelago/ApClient.cs").read_text()
        self.assertIn('"very important"', source)
        self.assertIn('"useful"', source)
        self.assertIn("pair.Value.Player.Alias", source)
        self.assertIn("_shopDescriptions[pair.Key]", source)
        source = (ROOT / "src/P5RArchipelago/ArchipelagoMod.cs").read_text()
        self.assertIn("details.LocationIds.Select(_apClient.ShopDescription)", source)

if __name__ == "__main__": unittest.main()
