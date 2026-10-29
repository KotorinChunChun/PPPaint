using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PPPaint.Core;
using PPPaint.Services;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class UpdateClientTests
{
    private static UpdateManifest Manifest(byte[] bytes) => new("0.3.0", "https://example.com/PPPaint.msix", Convert.ToHexString(SHA256.HashData(bytes))) { Publisher = "CN=PPPaint" };

    [TestMethod]
    public async Task FetchesBoundedManifestWithoutNetwork()
    {
        var expected = Manifest([1, 2, 3]);
        using var client = new UpdateClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(expected)) }));
        Assert.AreEqual(expected, await client.FetchManifestAsync("https://example.com/update.json", default));
    }

    [TestMethod]
    public async Task RejectsRedirectAndOversizedManifest()
    {
        using var redirect = new UpdateClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => redirect.FetchManifestAsync("https://example.com/update.json", default));
        using var large = new UpdateClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string(' ', UpdateClient.MaximumManifestBytes + 1)) }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => large.FetchManifestAsync("https://example.com/update.json", default));
    }

    [TestMethod]
    public async Task VerifiesDownloadAndDeletesCorruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "pppaint-update-test-" + Guid.NewGuid() + ".msix");
        byte[] bytes = [1, 2, 3];
        using var client = new UpdateClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        try
        {
            await client.DownloadVerifiedAsync(Manifest(bytes), new Version(0, 2, 0), path, true, default);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            File.Delete(path);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.DownloadVerifiedAsync(Manifest([8, 9]), new Version(0, 2, 0), path, true, default));
            Assert.IsFalse(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    public async Task RejectsNoConsentDowngradeAndExecutableBeforeSending()
    {
        var handler = new Handler(_ => throw new AssertFailedException("送信してはいけません。"));
        using var client = new UpdateClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.DownloadVerifiedAsync(Manifest([1]), new Version(0, 2, 0), "unused.msix", false, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.DownloadVerifiedAsync(Manifest([1]), new Version(0, 4, 0), "unused.msix", true, default));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => client.DownloadVerifiedAsync(Manifest([1]), new Version(0, 2, 0), "unused.msix", true, new CancellationToken(true)));
        Assert.ThrowsExactly<InvalidDataException>(() => UpdateClient.ValidatePackage(Manifest([1]) with { DownloadUrl = "https://example.com/file.exe" }));
        Assert.AreEqual(0, handler.Calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(respond(request)); }
    }
}
