using System.Net;

namespace Atmos.Web.Tests.TestSupport;

public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new FakeHttpMessageHandler(respond));

    public static HttpClient CreateJsonClient(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        CreateClient(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    /// <summary>Routes by request URI so a single client can answer two calls differently (Overpass then Nominatim).</summary>
    public static HttpClient CreateSequencedClient(params (Func<HttpRequestMessage, bool> Match, HttpResponseMessage Response)[] routes) =>
        CreateClient(req =>
        {
            foreach (var (match, response) in routes)
            {
                if (match(req))
                {
                    return response;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
