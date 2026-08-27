using Microsoft.Playwright;

namespace Atmos.Web.PlaywrightTests;

/// <summary>
/// One Chromium instance shared across every Playwright test class in the
/// "Playwright" collection — launching a browser per test would dominate the
/// suite's runtime for no isolation benefit, since each test still gets its
/// own IBrowserContext (and therefore its own cookies/session).
/// </summary>
public sealed class PlaywrightBrowserFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Installs the Chromium binary into Playwright's local cache if it
        // isn't already there (no-op, fast, if it is) — the alternative,
        // running the generated playwright.ps1, needs PowerShell, which
        // isn't part of this project's toolchain (CLAUDE.md §20).
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright browser install failed with exit code {exitCode}.");
        }

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        Playwright.Dispose();
    }
}

[CollectionDefinition("Playwright")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightBrowserFixture>;
