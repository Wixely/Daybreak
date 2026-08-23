using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using Dapper;
using Daybreak.Data;
using Daybreak.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

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
                    ["Daybreak:EnableApi"] = "true",
                    ["Daybreak:EnableMcp"] = "true",
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
        StringAssert.Contains(dashboardHtml, "Take morning vitamins");
        StringAssert.Contains(dashboardHtml, "Collect the parcel");
        StringAssert.Contains(dashboardHtml, "Daily");
        Assert.IsFalse(dashboardHtml.Contains("Tap when complete", StringComparison.Ordinal));
        StringAssert.Contains(dashboardHtml, "dashboard-brand-link");
        StringAssert.Contains(dashboardHtml, "dashboard-clock");
        StringAssert.Contains(dashboardHtml, "dashboard-fullscreen-button");
        StringAssert.Contains(dashboardHtml, "Open Daybreak in full screen");
        Assert.IsFalse(dashboardHtml.Contains("<p class=\"eyebrow\">Today</p>", StringComparison.Ordinal));
        Assert.IsFalse(dashboardHtml.Contains("tabindex=\"-1\"", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
    }

    [TestMethod]
    public async Task DemoDataIncludesAFullBoardAndVariedHistory()
    {
        using var scope = _factory.Services.CreateScope();
        var board = scope.ServiceProvider.GetRequiredService<Daybreak.Services.BoardService>();
        var history = scope.ServiceProvider.GetRequiredService<Daybreak.Services.HistoryService>();

        var boardSnapshot = await board.GetSnapshotAsync();
        var historySnapshot = await history.GetAsync();

        Assert.AreEqual(16, boardSnapshot.Items.Count);
        Assert.IsGreaterThan(100, historySnapshot.Total);
        Assert.IsGreaterThan(0, historySnapshot.OnTime);
        Assert.IsGreaterThan(0, historySnapshot.Late);
        Assert.IsGreaterThan(0, historySnapshot.Unfinished);
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
        StringAssert.Contains(behaviorScript, "navigator.userAgentData?.mobile");
        StringAssert.Contains(behaviorScript, "window.self === window.top");
        StringAssert.Contains(behaviorScript, "requestFullscreen");
        StringAssert.Contains(behaviorScript, "fullscreenchange");
        StringAssert.Contains(behaviorScript, "navigator.clipboard");
        StringAssert.Contains(behaviorScript, "execCommand(\"copy\")");
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
        StringAssert.Contains(adminHtml, "Take morning vitamins");
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

    [TestMethod]
    public async Task ApiRequiresDeploymentAndApplicationActivationAndBearerKey()
    {
        using var client = _factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/board")).StatusCode);

        string secret;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<AgentAccessService>();
            secret = (await access.GenerateAsync(AgentSurface.Api)).Secret;
            await access.UpdateEnabledAsync(apiEnabled: true, mcpEnabled: false);
        }

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/board")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        using var response = await client.GetAsync("/api/v1/board");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var board = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = board.RootElement.GetProperty("items")[0];
        var occurrenceId = item.GetProperty("id").GetString();
        var expectedVersion = item.GetProperty("version").GetInt64();

        using var completed = await client.PostAsJsonAsync(
            $"/api/v1/occurrences/{occurrenceId}/complete",
            new { expectedVersion });
        Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode);
        StringAssert.Contains(await completed.Content.ReadAsStringAsync(), "\"applied\":true");

        await using var auditScope = _factory.Services.CreateAsyncScope();
        var connections = auditScope.ServiceProvider.GetRequiredService<DatabaseConnectionFactory>();
        await using var connection = await connections.OpenAsync();
        Assert.IsGreaterThanOrEqualTo(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AgentAccessEvents WHERE Surface = 'Api'"));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AgentAccessEvents WHERE Path LIKE @Secret",
            new { Secret = $"%{secret}%" }));
    }

    [TestMethod]
    public async Task McpCanBeEnabledWithoutMcpKeyThenRequireOneAfterGeneration()
    {
        using var client = _factory.CreateClient();
        string mcpKey;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<AgentAccessService>();
            await access.GenerateAsync(AgentSurface.Api);
            await access.UpdateEnabledAsync(apiEnabled: true, mcpEnabled: true);
        }

        using var unauthenticatedRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        using var unauthenticated = await client.SendAsync(unauthenticatedRequest);
        Assert.AreNotEqual(HttpStatusCode.NotFound, unauthenticated.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await using (var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(client.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            client,
            NullLoggerFactory.Instance,
            ownsHttpClient: false))
        await using (var mcpClient = await McpClient.CreateAsync(transport))
        {
            var tools = await mcpClient.ListToolsAsync();
            Assert.IsTrue(tools.Any(tool => tool.Name == "get_board"));
            Assert.IsTrue(tools.Any(tool => tool.Name == "save_activity"));
            var boardResult = await mcpClient.CallToolAsync("get_board", new Dictionary<string, object?>());
            Assert.IsFalse(boardResult.IsError ?? false);
            Assert.IsNotNull(boardResult.StructuredContent);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            mcpKey = (await scope.ServiceProvider.GetRequiredService<AgentAccessService>()
                .GenerateAsync(AgentSurface.Mcp)).Secret;
        }

        using var deniedRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.SendAsync(deniedRequest)).StatusCode);

        using var keyedRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        keyedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcpKey);
        using var keyed = await client.SendAsync(keyedRequest);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, keyed.StatusCode);
    }

    [GeneratedRegex("<input[^>]+name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
