using Dapper;
using Daybreak.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daybreak.Tests;

[TestClass]
public sealed class MigrationRunnerTests
{
    private string _directory = null!;
    private DatabaseConnectionFactory _connections = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daybreak-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Daybreak"] = $"Data Source={Path.Combine(_directory, "migration.db")};Pooling=False",
        }).Build();
        _connections = new DatabaseConnectionFactory(configuration, new TestEnvironment(_directory));
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
    public async Task ApplyingCurrentMigrationTwiceIsIdempotent()
    {
        var runner = new MigrationRunner(_connections, NullLogger<MigrationRunner>.Instance);

        await runner.MigrateAsync();
        await runner.MigrateAsync();

        await using var connection = await _connections.OpenAsync();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SchemaMigrations"));
        Assert.AreEqual(SchemaManifest.CurrentVersion, await connection.ExecuteScalarAsync<int>("SELECT MAX(Version) FROM SchemaMigrations"));
    }

    [TestMethod]
    public async Task NewerDatabaseSchemaIsRejected()
    {
        var runner = new MigrationRunner(_connections, NullLogger<MigrationRunner>.Instance);
        await runner.MigrateAsync();
        await using (var connection = await _connections.OpenAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO SchemaMigrations (Version, Name, AppliedAtUtc) VALUES (999, 'Future', @Now)",
                new { Now = DateTimeOffset.UtcNow.ToString("O") });
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.MigrateAsync());

        StringAssert.Contains(exception.Message, "newer than this Daybreak build supports");
    }

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Daybreak.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
