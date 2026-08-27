using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace Atmos.Web.PlaywrightTests;

/// <summary>Priorities 6 and 7 of CLAUDE.md §18's browser-test list: the map picker and the forecast for a map-picked point.</summary>
public sealed class MapPickerTests(PlaywrightAppFactory factory, PlaywrightBrowserFixture browserFixture)
    : AtmosBrowserTest(factory, browserFixture)
{
    [Fact]
    public async Task Opening_the_picker_and_dropping_a_pin_enables_the_use_this_location_button()
    {
        await Page.GotoAsync("/");

        // An empty, freshly-focused search box still shows the "Select from
        // map…" suggestion item (search.js's renderSugg appends it
        // unconditionally, even for zero results).
        await Page.Locator("#search-input").ClickAsync();
        await Page.Locator(".sugg-map-item").ClickAsync();

        await Expect(Page.Locator("#map-picker-overlay")).ToHaveClassAsync(new Regex("open"));
        await Expect(Page.Locator("#map-picker-use")).ToBeDisabledAsync();

        var viewport = Page.Locator("#map-picker-viewport");
        var box = await viewport.BoundingBoxAsync();
        await Page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);

        await Expect(Page.Locator("#map-picker-pin")).ToBeVisibleAsync();
        await Expect(Page.Locator("#map-picker-use")).ToBeEnabledAsync();
        await Expect(Page.Locator("#map-picker-coords")).Not.ToHaveTextAsync("Click the map to drop a pin");
    }

    [Fact]
    public async Task Using_a_dropped_pin_closes_the_picker_and_renders_its_forecast()
    {
        await Page.GotoAsync("/");

        await Page.Locator("#search-input").ClickAsync();
        await Page.Locator(".sugg-map-item").ClickAsync();
        var viewport = Page.Locator("#map-picker-viewport");
        var box = await viewport.BoundingBoxAsync();
        await Page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        await Expect(Page.Locator("#map-picker-use")).ToBeEnabledAsync();

        await Page.Locator("#map-picker-use").ClickAsync();

        await Expect(Page.Locator("#map-picker-overlay")).Not.ToHaveClassAsync(new Regex("open"));
        // FakeNearbyPlaceService always resolves a name, so the picked point's
        // label gets the "Near <place>" suffix map-picker.js appends.
        await Expect(Page.Locator("#loc-name")).ToContainTextAsync("Near Table Mountain");
        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("72");
    }
}
