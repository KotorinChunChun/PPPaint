using System.Security.Cryptography;
using System.Text.Json;

namespace ChunPaint.Core;

/// <summary>取得元の信頼確認は呼出側で行う。ハッシュだけでは発行元を認証できない。</summary>
public sealed record UpdateManifest(string Version, string DownloadUrl, string Sha256)
{
    public string? Publisher { get; init; }
    public static UpdateManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
            throw new ArgumentException("更新情報が空か大きすぎます。", nameof(json));
        var result = JsonSerializer.Deserialize<UpdateManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("更新情報がありません。", nameof(json));
        result.Validate();
        return result;
    }

    public System.Version Validate()
    {
        if (!System.Version.TryParse(Version, out var parsed) || parsed.Major < 0 ||
            parsed.Minor < 0 || parsed.Build < 0 || parsed.Major > 65535 ||
            parsed.Minor > 65535 || parsed.Build > 65535 || parsed.Revision > 65535)
            throw new ArgumentException("更新版は3桁または4桁の数値で指定してください。");
        if (!Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("更新URLには資格情報とフラグメントのないHTTPS URLが必要です。");
        if (Sha256 is null || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256は64桁の16進数で指定してください。");
        return Normalize(parsed);
    }

    public bool IsNewerThan(System.Version installedVersion)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        return Validate() > Normalize(installedVersion);
    }

    public void EnsureUpgradeFrom(System.Version installedVersion)
    {
        if (!IsNewerThan(installedVersion))
            throw new InvalidOperationException("同じ版または古い版への更新は実行できません。");
    }

    public async Task<bool> VerifyPayloadAsync(Stream payload, CancellationToken cancellationToken = default)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(payload);
        var actual = await SHA256.HashDataAsync(payload, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(Sha256));
    }

    private static System.Version Normalize(System.Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
