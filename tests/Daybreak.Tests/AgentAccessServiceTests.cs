using Dapper;
using Daybreak.Data;
using Daybreak.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Daybreak.Tests;

[TestClass]
public sealed class AgentAccessServiceTests
{
    private string _directory = null!;
    private DatabaseConnectionFactory _connections = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daybreak-agent-access-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Daybreak"] = $"Data Source={Path.Combine(_directory, "access.db")};Pooling=False",
        }).Build();
        _connections = new DatabaseConnectionFactory(configuration, new TestEnvironment(_directory));
        await new MigrationRunner(_connections, NullLogger<MigrationRunner>.Instance).MigrateAsync();
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
    public async Task DeploymentFlagsGateConfigurationAndEndpoints()
    {
        var access = CreateAccess(enableApi: false, enableMcp: false);

        var configuration = await access.GetAsync();
        var apiAuthentication = await access.AuthenticateAsync(AgentSurface.Api, null);
        var mcpAuthentication = await access.AuthenticateAsync(AgentSurface.Mcp, null);

        Assert.IsFalse(configuration.ApiAvailable);
        Assert.IsFalse(configuration.McpAvailable);
        Assert.AreEqual(AgentAccessOutcome.Unavailable, apiAuthentication.Outcome);
        Assert.AreEqual(AgentAccessOutcome.Unavailable, mcpAuthentication.Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(() => access.GenerateAsync(AgentSurface.Api));
    }

    [TestMethod]
    public async Task ApiKeyIsHashedAndRotationRevokesPreviousKey()
    {
        var access = CreateAccess(enableApi: true, enableMcp: true);
        var first = await access.GenerateAsync(AgentSurface.Api);
        await access.UpdateEnabledAsync(apiEnabled: true, mcpEnabled: false);

        Assert.AreEqual(
            AgentAccessOutcome.Allowed,
            (await access.AuthenticateAsync(AgentSurface.Api, $"Bearer {first.Secret}")).Outcome);
        await using (var connection = await _connections.OpenAsync())
        {
            var stored = await connection.QuerySingleAsync<string>(
                "SELECT SecretHash FROM AgentCredentials WHERE Kind = 'Api'");
            Assert.AreNotEqual(first.Secret, stored);
            Assert.IsFalse(stored.Contains(first.Secret, StringComparison.Ordinal));
        }

        var second = await access.GenerateAsync(AgentSurface.Api);

        Assert.AreEqual(
            AgentAccessOutcome.Unauthorized,
            (await access.AuthenticateAsync(AgentSurface.Api, $"Bearer {first.Secret}")).Outcome);
        Assert.AreEqual(
            AgentAccessOutcome.Allowed,
            (await access.AuthenticateAsync(AgentSurface.Api, $"Bearer {second.Secret}")).Outcome);
    }

    [TestMethod]
    public async Task McpRequiresApiButAllowsAnExplicitlyMissingMcpKey()
    {
        var access = CreateAccess(enableApi: true, enableMcp: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            access.UpdateEnabledAsync(apiEnabled: false, mcpEnabled: true));

        await access.GenerateAsync(AgentSurface.Api);
        await access.UpdateEnabledAsync(apiEnabled: true, mcpEnabled: true);
        Assert.AreEqual(
            AgentAccessOutcome.Allowed,
            (await access.AuthenticateAsync(AgentSurface.Mcp, null)).Outcome);

        var mcpKey = await access.GenerateAsync(AgentSurface.Mcp);
        Assert.AreEqual(
            AgentAccessOutcome.Unauthorized,
            (await access.AuthenticateAsync(AgentSurface.Mcp, null)).Outcome);
        Assert.AreEqual(
            AgentAccessOutcome.Allowed,
            (await access.AuthenticateAsync(AgentSurface.Mcp, $"Bearer {mcpKey.Secret}")).Outcome);

        await access.RemoveMcpKeyAsync();
        Assert.AreEqual(
            AgentAccessOutcome.Allowed,
            (await access.AuthenticateAsync(AgentSurface.Mcp, null)).Outcome);
    }

    private AgentAccessService CreateAccess(bool enableApi, bool enableMcp) => new(
        _connections,
        Options.Create(new AgentFeatureOptions { EnableApi = enableApi, EnableMcp = enableMcp }),
        TimeProvider.System);

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Daybreak.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
