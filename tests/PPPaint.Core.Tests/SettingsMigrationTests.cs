namespace PPPaint.Core.Tests;

[TestClass]
public sealed class SettingsMigrationTests
{
    [TestMethod]
    public void LegacyCopyPreservesOriginalAndRunsOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "PPPaint-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = Path.Combine(root, "legacy"); var current = Path.Combine(root, "current");
            Directory.CreateDirectory(Path.Combine(legacy, "Recovery", "old"));
            File.WriteAllText(Path.Combine(legacy, "preferences.json"), "old preferences");
            File.WriteAllText(Path.Combine(legacy, "ai-settings.json"), "excluded");
            File.WriteAllText(Path.Combine(legacy, "Recovery", "old", "image.chunpaint"), "recovery");
            SettingsMigration.TryCopyLegacy(current, legacy);
            Assert.AreEqual("old preferences", File.ReadAllText(Path.Combine(current, "preferences.json")));
            Assert.AreEqual("recovery", File.ReadAllText(Path.Combine(current, "Recovery", "old", "image.chunpaint")));
            Assert.IsFalse(File.Exists(Path.Combine(current, "ai-settings.json")));
            File.WriteAllText(Path.Combine(current, "preferences.json"), "new preferences");
            SettingsMigration.TryCopyLegacy(current, legacy);
            Assert.AreEqual("new preferences", File.ReadAllText(Path.Combine(current, "preferences.json")));
            Assert.AreEqual("old preferences", File.ReadAllText(Path.Combine(legacy, "preferences.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void IncompleteCopyRetriesWithoutReplacingNewPreferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "PPPaint-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = Path.Combine(root, "legacy"); var current = Path.Combine(root, "current");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "preferences.json"), "old");
            using (var locked = new FileStream(Path.Combine(legacy, "preferences.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                SettingsMigration.TryCopyLegacy(current, legacy);
            Assert.IsTrue(File.Exists(current + ".legacy-migration-pending"));
            Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(current, "preferences.json"), "new");
            SettingsMigration.TryCopyLegacy(current, legacy);
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(current, "preferences.json")));
            Assert.IsFalse(File.Exists(current + ".legacy-migration-pending"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
