using System.Text.RegularExpressions;

namespace ChunPaint.Core.Tests;

/// <summary>
/// C13 のレイヤー所属を、WinUI の実行環境を必要としないソース契約として固定する。
/// </summary>
[TestClass]
public sealed class C13LayerOwnershipContractTests
{
    [TestMethod]
    public void MergeCommitsUpperObjectsDeletesThemAndComposesOnlyWhenUpperIsVisible()
    {
        var source = ReadSource("src/ChunPaint/MainWindow.Layers.cs");
        var method = ExtractMethod(source, "MergeActiveLayerDown");
        var merge = ExtractMethod(source, "MergeLayerIndices");
        var helpers = ReadSource("src/ChunPaint/MainWindow.LayerProperties.cs");
        AssertHasAll(method, "MergeLayerIndices");
        AssertHasAll(merge, "IsLocked", "DrawLayerComposite", "destination.Surface.SetPixelBytes", "_objects.RemoveAll", "selectedIds.Contains(item.LayerId)", "destination.Opacity = 1f");
        Assert.IsTrue(merge.IndexOf("DrawLayerComposite", StringComparison.Ordinal) < merge.IndexOf("_objects.RemoveAll", StringComparison.Ordinal),
            "所属オブジェクトを含めて合成してから削除してください。");
        AssertHasAll(ExtractMethod(helpers, "DrawLayerComposite"), "layer.IsVisible", "layer.Opacity", "CreateLayerContent");
    }

    [TestMethod]
    public void TransformPathCommitsEditableObjectsBeforePixelTransforms()
    {
        var source = ReadSource("src/ChunPaint/MainWindow.ImageOperations.cs");
        foreach (var methodName in new[] { "ApplyImageTransform", "ApplyDocumentTransform", "ApplyActiveLayerTransform" })
        {
            var method = ExtractMethod(source, methodName);
            var commit = IndexOfAny(method, "ApplyAllEditableObjects", "CommitAllEditableObjectsToLayers", "RasterizeObjectsIntoLayer");
            var pixelOperation = IndexOfAny(method, "ImageTransforms.", "transform(");
            Assert.IsTrue(commit >= 0, $"{methodName} は画素変換前に未確定オブジェクトを確定してください。");
            Assert.IsTrue(pixelOperation < 0 || commit < pixelOperation,
                $"{methodName} の画素変換より前にオブジェクトを確定してください。");
            if (methodName == "ApplyActiveLayerTransform")
            {
                AssertHasAll(method, "var layer = _layers[_activeLayerIndex]", "RasterizeObjectsIntoLayer(layer)");
                Assert.IsFalse(method.Contains("CommitAllEditableObjectsToLayers", StringComparison.Ordinal),
                    "アクティブレイヤーの変換で他レイヤーの編集オブジェクトを変更しないでください。");
            }
        }
    }

    [TestMethod]
    public void VisibleLayerDrawAndHitTestFilterByLayerOwnership()
    {
        var main = ReadSource("src/ChunPaint/MainWindow.xaml.cs");
        var objects = ReadSource("src/ChunPaint/MainWindow.Objects.cs");

        var draw = ExtractMethod(main, "PaintCanvas_Draw");
        var composite = ExtractMethod(main, "CreateComposite");
        var drawObjects = ExtractMethod(objects, "DrawEditableObjects");
        var hitTest = ExtractMethod(objects, "FindEditableObject");

        var helpers = ReadSource("src/ChunPaint/MainWindow.LayerProperties.cs");
        AssertHasAll(draw, "DrawLayerComposite");
        AssertHasAll(composite, "DrawLayerComposite");
        AssertHasAll(ExtractMethod(helpers, "DrawLayerComposite"), "layer.IsVisible", "layer.Opacity", "CreateLayerContent");
        AssertHasAll(ExtractMethod(helpers, "CreateLayerContent"), "DrawImage(layer.Surface)", "DrawEditableObjects");
        AssertHasAll(drawObjects, "LayerId", "layer.Id", "DrawEditableObject");
        AssertHasAll(hitTest, "layer.IsVisible", "layer.IsLocked", "LayerId", "layer.Id");
    }

    [TestMethod]
    public void LayerThumbnailIncludesOwnedEditableObjects()
    {
        var source = ReadSource("src/ChunPaint/MainWindow.Layers.cs");
        var method = ExtractMethod(source, "CreateLayerThumbnail");
        var pixels = ExtractMethod(source, "GetLayerThumbnailPixels");
        var helpers = ReadSource("src/ChunPaint/MainWindow.LayerProperties.cs");
        var objects = ReadSource("src/ChunPaint/MainWindow.Objects.cs");
        AssertHasAll(method, "GetLayerThumbnailPixels");
        AssertHasAll(pixels, "DrawLayerComposite");
        AssertHasAll(ExtractMethod(helpers, "DrawLayerComposite"), "CreateLayerContent", "layer.Opacity", "layer.IsVisible");
        AssertHasAll(ExtractMethod(helpers, "CreateLayerContent"), "DrawEditableObjects");
        var combined = ExtractMethod(objects, "DrawEditableObjects");

        AssertHasAll(combined, "_objects", "LayerId", "layer.Id");
        Assert.IsTrue(
            combined.Contains("DrawEditableObject", StringComparison.Ordinal) ||
            combined.Contains("DrawEditableObjects", StringComparison.Ordinal),
            "レイヤーサムネイルは所属オブジェクトを描画してください。");
    }

    [TestMethod]
    public void DuplicateCreatesNewLayerAndObjectIdsAndDeleteRemovesOwnedObjects()
    {
        var source = ReadSource("src/ChunPaint/MainWindow.Layers.cs");
        var duplicate = ExtractMethod(source, "DuplicateActiveLayer");
        var delete = ExtractMethod(source, "DeleteActiveLayer");

        AssertHasAll(duplicate, "new LayerState", "_objects", "LayerId");
        Assert.IsTrue(
            duplicate.Contains("Guid.NewGuid", StringComparison.Ordinal) ||
            duplicate.Contains("newId", StringComparison.Ordinal) ||
            duplicate.Contains("Id =", StringComparison.Ordinal),
            "複製時にはレイヤー／所属オブジェクトへ新しいIDを割り当ててください。");
        AssertHasAll(delete, "_objects", "RemoveAll");
        Assert.IsTrue(
            delete.Contains("deletedLayerId", StringComparison.Ordinal) ||
            delete.Contains("layer.Id", StringComparison.Ordinal) ||
            delete.Contains("_layers[_activeLayerIndex].Id", StringComparison.Ordinal),
            "削除対象レイヤーのIDに属する編集オブジェクトだけを削除してください。");
    }

    [TestMethod]
    public void ImageAiProviderContractRemainsWhileUiMayHideItsEntryPoint()
    {
        var core = ReadSource("src/ChunPaint.Core/ImageAiProvider.cs");
        AssertHasAll(core, "interface IImageAiProvider", "ImageAiRequest", "ExecuteAsync");
    }

    private static void AssertHasAll(string source, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            Assert.IsTrue(source.Contains(fragment, StringComparison.Ordinal),
                $"ソース契約が見つかりません: {fragment}");
        }
    }

    private static int IndexOfAny(string source, params string[] fragments)
    {
        var positions = fragments
            .Select(fragment => source.IndexOf(fragment, StringComparison.Ordinal))
            .Where(position => position >= 0);
        return positions.DefaultIfEmpty(-1).Min();
    }

    private static int IndexOfAnyAfter(string source, int start, params string[] fragments)
    {
        if (start < 0)
        {
            return -1;
        }

        var positions = fragments
            .Select(fragment => source.IndexOf(fragment, start, StringComparison.Ordinal))
            .Where(position => position >= 0);
        return positions.DefaultIfEmpty(-1).Min();
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var match = Regex.Match(
            source,
            $@"\b(?:private|internal|public)\s+(?:static\s+)?(?:async\s+)?[^\r\n{{;]+\b{Regex.Escape(methodName)}\s*\([^)]*\)\s*\{{",
            RegexOptions.CultureInvariant);
        Assert.IsTrue(match.Success, $"メソッドが見つかりません: {methodName}");

        var openBrace = source.IndexOf('{', match.Index + match.Length - 1);
        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
            {
                return source[match.Index..(index + 1)];
            }
        }

        Assert.Fail($"メソッドの終端が見つかりません: {methodName}");
        return string.Empty;
    }

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "src", "ChunPaint")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "ChunPaint のプロジェクトルートを特定できません。");
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
