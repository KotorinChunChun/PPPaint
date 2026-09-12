namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class SavePickerContractTests
{
    [TestMethod]
    public void SavePicker_PrioritizesRequestedTypeBeforeAddingChoices()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "ChunPaint", "MainWindow.xaml.cs"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "ChunPaint", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private async Task<bool> SaveDocumentAsync", StringComparison.Ordinal);
        var end = source.IndexOf("file = await picker.PickSaveFileAsync();", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start);
        var setup = source[start..end];
        StringAssert.Contains(setup, "requestedExtension ?? \"chunpaint\"");
        StringAssert.Contains(setup, "choices.OrderByDescending(choice => choice.Extensions.Contains(preferredExtension, StringComparer.OrdinalIgnoreCase))");
        StringAssert.Contains(setup, "picker.FileTypeChoices.Add(choice.Label, choice.Extensions)");
        StringAssert.Contains(setup, "picker.DefaultFileExtension = preferredExtension");
        Assert.IsFalse(setup.Contains("picker.FileTypeChoices.Add(\"ChunPaint", StringComparison.Ordinal), "プロジェクト形式を要求に関係なく先頭追加するとWebP等の選択を上書きします。");
    }
}
