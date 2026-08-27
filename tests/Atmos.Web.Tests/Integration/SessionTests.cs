using Microsoft.AspNetCore.Mvc.Testing;

namespace Atmos.Web.Tests.Integration;

/// <summary>
/// Covers CLAUDE.md §18's "session behavior" integration requirement —
/// SessionCookieMiddleware's issue/validate/reuse logic.
/// </summary>
public sealed class SessionTests(AtmosWebApplicationFactory factory) : IClassFixture<AtmosWebApplicationFactory>
{
    [Fact]
    public async Task First_request_issues_a_session_cookie()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = Assert.Single(cookies!);
        Assert.Contains("sid=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_request_on_the_same_client_reuses_the_existing_session_instead_of_issuing_a_new_one()
    {
        var client = factory.CreateClient();
        await client.GetAsync("/");

        var response = await client.GetAsync("/");

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Malformed_session_cookie_is_replaced_with_a_freshly_issued_one()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "sid=not-a-valid-session-id");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = Assert.Single(cookies!);
        Assert.Contains("sid=", cookie);
        Assert.DoesNotContain("not-a-valid-session-id", cookie);
    }
}
