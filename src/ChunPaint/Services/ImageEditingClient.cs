using System.Buffers.Binary;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChunPaint.Services;

internal sealed class ImageEditingClient(HttpMessageHandler? testHandler = null)
{
    public const int MaximumResponseBytes = 40 * 1024 * 1024;
    public const int MaximumImageBytes = 20 * 1024 * 1024;

    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0)
            throw new ArgumentException("接続先は資格情報・クエリ・フラグメントを含まないHTTPS URLにしてください。");
        return uri;
    }

    public async Task<byte[]> EditAsync(string endpoint, string model, string apiKey, string prompt,
        byte[] image, byte[]? mask, bool userConsented, CancellationToken cancellationToken)
    {
        if (!userConsented) throw new InvalidOperationException("画像送信への同意が必要です。");
        cancellationToken.ThrowIfCancellationRequested();
        var uri = ValidateEndpoint(endpoint);
        if (string.IsNullOrWhiteSpace(model) || model.Length > 128 || string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("モデルとAPIキーが必要です。");
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 32000)
            throw new ArgumentException("編集指示は1～32000文字で入力してください。");
        var inputSize = ValidatePng(image);
        if (mask is not null && ValidatePng(mask) != inputSize)
            throw new ArgumentException("画像とマスクの寸法が一致していません。");
        using var handler = testHandler ?? new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("1"), "n");
        form.Add(new StringContent("png"), "output_format");
        form.Add(new StringContent("auto"), "size");
        var imagePart = new ByteArrayContent(image);
        imagePart.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imagePart, "image[]", "input.png");
        if (mask is not null)
        {
            var maskPart = new ByteArrayContent(mask);
            maskPart.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(maskPart, "mask", "mask.png");
        }
        request.Content = form;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"画像編集サービスがHTTP {(int)response.StatusCode}を返しました。認証・モデル権限・利用上限を確認してください。自動再試行は行いません。", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("画像編集の応答が上限40MiBを超えています。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaximumResponseBytes) throw new InvalidDataException("画像編集の応答が上限40MiBを超えています。");
            buffer.Write(chunk, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.ToArray());
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1 ||
            !data[0].TryGetProperty("b64_json", out var encoded) || encoded.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("単一のPNG画像データが応答にありません。");
        var result = System.Convert.FromBase64String(encoded.GetString()!);
        ValidatePng(result);
        return result;
    }

    public static (int Width, int Height) ValidatePng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || bytes.Length > MaximumImageBytes || !bytes.AsSpan(0, 8).SequenceEqual(signature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("画像は20MiB以下のPNGである必要があります。");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16 * 1024 * 1024)
            throw new InvalidDataException("画像寸法は各辺8192以下、合計16メガピクセル以下にしてください。");
        return (width, height);
    }
}
