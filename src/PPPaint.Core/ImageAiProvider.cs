namespace PPPaint.Core;

public enum ImageAiFeature
{
    BackgroundRemoval,
    GenerativeErase,
    CopilotEdit
}

public enum ImageAiPixelFormat
{
    Bgra32,
    Unknown
}

public enum ImageAiResultStatus
{
    Succeeded,
    Disabled,
    Cancelled,
    InvalidRequest,
    Failed
}

public sealed record ImageAiRequest(
    ImageAiFeature Feature,
    PixelSurface InputImage,
    SelectionRegion? Selection = null,
    CancellationToken CancellationToken = default,
    IReadOnlyDictionary<string, string>? Diagnostics = null,
    ImageAiPixelFormat InputFormat = ImageAiPixelFormat.Bgra32);

public sealed record ImageAiResult(
    ImageAiResultStatus Status,
    string Message,
    PixelSurface? OutputImage = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null)
{
    public bool IsAvailableResult => Status == ImageAiResultStatus.Succeeded;
}

public interface IImageAiProvider
{
    string Name { get; }

    bool IsAvailable { get; }

    Task<ImageAiResult> ExecuteAsync(ImageAiRequest request);
}

public static class ImageAiRequestValidator
{
    public const int MaximumDimension = 8192;
    public const long MaximumInputBytes = 64L * 1024 * 1024;

    public static void Validate(ImageAiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request.InputImage);
        if (request.InputFormat != ImageAiPixelFormat.Bgra32)
        {
            throw new ArgumentException("Only BGRA32 input is supported by the local provider boundary.", nameof(request));
        }
        if (request.InputImage.Width > MaximumDimension || request.InputImage.Height > MaximumDimension ||
            (long)request.InputImage.Width * request.InputImage.Height * 4 > MaximumInputBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The AI input image exceeds the safety limit.");
        }
        if (request.Selection is not null &&
            (request.Selection.Bounds.X < 0 || request.Selection.Bounds.Y < 0 ||
             request.Selection.Bounds.Right > request.InputImage.Width ||
             request.Selection.Bounds.Bottom > request.InputImage.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The AI selection is outside the input image.");
        }
    }
}

public sealed class DisabledImageAiProvider : IImageAiProvider
{
    public string Name => "Disabled image AI provider";

    public bool IsAvailable => false;

    public Task<ImageAiResult> ExecuteAsync(ImageAiRequest request)
    {
        try
        {
            ImageAiRequestValidator.Validate(request);
            return Task.FromResult(CreateDisabledResult(request.Feature));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(CreateResult(ImageAiResultStatus.Cancelled, "画像AI処理はキャンセルされました。"));
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(CreateResult(ImageAiResultStatus.InvalidRequest, exception.Message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(CreateResult(ImageAiResultStatus.Failed, $"画像AI要求を受け付けられませんでした: {exception.Message}"));
        }
    }

    private static ImageAiResult CreateDisabledResult(ImageAiFeature feature) =>
        CreateResult(
            ImageAiResultStatus.Disabled,
            $"{feature} は現在無効です。外部サービスへの画像送信は行っていません。将来は IImageAiProvider の差し替え箇所から接続します。");

    private static ImageAiResult CreateResult(ImageAiResultStatus status, string message) =>
        new(status, message, Diagnostics: new Dictionary<string, string>
        {
            ["provider"] = nameof(DisabledImageAiProvider),
            ["networkAccess"] = "none"
        });
}

public sealed class LocalUnavailableProvider : IImageAiProvider
{
    public string Name => "Local image AI unavailable";

    public bool IsAvailable => false;

    public Task<ImageAiResult> ExecuteAsync(ImageAiRequest request)
    {
        try
        {
            ImageAiRequestValidator.Validate(request);
            return Task.FromResult(new ImageAiResult(
                ImageAiResultStatus.Disabled,
                "この環境ではローカル画像AIを利用できません。画像は外部へ送信されていません。",
                Diagnostics: new Dictionary<string, string>
                {
                    ["provider"] = nameof(LocalUnavailableProvider),
                    ["networkAccess"] = "none"
                }));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new ImageAiResult(ImageAiResultStatus.Cancelled, "画像AI処理はキャンセルされました."));
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(new ImageAiResult(ImageAiResultStatus.InvalidRequest, exception.Message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new ImageAiResult(ImageAiResultStatus.Failed, $"画像AI要求を受け付けられませんでした: {exception.Message}"));
        }
    }
}
