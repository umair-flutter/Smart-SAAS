using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace SmartSchool.E2E;

/// <summary>End-to-end checks of the main journeys, run against the demo data.</summary>
[Collection(BrowserCollection.Name)]
public class FunctionalTests(BrowserFixture fx)
{
    private static string Url(string path) => $"{BrowserFixture.BaseUrl}/{path.TrimStart('/')}";

    [Fact]
    public async Task Login_page_lists_the_demo_accounts()
    {
        var page = await fx.NewPageAsync();
        await page.GotoAsync(Url("login"));

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Quick role access")).ToBeVisibleAsync();
        await Expect(page.GetByText("superadmin@smartschool.local")).ToBeVisibleAsync();
        await Expect(page.GetByText("teacher@demo-academy.local")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var page = await fx.NewPageAsync();
        await page.GotoAsync(Url("login"));
        await page.Locator("#email").FillAsync("teacher@demo-academy.local");
        await page.Locator("#password").FillAsync("not-the-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();

        await Expect(page.GetByText("Invalid email or password.")).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/login"));
    }

    [Fact]
    public async Task SuperAdmin_sees_the_platform_and_all_tenants()
    {
        var page = await fx.NewPageAsync();
        await BrowserFixture.SignInAsync(page, "superadmin@smartschool.local", BrowserFixture.SuperAdminPassword);

        await Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Welcome back") })).ToBeVisibleAsync();
        await page.Locator("aside.navbar-vertical a[href='tenants']").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { NameRegex = new Regex("Demo Academy") })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { NameRegex = new Regex("Riverside Institute") })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Teacher_has_no_platform_menu_and_can_open_an_attendance_sheet()
    {
        var page = await fx.NewPageAsync();
        await BrowserFixture.SignInAsync(page, "teacher@demo-academy.local");

        var menu = page.Locator("aside.navbar-vertical");
        await Expect(menu.Locator("a[href='attendance']")).ToBeVisibleAsync();
        await Expect(menu.Locator("a[href='tenants']")).ToHaveCountAsync(0);
        await Expect(menu.Locator("a[href='invoices']")).ToHaveCountAsync(0);

        await page.GotoAsync(Url("attendance"));
        await page.Locator("select").Filter(new() { HasText = "Choose a class" }).SelectOptionAsync(new SelectOptionValue { Label = "Grade 5 A" });
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save attendance" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Nour Tazi")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Student_cannot_open_the_student_list()
    {
        var page = await fx.NewPageAsync();
        await BrowserFixture.SignInAsync(page, "student@demo-academy.local");

        await Expect(page.Locator("aside.navbar-vertical a[href='students']")).ToHaveCountAsync(0);
        await page.GotoAsync(Url("students"));
        await Expect(page.GetByText("You do not have permission to do this.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Parent_opens_the_published_report_card_and_downloads_the_pdf()
    {
        var page = await fx.NewPageAsync();
        await BrowserFixture.SignInAsync(page, "parent@demo-academy.local");

        await page.GotoAsync(Url("report-cards"));
        await page.GetByRole(AriaRole.Link).Filter(new() { HasText = "Term 1" }).ClickAsync();
        await Expect(page.GetByText("Overall average")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Nour Tazi") }).First).ToBeVisibleAsync();

        var download = await page.RunAndWaitForDownloadAsync(() =>
            page.GetByRole(AriaRole.Button, new() { Name = "English" }).ClickAsync());
        Assert.EndsWith(".pdf", download.SuggestedFilename);
        var path = await download.PathAsync();
        Assert.True(new FileInfo(path!).Length > 10_000, "The PDF should not be empty.");
    }

    [Fact]
    public async Task Parent_sees_notifications_in_the_bell()
    {
        var page = await fx.NewPageAsync();
        await BrowserFixture.SignInAsync(page, "parent@demo-academy.local");

        await page.Locator("header").GetByTitle("Notifications").ClickAsync();
        await Expect(page.GetByText("View all")).ToBeVisibleAsync();
        await Expect(page.GetByText("Welcome to SmartSchool")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Arabic_switches_the_interface_to_right_to_left()
    {
        var page = await fx.NewPageAsync();
        await page.GotoAsync(Url("login"));
        await page.GetByRole(AriaRole.Button, new() { Name = "العربية" }).ClickAsync();

        await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "تسجيل الدخول" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Français" }).ClickAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "ltr");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Se connecter" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Language_picked_before_sign_in_is_kept_after_sign_in()
    {
        var page = await fx.NewPageAsync();
        await page.GotoAsync(Url("login"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Français" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Se connecter" })).ToBeVisibleAsync();

        await page.Locator("#email").FillAsync("schooladmin@demo-academy.local");
        await page.Locator("#password").FillAsync(BrowserFixture.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Se connecter", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/login"));

        var menu = page.Locator("aside.navbar-vertical");
        await Expect(menu.GetByText("Élèves", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(menu.GetByText("Emploi du temps", new() { Exact = true })).ToBeVisibleAsync();

        // Put the demo account back to English for the other tests.
        var token = await page.EvaluateAsync<string>("localStorage.getItem('ss.token')");
        await page.APIRequest.PutAsync(Url("api/me/profile"), new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            DataObject = new { firstName = "Karim", lastName = "Mansour", language = "en" }
        });
    }

    [Fact]
    public async Task Health_endpoint_reports_the_database()
    {
        var page = await fx.NewPageAsync();
        var response = await page.APIRequest.GetAsync(Url("health"));
        Assert.True(response.Ok);
        Assert.Contains("\"status\":\"Healthy\"", await response.TextAsync());
    }
}
