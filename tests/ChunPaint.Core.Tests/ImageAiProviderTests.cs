using System.Numerics;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ImageAiProviderTests
{
    [TestMethod]
    public async Task DisabledProvider_ReturnsSafeUnavailableResultWithoutOutput()
    {
        var provider = new DisabledImageAiProvider();
        var request = new ImageAiRequest(
            ImageAiFeature.GenerativeErase,
            new PixelSurface(4, 3, Rgba32.Transparent),
            SelectionRegion.CreateRectangle(new PixelRect(1, 1, 2, 2)),
            Diagnostics: new Dictionary<string, string> { ["scenario"] = "test" });

        var result = await provider.ExecuteAsync(request);

        Assert.IsFalse(provider.IsAvailable);
        Assert.AreEqual(ImageAiResultStatus.Disabled, result.Status);
        Assert.IsNull(result.OutputImage);
        StringAssert.Contains(result.Message, "外部サービスへの画像送信は行っていません");
        Assert.AreEqual("none", result.Diagnostics!["networkAccess"]);
    }

    [TestMethod]
    public async Task LocalUnavailableProvider_ReturnsDisabledResult()
    {
        var result = await new LocalUnavailableProvider().ExecuteAsync(
            new ImageAiRequest(ImageAiFeature.BackgroundRemoval, new PixelSurface(1, 1, Rgba32.White)));

        Assert.AreEqual(ImageAiResultStatus.Disabled, result.Status);
        Assert.IsNull(result.OutputImage);
    }

    [TestMethod]
    public async Task Providers_ReturnInvalidRequestForUnsupportedFormatAndSelection()
    {
        var provider = new DisabledImageAiProvider();
        var unsupportedFormat = await provider.ExecuteAsync(new ImageAiRequest(
            ImageAiFeature.CopilotEdit,
            new PixelSurface(2, 2, Rgba32.Transparent),
            InputFormat: ImageAiPixelFormat.Unknown));
        var outsideSelection = await provider.ExecuteAsync(new ImageAiRequest(
            ImageAiFeature.CopilotEdit,
            new PixelSurface(2, 2, Rgba32.Transparent),
            SelectionRegion.CreateRectangle(new PixelRect(1, 1, 2, 2))));

        Assert.AreEqual(ImageAiResultStatus.InvalidRequest, unsupportedFormat.Status);
        Assert.AreEqual(ImageAiResultStatus.InvalidRequest, outsideSelection.Status);
    }

    [TestMethod]
    public async Task Providers_ReturnInvalidRequestForOversizedInput()
    {
        var result = await new DisabledImageAiProvider().ExecuteAsync(
            new ImageAiRequest(ImageAiFeature.CopilotEdit, new PixelSurface(8193, 1, Rgba32.Transparent)));

        Assert.AreEqual(ImageAiResultStatus.InvalidRequest, result.Status);
    }

    [TestMethod]
    public async Task Providers_ReturnCancelledWhenRequestTokenIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new DisabledImageAiProvider().ExecuteAsync(
            new ImageAiRequest(ImageAiFeature.CopilotEdit, new PixelSurface(1, 1, Rgba32.Transparent), CancellationToken: cancellation.Token));

        Assert.AreEqual(ImageAiResultStatus.Cancelled, result.Status);
    }

    [TestMethod]
    public async Task ReplacementProvider_ReceivesFeatureImageSelectionTokenAndDiagnostics()
    {
        using var cancellation = new CancellationTokenSource();
        var image = new PixelSurface(3, 2, Rgba32.White);
        var selection = SelectionRegion.CreatePolygon([
            new Vector2(0, 0), new Vector2(3, 0), new Vector2(0, 2)]);
        var request = new ImageAiRequest(
            ImageAiFeature.CopilotEdit,
            image,
            selection,
            cancellation.Token,
            new Dictionary<string, string> { ["requestId"] = "contract-test" });
        var provider = new RecordingProvider();

        var result = await provider.ExecuteAsync(request);

        Assert.AreEqual(ImageAiResultStatus.Succeeded, result.Status);
        Assert.AreSame(image, provider.Received!.InputImage);
        Assert.AreSame(selection, provider.Received.Selection);
        Assert.AreEqual(cancellation.Token, provider.Received.CancellationToken);
        Assert.AreEqual("contract-test", provider.Received.Diagnostics!["requestId"]);
        Assert.IsNotNull(result.OutputImage);
    }

    private sealed class RecordingProvider : IImageAiProvider
    {
        public string Name => "Test replacement provider";
        public bool IsAvailable => true;
        public ImageAiRequest? Received { get; private set; }

        public Task<ImageAiResult> ExecuteAsync(ImageAiRequest request)
        {
            ImageAiRequestValidator.Validate(request);
            Received = request;
            return Task.FromResult(new ImageAiResult(
                ImageAiResultStatus.Succeeded,
                "test result",
                request.InputImage));
        }
    }
}
