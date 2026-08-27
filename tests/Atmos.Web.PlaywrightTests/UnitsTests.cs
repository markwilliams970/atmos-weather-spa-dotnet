using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace Atmos.Web.PlaywrightTests;

/// <summary>Priority 5 of CLAUDE.md §18's browser-test list: unit switching.</summary>
public sealed class UnitsTests(PlaywrightAppFactory factory, PlaywrightBrowserFixture browserFixture)
    : AtmosBrowserTest(factory, browserFixture)
{
    [Fact]
    public async Task Switching_to_metric_updates_the_displayed_temperature_and_active_toggle()
    {
        await Page.GotoAsync("/");
        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("72");
        await Expect(Page.Locator("#unit-f")).ToHaveClassAsync(new Regex("active"));

        await Page.Locator("#unit-c").ClickAsync();

        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("22");
        await Expect(Page.Locator("#temp-unit")).ToHaveTextAsync("°C");
        await Expect(Page.Locator("#unit-c")).ToHaveClassAsync(new Regex("active"));
        await Expect(Page.Locator("#unit-f")).Not.ToHaveClassAsync(new Regex("active"));
    }

    [Fact]
    public async Task Switching_back_to_imperial_restores_the_fahrenheit_reading()
    {
        await Page.GotoAsync("/");
        await Page.Locator("#search-input").FillAsync("80002");
        await Page.Locator("#search-input").PressAsync("Enter");
        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("72");

        await Page.Locator("#unit-c").ClickAsync();
        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("22");

        await Page.Locator("#unit-f").ClickAsync();

        await Expect(Page.Locator("#temp-val")).ToHaveTextAsync("72");
        await Expect(Page.Locator("#temp-unit")).ToHaveTextAsync("°F");
    }
}
