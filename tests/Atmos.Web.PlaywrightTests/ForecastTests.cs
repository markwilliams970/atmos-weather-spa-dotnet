using static Microsoft.Playwright.Assertions;

namespace Atmos.Web.PlaywrightTests;

/// <summary>Priorities 3 and 8 of CLAUDE.md §18's browser-test list: forecast rendering (current/hourly/daily tabs) and radar rendering.</summary>
public sealed class ForecastTests(PlaywrightAppFactory factory, PlaywrightBrowserFixture browserFixture)
    : AtmosBrowserTest(factory, browserFixture)
{
    [Fact]
    public async Task Current_tab_renders_conditions_from_the_forecast_response()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");

        await Expect(Page.Locator("#cond-label")).ToHaveTextAsync("Clear sky");
        await Expect(Page.Locator("#humidity-val")).ToHaveTextAsync("45%");
        await Expect(Page.Locator("#feels-like")).ToHaveTextAsync("70°F");
        await Expect(Page.Locator("#today-hi")).ToHaveTextAsync("78°F");
        await Expect(Page.Locator("#today-lo")).ToHaveTextAsync("55°F");
    }

    [Fact]
    public async Task Hourly_tab_renders_the_hourly_forecast()
    {
        await Page.GotoAsync("/");
        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");

        await Page.Locator(".tab-btn[data-tab='hourly']").ClickAsync();

        await Expect(Page.Locator("#tab-hourly")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        Assert.True(await Page.Locator("#hour-scroll > *").CountAsync() > 0);
    }

    [Fact]
    public async Task Daily_tab_renders_the_seven_day_forecast()
    {
        await Page.GotoAsync("/");
        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");

        await Page.Locator(".tab-btn[data-tab='daily']").ClickAsync();

        await Expect(Page.Locator("#tab-daily")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        Assert.True(await Page.Locator("#daily-list > *").CountAsync() > 0);
    }

    [Fact]
    public async Task Radar_card_renders_tiles_after_a_search()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");

        // renderRadarMap (radar.js) is fire-and-forget from populate() — it
        // awaits its own /api/radar/frame fetch internally, so the tiles can
        // land after loc-name is already set. Expect(...).First auto-retries
        // until the image actually appears, unlike a one-shot CountAsync().
        await Expect(Page.Locator("#radar-map img").First).ToBeAttachedAsync();
    }

    [Fact]
    public async Task Air_quality_card_renders_from_the_air_quality_endpoint()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#loc-name")).ToHaveTextAsync("Arvada, CO");

        await Expect(Page.Locator("#aqi-val")).ToHaveTextAsync("42");
        await Expect(Page.Locator("#aqi-cat")).ToHaveTextAsync("Good");
    }
}
