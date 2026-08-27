using static Microsoft.Playwright.Assertions;

namespace Atmos.Web.PlaywrightTests;

/// <summary>Priorities 1 and 2 of CLAUDE.md §18's browser-test list: ZIP search and city autocomplete.</summary>
public sealed class SearchTests(PlaywrightAppFactory factory, PlaywrightBrowserFixture browserFixture)
    : AtmosBrowserTest(factory, browserFixture)
{
    [Fact]
    public async Task Searching_by_zip_renders_the_forecast()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");

        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");
        await Expect(Page.Locator("#loc-zip")).ToHaveTextAsync("ZIP 80002");
        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("72");
    }

    [Fact]
    public async Task Unknown_zip_shows_an_inline_error_instead_of_a_forecast()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("00000");
        await Page.Locator("#search-input").PressAsync("Enter");

        await Expect(Page.Locator("#status")).ToContainTextAsync("not found");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("—");
    }

    [Fact]
    public async Task Typing_a_city_name_shows_autocomplete_suggestions()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("Denver");

        await Expect(Page.Locator(".sugg-item", new() { HasTextString = "Denver, Colorado, US" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Selecting_an_autocomplete_suggestion_searches_that_location()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("Denver");
        await Page.Locator(".sugg-item", new() { HasTextString = "Denver, Colorado, US" }).ClickAsync();

        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Denver, Colorado, US");
        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("72");
    }
}
