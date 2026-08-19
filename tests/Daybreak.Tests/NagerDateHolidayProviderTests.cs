using System.Net;
using System.Text;
using Daybreak.Data;
using Daybreak.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daybreak.Tests;

[TestClass]
public sealed class NagerDateHolidayProviderTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daybreak-holiday-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FiltersSubdivisionReusesCacheAndSupportsForcedRefresh()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Daybreak"] = $"Data Source={Path.Combine(_directory, "holidays.db")};Pooling=False",
        }).Build();
        var connections = new DatabaseConnectionFactory(configuration, new TestEnvironment(_directory));
        await new MigrationRunner(connections, NullLogger<MigrationRunner>.Instance).MigrateAsync();
        var handler = new HolidayResponseHandler();
        var provider = new NagerDateHolidayProvider(
            connections,
            new StaticHttpClientFactory(handler),
            new FixedTimeProvider(new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<NagerDateHolidayProvider>.Instance);

        var first = await provider.GetHolidayDatesAsync(2026, "GB", "GB-ENG");
        var second = await provider.GetHolidayDatesAsync(2026, "GB", "GB-ENG");
        var refreshed = await provider.RefreshHolidayDatesAsync(2026, "GB", "GB-ENG");
        handler.FailRequests = true;
        var fallback = await provider.RefreshHolidayDatesAsync(2026, "GB", "GB-ENG");

        Assert.IsTrue(first.Contains(new DateOnly(2026, 1, 1)));
        Assert.IsTrue(first.Contains(new DateOnly(2026, 8, 31)));
        Assert.IsFalse(first.Contains(new DateOnly(2026, 8, 3)));
        CollectionAssert.AreEquivalent(first.ToList(), second.ToList());
        CollectionAssert.AreEquivalent(first.ToList(), refreshed.ToList());
        CollectionAssert.AreEquivalent(first.ToList(), fallback.ToList());
        Assert.AreEqual(3, handler.RequestCount);
    }

    private sealed class HolidayResponseHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool FailRequests { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (FailRequests)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            const string json = """
                [
                  {"date":"2026-01-01","localName":"New Year's Day","name":"New Year's Day","global":true,"counties":null},
                  {"date":"2026-08-31","localName":"Summer bank holiday","name":"Summer bank holiday","global":false,"counties":["GB-ENG","GB-WLS"]},
                  {"date":"2026-08-03","localName":"Summer bank holiday","name":"Summer bank holiday","global":false,"counties":["GB-SCT"]}
                ]
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://date.nager.at/"),
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Daybreak.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
