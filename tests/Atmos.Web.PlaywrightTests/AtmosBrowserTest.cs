using Microsoft.Playwright;

namespace Atmos.Web.PlaywrightTests;

/// <summary>
/// Base for every D17 browser test: a fresh IBrowserContext/IPage per test
/// method (own cookies -&gt; own session, so tests in the same class don't
/// interfere with each other's recent-search state) against the one
/// PlaywrightAppFactory shared for the whole test class (via IClassFixture,
/// same pattern as D16's AtmosWebApplicationFactory).
/// </summary>
[Collection("Playwright")]
public abstract class AtmosBrowserTest : IClassFixture<PlaywrightAppFactory>, IAsyncLifetime
{
    private readonly PlaywrightBrowserFixture _browserFixture;
    protected readonly PlaywrightAppFactory Factory;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    protected AtmosBrowserTest(PlaywrightAppFactory factory, PlaywrightBrowserFixture browserFixture)
    {
        Factory = factory;
        _browserFixture = browserFixture;
    }

    public async Task InitializeAsync()
    {
        Context = await _browserFixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Factory.ServerAddress,
        });
        Page = await Context.NewPageAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();
}
