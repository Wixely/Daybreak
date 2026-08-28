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
        Assert.AreEqual(SchemaManifest.CurrentVersion, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SchemaMigrations"));
        Assert.AreEqual(SchemaManifest.CurrentVersion, await connection.ExecuteScalarAsync<int>("SELECT MAX(Version) FROM SchemaMigrations"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Occurrences') WHERE name = 'ScheduleLabelSnapshot'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Activities') WHERE name = 'ShowAheadHours'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('OneOffTasks') WHERE name = 'ShowAheadHours'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Occurrences') WHERE name = 'VisibleFromUtc'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Activities') WHERE name = 'HolidayTargetWeekday'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Occurrences') WHERE name = 'AdjustmentDescriptionSnapshot'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('OneOffTasks') WHERE name = 'IsPermanent'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Occurrences') WHERE name = 'IsPermanent'"));
    }

    [TestMethod]
    public async Task PreviousSchemaUpgradesWithAgentAccessDisabled()
    {
        await using (var connection = await _connections.OpenAsync())
        {
            await connection.ExecuteAsync("""
                CREATE TABLE SchemaMigrations (
                    Version INTEGER NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    AppliedAtUtc TEXT NOT NULL
                );
                """);
            foreach (var migration in SchemaManifest.Migrations.Where(item => item.Version < SchemaManifest.CurrentVersion))
            {
                await connection.ExecuteAsync(migration.Sql);
                await connection.ExecuteAsync(
                    "INSERT INTO SchemaMigrations (Version, Name, AppliedAtUtc) VALUES (@Version, @Name, @AppliedAtUtc)",
                    new { migration.Version, migration.Name, AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O") });
            }
        }

        await new MigrationRunner(_connections, NullLogger<MigrationRunner>.Instance).MigrateAsync();

        await using var upgraded = await _connections.OpenAsync();
        Assert.AreEqual("Europe/London", await upgraded.ExecuteScalarAsync<string>(
            "SELECT TimeZoneId FROM HouseholdSettings WHERE Id = 1"));
        Assert.AreEqual(0, await upgraded.ExecuteScalarAsync<int>(
            "SELECT ApiEnabled FROM HouseholdSettings WHERE Id = 1"));
        Assert.AreEqual(0, await upgraded.ExecuteScalarAsync<int>(
            "SELECT McpEnabled FROM HouseholdSettings WHERE Id = 1"));
        Assert.AreEqual(1, await upgraded.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AgentCredentials'"));
        Assert.AreEqual(1, await upgraded.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AgentAccessEvents'"));
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
