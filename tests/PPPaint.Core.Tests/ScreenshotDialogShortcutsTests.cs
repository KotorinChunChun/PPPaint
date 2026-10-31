namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotDialogShortcutsTests
{
    [TestMethod]
    [DataRow('N', false, ScreenshotDialogAction.Secondary)]
    [DataRow('n', false, ScreenshotDialogAction.Secondary)]
    [DataRow('N', true, ScreenshotDialogAction.Secondary)]
    [DataRow('n', true, ScreenshotDialogAction.Secondary)]
    [DataRow('S', false, ScreenshotDialogAction.Primary)]
    [DataRow('s', true, ScreenshotDialogAction.Primary)]
    [DataRow('C', false, ScreenshotDialogAction.Close)]
    [DataRow('c', true, ScreenshotDialogAction.Close)]
    public void UnsavedConfirmationSupportsDisplayedLettersWithOrWithoutAlt(char key, bool alt, ScreenshotDialogAction expected)
    {
        var shortcuts = new ScreenshotDialogShortcuts(AllowPlainKeys: true, SecondaryKey: 'N');
        Assert.AreEqual(expected, shortcuts.Resolve(key, false, alt, false, false, false));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void PlainLettersDoNotExecuteInEditingDialogsOrTextFields(bool allowPlainKeys, bool editingText)
    {
        var shortcuts = new ScreenshotDialogShortcuts(AllowPlainKeys: allowPlainKeys, SecondaryKey: 'N');
        foreach (var key in new[] { 'S', 'N', 'C' })
            Assert.AreEqual(ScreenshotDialogAction.None, shortcuts.Resolve(key, false, false, false, editingText, false));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ExplicitAltAccessKeysRemainAvailableRegardlessOfTextFocus(bool allowPlainKeys, bool editingText)
    {
        var shortcuts = new ScreenshotDialogShortcuts(AllowPlainKeys: allowPlainKeys, SecondaryKey: 'N');
        Assert.AreEqual(ScreenshotDialogAction.Primary, shortcuts.Resolve('S', false, true, false, editingText, false));
        Assert.AreEqual(ScreenshotDialogAction.Secondary, shortcuts.Resolve('N', false, true, false, editingText, false));
        Assert.AreEqual(ScreenshotDialogAction.Close, shortcuts.Resolve('C', false, true, false, editingText, false));
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    public void ControlShiftAndImeNeverExecuteDialogActions(bool control, bool shift, bool composing)
    {
        var shortcuts = new ScreenshotDialogShortcuts(AllowPlainKeys: true, SecondaryKey: 'N');
        foreach (var alt in new[] { false, true }) foreach (var editingText in new[] { false, true }) foreach (var key in new[] { 'S', 'N', 'C' })
            Assert.AreEqual(ScreenshotDialogAction.None, shortcuts.Resolve(key, control, alt, shift, editingText, composing));
    }

    [TestMethod]
    [DataRow('\r')]
    [DataRow('\n')]
    [DataRow('\u001b')]
    [DataRow('\t')]
    [DataRow(' ')]
    [DataRow('?')]
    [DataRow('0')]
    [DataRow('Q')]
    public void UnassignedAndStandardDialogNavigationKeysRemainUnhandled(char key)
    {
        var shortcuts = new ScreenshotDialogShortcuts(AllowPlainKeys: true, SecondaryKey: 'N');
        foreach (var alt in new[] { false, true })
            Assert.AreEqual(ScreenshotDialogAction.None, shortcuts.Resolve(key, false, alt, false, false, false));
    }

    [TestMethod]
    public void CustomLettersAreCaseInsensitiveAndSecondaryDoesNotLeakBetweenDialogKinds()
    {
        var standard = new ScreenshotDialogShortcuts(AllowPlainKeys: true);
        var unsaved = standard with { SecondaryKey = 'n' };
        Assert.AreEqual(ScreenshotDialogAction.Secondary, standard.Resolve('d', false, false, false, false, false));
        Assert.AreEqual(ScreenshotDialogAction.None, standard.Resolve('n', false, false, false, false, false));
        Assert.AreEqual(ScreenshotDialogAction.None, unsaved.Resolve('D', false, true, false, false, false));
        Assert.AreEqual(ScreenshotDialogAction.Secondary, unsaved.Resolve('N', false, true, false, false, false));
        var custom = new ScreenshotDialogShortcuts(true, 'a', 'b', 'x');
        Assert.AreEqual(ScreenshotDialogAction.Primary, custom.Resolve('A', false, false, false, false, false));
        Assert.AreEqual(ScreenshotDialogAction.Secondary, custom.Resolve('B', false, true, false, true, false));
        Assert.AreEqual(ScreenshotDialogAction.Close, custom.Resolve('X', false, false, false, false, false));
        Assert.AreEqual(ScreenshotDialogAction.None, custom.Resolve('C', false, false, false, false, false));
    }
}
