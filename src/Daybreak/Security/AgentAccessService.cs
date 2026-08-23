using System.Security.Cryptography;
using System.Text;
using Dapper;
using Daybreak.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Daybreak.Security;

public enum AgentSurface
{
    Api,
    Mcp,
}

public enum AgentAccessOutcome
{
    Allowed,
    Unavailable,
    Unauthorized,
}

public sealed record AgentCredentialInfo(string Suffix, DateTimeOffset CreatedAtUtc);

public sealed record AgentAccessConfiguration(
    bool ApiAvailable,
    bool McpAvailable,
    bool ApiEnabled,
    bool McpEnabled,
    AgentCredentialInfo? ApiCredential,
    AgentCredentialInfo? McpCredential);

public sealed record GeneratedAgentCredential(AgentSurface Surface, string Secret, string Suffix, DateTimeOffset CreatedAtUtc);

public sealed record AgentAuthenticationResult(AgentAccessOutcome Outcome, string? CredentialSuffix = null);

public sealed class AgentAccessService(
    DatabaseConnectionFactory connections,
    IOptions<AgentFeatureOptions> options,
    TimeProvider clock)
{
    private readonly AgentFeatureOptions _features = options.Value;

    public async Task<AgentAccessConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var settings = await connection.QuerySingleAsync<AgentSettingsRow>(
            "SELECT ApiEnabled, McpEnabled FROM HouseholdSettings WHERE Id = 1");
        var credentials = (await connection.QueryAsync<AgentCredentialRow>(
            "SELECT Kind, SecretHash, Suffix, CreatedAtUtc FROM AgentCredentials")).ToDictionary(
                item => item.Kind,
                StringComparer.Ordinal);

        return new AgentAccessConfiguration(
            _features.EnableApi,
            _features.EnableMcp,
            settings.ApiEnabled,
            settings.McpEnabled,
            CredentialInfo(credentials.GetValueOrDefault(nameof(AgentSurface.Api))),
            CredentialInfo(credentials.GetValueOrDefault(nameof(AgentSurface.Mcp))));
    }

    public async Task UpdateEnabledAsync(bool apiEnabled, bool mcpEnabled, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(cancellationToken);
        if (apiEnabled && !_features.EnableApi && !current.ApiEnabled)
        {
            throw new InvalidOperationException("API access is not enabled by the deployment configuration.");
        }

        if (mcpEnabled && !_features.EnableMcp && !current.McpEnabled)
        {
            throw new InvalidOperationException("MCP access is not enabled by the deployment configuration.");
        }

        if (apiEnabled && current.ApiCredential is null)
        {
            throw new InvalidOperationException("Generate an API key before enabling API access.");
        }

        if (mcpEnabled && (!apiEnabled || current.ApiCredential is null))
        {
            throw new InvalidOperationException("MCP requires enabled API access and an active API key.");
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("""
            UPDATE HouseholdSettings
            SET ApiEnabled = @ApiEnabled,
                McpEnabled = @McpEnabled
            WHERE Id = 1;
            """, new { ApiEnabled = apiEnabled, McpEnabled = mcpEnabled });
    }

    public async Task<GeneratedAgentCredential> GenerateAsync(
        AgentSurface surface,
        CancellationToken cancellationToken = default)
    {
        if (surface == AgentSurface.Api && !_features.EnableApi)
        {
            throw new InvalidOperationException("API access is not enabled by the deployment configuration.");
        }

        if (surface == AgentSurface.Mcp)
        {
            if (!_features.EnableMcp)
            {
                throw new InvalidOperationException("MCP access is not enabled by the deployment configuration.");
            }

            var current = await GetAsync(cancellationToken);
            if (current.ApiCredential is null)
            {
                throw new InvalidOperationException("Generate an API key before generating an MCP key.");
            }
        }

        var now = clock.GetUtcNow();
        var prefix = surface == AgentSurface.Api ? "daybreak_api_" : "daybreak_mcp_";
        var secret = prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(secret);
        var suffix = secret[^8..];

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("""
            INSERT INTO AgentCredentials (Kind, SecretHash, Suffix, CreatedAtUtc)
            VALUES (@Kind, @SecretHash, @Suffix, @CreatedAtUtc)
            ON CONFLICT(Kind) DO UPDATE SET
                SecretHash = excluded.SecretHash,
                Suffix = excluded.Suffix,
                CreatedAtUtc = excluded.CreatedAtUtc;
            """, new
        {
            Kind = surface.ToString(),
            SecretHash = hash,
            Suffix = suffix,
            CreatedAtUtc = now.ToString("O"),
        });

        return new GeneratedAgentCredential(surface, secret, suffix, now);
    }

    public async Task RemoveMcpKeyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "DELETE FROM AgentCredentials WHERE Kind = @Kind",
            new { Kind = nameof(AgentSurface.Mcp) });
    }

    public async Task<AgentAuthenticationResult> AuthenticateAsync(
        AgentSurface surface,
        string? authorizationHeader,
        CancellationToken cancellationToken = default)
    {
        var configuration = await GetAsync(cancellationToken);
        var available = surface == AgentSurface.Api
            ? configuration.ApiAvailable && configuration.ApiEnabled && configuration.ApiCredential is not null
            : configuration.McpAvailable && configuration.McpEnabled &&
              configuration.ApiAvailable && configuration.ApiEnabled && configuration.ApiCredential is not null;
        if (!available)
        {
            return new AgentAuthenticationResult(AgentAccessOutcome.Unavailable);
        }

        var credential = surface == AgentSurface.Api ? configuration.ApiCredential : configuration.McpCredential;
        if (surface == AgentSurface.Mcp && credential is null)
        {
            return new AgentAuthenticationResult(AgentAccessOutcome.Allowed);
        }

        var token = BearerToken(authorizationHeader);
        if (token is null)
        {
            return new AgentAuthenticationResult(AgentAccessOutcome.Unauthorized);
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var expectedHash = await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT SecretHash FROM AgentCredentials WHERE Kind = @Kind",
            new { Kind = surface.ToString() });
        if (expectedHash is null || !FixedTimeEquals(expectedHash, Hash(token)))
        {
            return new AgentAuthenticationResult(AgentAccessOutcome.Unauthorized);
        }

        return new AgentAuthenticationResult(AgentAccessOutcome.Allowed, credential?.Suffix);
    }

    public async Task RecordAccessAsync(
        AgentSurface surface,
        string? credentialSuffix,
        string method,
        string path,
        int statusCode,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("""
            INSERT INTO AgentAccessEvents (
                Surface, CredentialSuffix, Method, Path, StatusCode, CorrelationId, OccurredAtUtc)
            VALUES (
                @Surface, @CredentialSuffix, @Method, @Path, @StatusCode, @CorrelationId, @OccurredAtUtc);
            """, new
        {
            Surface = surface.ToString(),
            CredentialSuffix = credentialSuffix,
            Method = method,
            Path = path,
            StatusCode = statusCode,
            CorrelationId = correlationId,
            OccurredAtUtc = clock.GetUtcNow().ToString("O"),
        });
    }

    private static AgentCredentialInfo? CredentialInfo(AgentCredentialRow? row) => row is null
        ? null
        : new AgentCredentialInfo(row.Suffix, DateTimeOffset.Parse(row.CreatedAtUtc));

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Convert.FromHexString(expected);
        var actualBytes = Convert.FromHexString(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string? BearerToken(string? header)
    {
        const string prefix = "Bearer ";
        return header is not null && header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private sealed record AgentSettingsRow(bool ApiEnabled, bool McpEnabled)
    {
        public AgentSettingsRow() : this(false, false) { }
    }

    private sealed record AgentCredentialRow(string Kind, string SecretHash, string Suffix, string CreatedAtUtc)
    {
        public AgentCredentialRow() : this(string.Empty, string.Empty, string.Empty, string.Empty) { }
    }
}
