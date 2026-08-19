using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class OneOffTaskService(
    DatabaseConnectionFactory connections,
    BoardChangeNotifier changes,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<OneOffTask>> ListAsync()
    {
        await using var connection = await connections.OpenAsync();
        return (await connection.QueryAsync<OneOffTask>("""
            SELECT task.*
            FROM OneOffTasks task
            WHERE NOT EXISTS (
                SELECT 1 FROM Occurrences occurrence
                WHERE occurrence.OneOffTaskId = task.Id AND occurrence.State != @Pending)
            ORDER BY task.ScheduledDate, task.DeadlineMinutes, task.Title;
            """, new { Pending = OccurrenceState.Pending })).AsList();
    }

    public async Task<string> SaveAsync(OneOffTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Title) || !DateOnly.TryParse(task.ScheduledDate, out _))
        {
            throw new ArgumentException("A title and valid scheduled date are required.", nameof(task));
        }

        var now = clock.GetUtcNow().ToString("O");
        var id = string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
        await using var connection = await connections.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM Occurrences WHERE OneOffTaskId = @id AND State = @pending",
            new { id, pending = OccurrenceState.Pending }, transaction);
        await connection.ExecuteAsync("""
            INSERT INTO OneOffTasks (
                Id, Title, Notes, ScheduledDate, DeadlineMinutes, UrgencyMode, WarningMinutes,
                BleedOverrideMinutes, SourceKind, SourceReference, CreatedAtUtc, UpdatedAtUtc)
            VALUES (
                @Id, @Title, @Notes, @ScheduledDate, @DeadlineMinutes, @UrgencyMode, @WarningMinutes,
                @BleedOverrideMinutes, @SourceKind, @SourceReference, @CreatedAtUtc, @UpdatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Notes = excluded.Notes,
                ScheduledDate = excluded.ScheduledDate,
                DeadlineMinutes = excluded.DeadlineMinutes,
                UrgencyMode = excluded.UrgencyMode,
                WarningMinutes = excluded.WarningMinutes,
                BleedOverrideMinutes = excluded.BleedOverrideMinutes,
                SourceKind = excluded.SourceKind,
                SourceReference = excluded.SourceReference,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """, new
        {
            Id = id,
            Title = task.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(task.Notes) ? null : task.Notes.Trim(),
            task.ScheduledDate,
            task.DeadlineMinutes,
            task.UrgencyMode,
            task.WarningMinutes,
            task.BleedOverrideMinutes,
            task.SourceKind,
            task.SourceReference,
            CreatedAtUtc = string.IsNullOrWhiteSpace(task.CreatedAtUtc) ? now : task.CreatedAtUtc,
            UpdatedAtUtc = now,
        }, transaction);
        var revision = await connection.ExecuteScalarAsync<long>(
            "UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1 RETURNING BoardRevision",
            transaction: transaction);
        await transaction.CommitAsync();
        await changes.NotifyAsync(revision);
        return id;
    }

    public async Task<bool> DeletePendingAsync(string id)
    {
        await using var connection = await connections.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM Occurrences WHERE OneOffTaskId = @id AND State = @pending",
            new { id, pending = OccurrenceState.Pending }, transaction);
        var removed = await connection.ExecuteAsync("""
            DELETE FROM OneOffTasks
            WHERE Id = @id
              AND NOT EXISTS (SELECT 1 FROM Occurrences WHERE OneOffTaskId = @id)
            """, new { id }, transaction);
        if (removed == 0)
        {
            await transaction.RollbackAsync();
            return false;
        }

        var revision = await connection.ExecuteScalarAsync<long>(
            "UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1 RETURNING BoardRevision",
            transaction: transaction);
        await transaction.CommitAsync();
        await changes.NotifyAsync(revision);
        return true;
    }
}
