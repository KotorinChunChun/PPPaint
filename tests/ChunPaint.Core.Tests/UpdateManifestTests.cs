using System.Security.Cryptography;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class UpdateManifestTests
{
    private static UpdateManifest Valid() => new("0.2.0", "https://example.com/app.msix", new string('a', 64));

    [TestMethod]
    public void RejectsDowngradeAndEquivalentVersions()
    {
        Assert.IsTrue(Valid().IsNewerThan(new Version(0, 1, 9)));
        Assert.ThrowsExactly<InvalidOperationException>(() => Valid().EnsureUpgradeFrom(new Version(0, 2, 0, 0)));
        Assert.ThrowsExactly<InvalidOperationException>(() => Valid().EnsureUpgradeFrom(new Version(0, 3, 0)));
    }

    [TestMethod]
    public void RejectsUnsafeOrMalformedMetadata()
    {
        foreach (var url in new[] { "http://example.com/app", "file:///C:/app", "https://user:secret@example.com/app", "https://example.com/app#fragment" })
            Assert.ThrowsExactly<ArgumentException>(() => (Valid() with { DownloadUrl = url }).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => (Valid() with { Sha256 = "invalid" }).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => (Valid() with { Version = "next" }).Validate());
    }

    [TestMethod]
    public async Task VerifiesActualBytesAndDetectsCorruption()
    {
        byte[] bytes = [1, 2, 3, 4];
        var manifest = Valid() with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        Assert.IsTrue(await manifest.VerifyPayloadAsync(new MemoryStream(bytes)));
        Assert.IsFalse(await manifest.VerifyPayloadAsync(new MemoryStream([1, 2, 3, 5])));
    }
}
