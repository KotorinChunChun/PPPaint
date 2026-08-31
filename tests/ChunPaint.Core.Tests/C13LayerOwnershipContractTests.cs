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
        var rasterize = ExtractMethod(source, "RasterizeObjectsIntoLayer");

        var draw = IndexOfAny(rasterize, "DrawEditableObject", "DrawEditableObjects");
        var remove = IndexOfAny(rasterize,
            "RemoveObjectsForLayer", "_objects.RemoveAll", "_objects.Clear");
        var visible = method.IndexOf("upper.IsVisible", StringComparison.Ordinal);
        var compose = IndexOfAnyAfter(method, visible, "DrawImage(upper.Surface", "DrawImage (upper.Surface");

        Assert.IsTrue(draw >= 0, "結合前に上レイヤーの編集オブジェクトをSurfaceへ確定してください。");
        Assert.IsTrue(remove > draw, "確定した上レイヤーのオブジェクトを削除してからSurfaceを結合してください。");
        Assert.IsTrue(method.IndexOf("RasterizeObjectsIntoLayer", StringComparison.Ordinal) >= 0,
            "結合処理は上レイヤーの所属オブジェクトを先にラスタライズしてください。");
        Assert.IsTrue(visible >= 0, "上レイヤーの可視性を判定してください。");
        Assert.IsTrue(compose > visible, "非表示上レイヤーのSurfaceを下へ合成しないでください。");
    }

    [TestMethod]
    public void TransformPathCommitsEditableObjectsBeforePixelTransforms()
    {
        var source = ReadSource("src/ChunPaint/MainWindow.ImageOperations.cs");
        foreach (var methodName in new[] { "ApplyImageTransform", "ApplyDocumentTransform", "ApplyActiveLayerTransform" })
        {
            var method = ExtractMethod(source, methodName);
            var commit = IndexOfAny(method, "ApplyAllEditableObjects", "CommitAllEditableObjectsToLayers");
            var pixelOperation = IndexOfAny(method, "ImageTransforms.", "transform(");
            Assert.IsTrue(commit >= 0, $"{methodName} は画素変換前に未確定オブジェクトを確定してください。");
            Assert.IsTrue(pixelOperation < 0 || commit < pixelOperation,
                $"{methodName} の画素変換より前にオブジェクトを確定してください。");
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

        AssertHasAll(draw, "layer.IsVisible", "DrawEditableObjects");
        AssertHasAll(composite, "layer.IsVisible", "DrawEditableObjects");
        AssertHasAll(drawObjects, "LayerId", "layer.Id", "DrawEditableObject");
        AssertHasAll(hitTest, "layer.IsVisible", "LayerId", "layer.Id");
    }

    [TestMethod]
    public void LayerThumbnailIncludesOwnedEditableObjects()
    {
        var source = ReadSource("src/ChunPaint/MainWindow.Layers.cs");
        var method = ExtractMethod(source, "CreateLayerThumbnail");
        var pixels = ExtractMethod(source, "GetLayerThumbnailPixels");
        var combined = method + "\n" + pixels;

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
