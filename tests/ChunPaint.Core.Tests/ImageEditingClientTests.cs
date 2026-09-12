using System.Net;
using System.Text.Json;
using ChunPaint.Services;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ImageEditingClientTests
{
    private static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");

    [TestMethod]
    public async Task SendsExplicitMultipartAndReturnsImageWithoutNetwork()
    {
        var png = Png();
        var handler = new FakeHandler(async request =>
        {
            Assert.AreEqual("Bearer test-key", request.Headers.Authorization!.ToString());
            Assert.AreEqual("https://example.com/v1/images/edits", request.RequestUri!.AbsoluteUri);
            var body = await request.Content!.ReadAsStringAsync();
            StringAssert.Contains(body, "image[]");
            StringAssert.Contains(body, "mask");
            StringAssert.Contains(body, "requested-model");
            StringAssert.Contains(body, "remove subject");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } })) };
        });
        var result = await new ImageEditingClient(handler).EditAsync("https://example.com/v1/images/edits", "requested-model", "test-key", "remove subject", png, png, true, default);
        CollectionAssert.AreEqual(png, result);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task NoConsentOrCancellationNeverSends()
    {
        var handler = new FakeHandler(_ => throw new AssertFailedException("送信してはいけません。"));
        var client = new ImageEditingClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.EditAsync("https://example.com/edit", "model", "key", "edit", Png(), null, false, default));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => client.EditAsync("https://example.com/edit", "model", "key", "edit", Png(), null, true, new CancellationToken(true)));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task RejectsUrlResponsesAndOversizedResponseWithoutFollowingThem()
    {
        var urlHandler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"url\":\"https://elsewhere.example/image\"}]}") }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new ImageEditingClient(urlHandler).EditAsync("https://example.com/edit", "model", "key", "edit", Png(), null, true, default));
        var largeHandler = new FakeHandler(_ =>
        {
            var content = new StringContent("{}"); content.Headers.ContentLength = ImageEditingClient.MaximumResponseBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new ImageEditingClient(largeHandler).EditAsync("https://example.com/edit", "model", "key", "edit", Png(), null, true, default));
    }

    [TestMethod]
    public void RejectsInsecureEndpointsAndInvalidImageHeaders()
    {
        foreach (var uri in new[] { "http://example.com/edit", "https://key@example.com/edit", "https://example.com/edit#fragment" })
            Assert.ThrowsExactly<ArgumentException>(() => ImageEditingClient.ValidateEndpoint(uri));
        Assert.ThrowsExactly<InvalidDataException>(() => ImageEditingClient.ValidatePng([1, 2, 3]));
        var png = Png(); png[16] = 127;
        Assert.ThrowsExactly<InvalidDataException>(() => ImageEditingClient.ValidatePng(png));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request);
        }
    }
}
