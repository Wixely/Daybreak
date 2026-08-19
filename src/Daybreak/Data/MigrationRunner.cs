using Dapper;

namespace Daybreak.Data;

public sealed class MigrationRunner(DatabaseConnectionFactory connections, ILogger<MigrationRunner> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;");
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );
            """);

        var applied = (await connection.QueryAsync<int>("SELECT Version FROM SchemaMigrations")).ToHashSet();
        var futureVersion = applied.Where(version => version > SchemaManifest.CurrentVersion).DefaultIfEmpty().Max();
        if (futureVersion > SchemaManifest.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"The database schema version {futureVersion} is newer than this Daybreak build supports ({SchemaManifest.CurrentVersion}).");
        }

        var highestApplied = applied.DefaultIfEmpty().Max();
        for (var version = 1; version <= highestApplied; version++)
        {
            if (!applied.Contains(version))
            {
                throw new InvalidOperationException($"The database migration history is missing version {version}.");
            }
        }

        foreach (var migration in SchemaManifest.Migrations.Where(item => !applied.Contains(item.Version)))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await connection.ExecuteAsync(migration.Sql, transaction: transaction);
                await connection.ExecuteAsync(
                    "INSERT INTO SchemaMigrations (Version, Name, AppliedAtUtc) VALUES (@Version, @Name, @AppliedAtUtc)",
                    new { migration.Version, migration.Name, AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O") },
                    transaction);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Applied database migration {Version}: {Name}", migration.Version, migration.Name);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
