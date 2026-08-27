using static Microsoft.Playwright.Assertions;

namespace Atmos.Web.PlaywrightTests;

/// <summary>Priority 4 of CLAUDE.md §18's browser-test list: recent-search selection.</summary>
public sealed class RecentSearchTests(PlaywrightAppFactory factory, PlaywrightBrowserFixture browserFixture)
    : AtmosBrowserTest(factory, browserFixture)
{
    [Fact]
    public async Task Selecting_a_recent_search_reloads_that_forecast()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");

        await Page.Locator("#search-input").FillAsync("90210");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Beverly Hills, CA");

        await Expect(Page.Locator("#recent-list .recent-item")).ToHaveCountAsync(2);

        await Page.Locator("#recent-list .recent-item", new() { HasTextString = "Arvada, CO" }).ClickAsync();

        // Recent items re-fetch by their saved lat/lon (weather.js's
        // selectRecent), not the original ZIP, so the response has no zip —
        // loc-name reverting is what actually proves the right row was
        // reselected.
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");
    }
}
