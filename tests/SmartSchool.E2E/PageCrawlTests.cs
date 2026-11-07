using Microsoft.Playwright;

namespace SmartSchool.E2E;

/// <summary>
/// Signs in as every demo role and opens every page of that role's menu. A page fails when it shows an
/// error alert, the Blazor crash banner, or makes an API call that returns 4xx/5xx — the menu only lists
/// pages the role is allowed to use, so any such error is a bug.
/// </summary>
[Collection(BrowserCollection.Name)]
public class PageCrawlTests(BrowserFixture fx)
{
    public static TheoryData<string, string> Accounts => new()
    {
        { "superadmin@smartschool.local", BrowserFixture.SuperAdminPassword },
        { "admin@demo-academy.local", BrowserFixture.DemoPassword },
        { "schooladmin@demo-academy.local", BrowserFixture.DemoPassword },
        { "teacher@demo-academy.local", BrowserFixture.DemoPassword },
        { "parent@demo-academy.local", BrowserFixture.DemoPassword },
        { "student@demo-academy.local", BrowserFixture.DemoPassword }
    };

    [Theory]
    [MemberData(nameof(Accounts))]
    public async Task Every_menu_page_loads_without_errors(string email, string password)
    {
        var page = await fx.NewPageAsync();
        var failures = new List<string>();
        var current = "sign-in";

        page.Response += (_, r) =>
        {
            if (r.Url.Contains("/api/") && r.Status >= 400)
                failures.Add($"{current}: {r.Request.Method} {new Uri(r.Url).PathAndQuery} -> {r.Status}");
        };
        page.PageError += (_, e) => failures.Add($"{current}: script error {e}");

        await BrowserFixture.SignInAsync(page, email, password);

        var hrefs = await page.Locator("aside.navbar-vertical a.nav-link").EvaluateAllAsync<string[]>(
            "links => links.map(a => a.getAttribute('href'))");
        Assert.NotEmpty(hrefs);

        foreach (var href in hrefs.Distinct())
        {
            current = string.IsNullOrEmpty(href) ? "dashboard" : href;
            await page.Locator($"aside.navbar-vertical a.nav-link[href='{href}']").ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            // Give components a moment to finish rendering after their API calls.
            await page.WaitForTimeoutAsync(300);

            if (await page.Locator("#blazor-error-ui").IsVisibleAsync())
                failures.Add($"{current}: the Blazor error banner is shown");

            foreach (var alert in await page.Locator(".page-body .alert-danger").AllAsync())
                if (await alert.IsVisibleAsync())
                    failures.Add($"{current}: error alert \"{(await alert.InnerTextAsync()).Trim()}\"");
        }

        Assert.True(failures.Count == 0, $"{email} hit {failures.Count} problem(s):\n" + string.Join("\n", failures));
    }
}
