using System.Net;

namespace Atmos.Core.Tests.TestSupport;

/// <summary>
/// Routes requests to a caller-supplied function instead of the network — used
/// to unit-test the HTTP-calling services against fixture JSON with zero live
/// network dependency (Phase B §16).
/// </summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new FakeHttpMessageHandler(respond));

    public static HttpClient CreateJsonClient(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        CreateClient(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
