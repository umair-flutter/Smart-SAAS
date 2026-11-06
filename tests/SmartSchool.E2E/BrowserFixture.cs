using Microsoft.Playwright;

namespace SmartSchool.E2E;

/// <summary>
/// Starts one browser for all tests. Uses the installed Microsoft Edge (no browser download needed);
/// set E2E_BROWSER_CHANNEL=chrome to use Chrome, or leave empty after "playwright install" for bundled Chromium.
/// The app must already be running at E2E_BASE_URL (default http://localhost:5021) with demo data enabled.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5021").TrimEnd('/');
    public const string DemoPassword = "Demo@12345";
    public const string SuperAdminPassword = "Admin@12345";

    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // The Blazor WebAssembly app downloads the .NET runtime on first load; give assertions time.
        Assertions.SetDefaultExpectTimeout(30_000);
        _playwright = await Playwright.CreateAsync();
        var channel = Environment.GetEnvironmentVariable("E2E_BROWSER_CHANNEL") ?? "msedge";
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
            Headless = Environment.GetEnvironmentVariable("E2E_HEADED") != "1"
        });
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();
    }

    /// <summary>A fresh, isolated browser session (own storage, so tests never share a sign-in).</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            AcceptDownloads = true
        });
        context.SetDefaultTimeout(30_000);
        return await context.NewPageAsync();
    }

    /// <summary>Signs in through the login form and waits until the app shell has loaded.</summary>
    public static async Task SignInAsync(IPage page, string email, string password = DemoPassword)
    {
        await page.GotoAsync($"{BaseUrl}/login");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/login"));
        await page.Locator("aside.navbar-vertical").WaitForAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "browser";
}
