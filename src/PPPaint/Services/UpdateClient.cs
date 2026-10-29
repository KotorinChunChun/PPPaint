using System.Net.Http;
using System.Text;
using PPPaint.Core;

namespace PPPaint.Services;

internal sealed class UpdateClient : IDisposable
{
    public const int MaximumManifestBytes = 65536;
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    private readonly HttpClient _client;

    public UpdateClient(HttpMessageHandler? testHandler = null) =>
        _client = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    public static Uri ValidateManifestUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("更新情報には資格情報・フラグメントのないHTTPS URLが必要です。");
        return uri;
    }

    public async Task<UpdateManifest> FetchManifestAsync(string url, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await DownloadBoundedAsync(ValidateManifestUri(url), buffer, MaximumManifestBytes, cancellationToken);
        var manifest = UpdateManifest.Parse(new UTF8Encoding(false, true).GetString(buffer.ToArray()));
        ValidatePackage(manifest);
        return manifest;
    }

    public static void ValidatePackage(UpdateManifest manifest)
    {
        manifest.Validate();
        var uri = new Uri(manifest.DownloadUrl);
        if (!uri.AbsolutePath.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("自動取得できる更新はMSIXパッケージだけです。");
        if (manifest.Publisher?.Length > 512) throw new InvalidDataException("発行者名が長すぎます。");
    }

    public async Task DownloadVerifiedAsync(UpdateManifest manifest, Version installedVersion,
        string destination, bool userConsented, CancellationToken cancellationToken)
    {
        if (!userConsented) throw new InvalidOperationException("更新取得への同意が必要です。");
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePackage(manifest);
        manifest.EnsureUpgradeFrom(installedVersion);
        if (!string.Equals(Path.GetExtension(destination), ".msix", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("保存先はMSIXファイルにしてください。");
        // 新規作成のみ。既存ファイルや実行中のアプリを上書きしない。
        var created = false;
        try
        {
            await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true);
            created = true;
            await DownloadBoundedAsync(new Uri(manifest.DownloadUrl), file, MaximumPackageBytes, cancellationToken);
            await file.FlushAsync(cancellationToken);
            file.Position = 0;
            if (!await manifest.VerifyPayloadAsync(file, cancellationToken))
                throw new InvalidDataException("更新パッケージのSHA-256が一致しません。取得したファイルを破棄しました。");
        }
        catch
        {
            if (created) File.Delete(destination);
            throw;
        }
    }

    private async Task DownloadBoundedAsync(Uri uri, Stream destination, long maximum, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("更新データが許容サイズを超えています。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = new byte[65536];
        long total = 0;
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken)) != 0)
        {
            total += count;
            if (total > maximum) throw new InvalidDataException("更新データが許容サイズを超えています。");
            await destination.WriteAsync(bytes.AsMemory(0, count), cancellationToken);
        }
    }

    public void Dispose() => _client.Dispose();
}
