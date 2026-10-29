using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class LaunchOptionsTests
{
    [TestMethod]
    public void DefaultStartupShowsEditorAndUsesProductSettings()
    {
        var options = LaunchOptions.Parse(["PPPaint.exe"]);
        Assert.IsFalse(options.IsolatedSettings);
        Assert.AreEqual("pppaint", options.ToolId);
        Assert.AreEqual("standalone", options.LaunchOrigin);
        Assert.AreEqual("PPPaint", Path.GetFileName(options.SettingsDirectory));
    }

    [TestMethod]
    public void SettingsOptionIsNotTreatedAsAnImage()
    {
        var settings = Path.GetFullPath("settings.pppaint");
        var image = Path.GetFullPath("image.png");
        var options = LaunchOptions.Parse(["PPPaint.exe", "--settings-dir", settings, "--launch-origin=kotorintoys", image]);
        Assert.AreEqual(settings, options.SettingsDirectory);
        Assert.AreEqual(image, options.FilePath);
        Assert.AreEqual("host_manual", options.LaunchOrigin);
        Assert.AreEqual("pppaint-" + LaunchOptions.SettingsKey(settings), options.ToolId);
    }

    [TestMethod]
    public void ExplicitSettingsOverridesEnvironment()
    {
        var settings = Path.GetFullPath("settings");
        Assert.AreEqual(settings, LaunchOptions.Parse(["PPPaint.exe", "--settings-dir=" + settings], Path.GetFullPath("environment")).SettingsDirectory);
        Assert.IsTrue(LaunchOptions.Parse(["PPPaint.exe"], settings).IsolatedSettings);
    }

    [TestMethod]
    public void SettingsKeyMatchesWindowsNormalizedPath()
    {
        var settings = Path.GetFullPath("settings");
        Assert.AreEqual(LaunchOptions.SettingsKey(settings), LaunchOptions.SettingsKey(settings.ToUpperInvariant().Replace('\\', '/')));
    }

    [TestMethod]
    public void SilentStartupAndInvalidArgumentsAreRejected()
    {
        foreach (var argument in new[] { "--startup", "--hidden", "--hidden=true", "--settings-dir", "--settings-dir=relative", "--launch-origin" })
            Assert.ThrowsExactly<ArgumentException>(() => LaunchOptions.Parse(["PPPaint.exe", argument]));
    }
}
