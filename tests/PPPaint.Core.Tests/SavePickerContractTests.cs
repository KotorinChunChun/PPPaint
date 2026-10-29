using System.Text.RegularExpressions;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class SavePickerContractTests
{
    [TestMethod]
    public void SavePicker_PrioritizesRequestedTypeBeforeAddingChoices()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "PPPaint", "MainWindow.xaml.cs"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "PPPaint", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private async Task<bool> SaveDocumentAsync", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "保存処理が見つかりません。");
        var end = source.IndexOf("file = await picker.PickSaveFileManagedAsync();", start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, "形式を設定した保存ピッカーは管理ラッパーで開いてください。");
        var setup = source[start..end];
        StringAssert.Contains(setup, "requestedExtension ?? \"pppaint\"");
        StringAssert.Contains(setup, "choices.OrderByDescending(choice => choice.Extensions.Contains(preferredExtension, StringComparer.OrdinalIgnoreCase))");
        StringAssert.Contains(setup, "picker.FileTypeChoices.Add(choice.Label, choice.Extensions)");
        StringAssert.Contains(setup, "picker.DefaultFileExtension = preferredExtension");
        Assert.IsFalse(setup.Contains("picker.FileTypeChoices.Add(\"PPPaint", StringComparison.Ordinal), "プロジェクト形式を要求に関係なく先頭追加するとWebP等の選択を上書きします。");

        var managedPickers = File.ReadAllText(Path.Combine(directory.FullName, "src", "PPPaint", "ManagedPickers.cs"));
        Assert.IsTrue(Regex.IsMatch(managedPickers,
            @"\bPickSaveFileManagedAsync\s*\(this\s+FileSavePicker\s+picker\)\s*\{\s*OpenCount\+\+;\s*try\s*\{\s*return\s+await\s+picker\.PickSaveFileAsync\(\);\s*\}\s*finally\s*\{\s*OpenCount--;\s*\}\s*\}",
            RegexOptions.CultureInvariant),
            "管理ラッパーはネイティブ保存ピッカーの待機中だけOpenCountを加算し、取消・例外を含めfinallyで必ず戻してください。");
    }
}
