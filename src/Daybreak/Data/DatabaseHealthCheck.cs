using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Daybreak.Data;

public sealed class DatabaseHealthCheck(DatabaseConnectionFactory connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken);
            var result = await connection.ExecuteScalarAsync<int>("SELECT 1");
            return result == 1
                ? HealthCheckResult.Healthy("SQLite is available.")
                : HealthCheckResult.Unhealthy("SQLite returned an unexpected health result.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("SQLite is unavailable.", exception);
        }
    }
}
