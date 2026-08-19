using Microsoft.Data.Sqlite;

namespace Daybreak.Data;

public sealed class DatabaseConnectionFactory
{
    private readonly string _connectionString;

    public DatabaseConnectionFactory(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = configuration.GetConnectionString("Daybreak")
            ?? throw new InvalidOperationException("ConnectionStrings:Daybreak is required.");

        var builder = new SqliteConnectionStringBuilder(_connectionString);
        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(environment.ContentRootPath, builder.DataSource));
            _connectionString = builder.ToString();
        }

        var directory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }
}
