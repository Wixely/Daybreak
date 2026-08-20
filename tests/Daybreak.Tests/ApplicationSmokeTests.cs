using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Daybreak.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class ApplicationSmokeTests
{
    private string _directory = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daybreak-web-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", "test-password-long-enough");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Daybreak"] = $"Data Source={Path.Combine(_directory, "web.db")};Pooling=False",
                    ["Daybreak:SeedDemoData"] = "true",
                    ["Daybreak:DataProtectionKeysPath"] = Path.Combine(_directory, "keys"),
                }));
        });
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", null);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DashboardAndHealthEndpointRenderSuccessfully()
    {
        using var client = _factory.CreateClient();

        var dashboard = await client.GetAsync("/");
        var health = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, dashboard.StatusCode);
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        StringAssert.Contains(dashboardHtml, "Daybreak");
        StringAssert.Contains(dashboardHtml, "Take vitamins");
        StringAssert.Contains(dashboardHtml, "Daily");
        Assert.IsFalse(dashboardHtml.Contains("Tap when complete", StringComparison.Ordinal));
        StringAssert.Contains(dashboardHtml, "dashboard-brand-link");
        StringAssert.Contains(dashboardHtml, "dashboard-clock");
        Assert.IsFalse(dashboardHtml.Contains("<p class=\"eyebrow\">Today</p>", StringComparison.Ordinal));
        Assert.IsFalse(dashboardHtml.Contains("tabindex=\"-1\"", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
    }

    [TestMethod]
    public async Task DashboardSeparatesCompletedActivitiesAndServesRevealBehaviorLocally()
    {
        using var scope = _factory.Services.CreateScope();
        var board = scope.ServiceProvider.GetRequiredService<Daybreak.Services.BoardService>();
        var item = (await board.GetSnapshotAsync()).Items.First();
        Assert.IsTrue(await board.CompleteAsync(item.Id, item.Version));

        using var client = _factory.CreateClient();
        var dashboardHtml = await client.GetStringAsync("/");
        using var behavior = await client.GetAsync("/dashboard.js");
        var behaviorScript = await behavior.Content.ReadAsStringAsync();

        var pendingSection = dashboardHtml.IndexOf("aria-label=\"Activities to do\"", StringComparison.Ordinal);
        var completedSection = dashboardHtml.IndexOf("aria-label=\"Completed activities\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, pendingSection);
        Assert.IsGreaterThan(pendingSection, completedSection);
        Assert.AreEqual(HttpStatusCode.OK, behavior.StatusCode);
        StringAssert.Contains(behaviorScript, "IntersectionObserver");
        StringAssert.Contains(behaviorScript, "is-revealed");
        StringAssert.Contains(behaviorScript, "scrollTo");
        StringAssert.Contains(behaviorScript, "60_000");
        StringAssert.Contains(behaviorScript, "daybreak.keepAwake");
        StringAssert.Contains(behaviorScript, "localStorage");
        StringAssert.Contains(behaviorScript, "audio/wav");
    }

    [TestMethod]
    public async Task ConfiguredPasswordCreatesAnAdministratorSession()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var loginPage = await client.GetStringAsync("/admin/login");
        var token = AntiforgeryTokenRegex().Match(loginPage).Groups[1].Value;
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));

        using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["password"] = "test-password-long-enough",
                ["returnUrl"] = "/admin",
            }));

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/admin", response.Headers.Location?.OriginalString);
        using var admin = await client.GetAsync("/admin");
        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
        var adminHtml = await admin.Content.ReadAsStringAsync();
        StringAssert.Contains(adminHtml, "Configure Daybreak");
        StringAssert.Contains(adminHtml, "Take vitamins");
        StringAssert.Contains(adminHtml, "admin-heading");
        StringAssert.Contains(adminHtml, "catalog-panel");
        StringAssert.Contains(adminHtml, "aria-pressed");
    }

    [TestMethod]
    public async Task AdministrationRequiresLoginAndRejectsExternalReturnUrls()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using var unauthenticated = await client.GetAsync("/admin");
        Assert.AreEqual(HttpStatusCode.Redirect, unauthenticated.StatusCode);
        StringAssert.Contains(unauthenticated.Headers.Location?.OriginalString ?? string.Empty, "/admin/login");

        var loginPage = await client.GetStringAsync("/admin/login?returnUrl=%2F%2Fevil.example");
        var token = WebUtility.HtmlDecode(AntiforgeryTokenRegex().Match(loginPage).Groups[1].Value);
        using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["password"] = "test-password-long-enough",
                ["returnUrl"] = "//evil.example",
            }));

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/admin", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task AdministratorLoginIsRateLimited()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var loginPage = await client.GetStringAsync("/admin/login");
        var token = WebUtility.HtmlDecode(AntiforgeryTokenRegex().Match(loginPage).Groups[1].Value);
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["password"] = "incorrect-password",
                    ["returnUrl"] = "/admin",
                }));
            statuses.Add(response.StatusCode);
        }

        CollectionAssert.AreEqual(
            new[]
            {
                HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.Redirect,
                HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests,
            },
            statuses);
    }

    [GeneratedRegex("<input[^>]+name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
