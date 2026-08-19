using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class ActivityService(
    DatabaseConnectionFactory connections,
    BoardChangeNotifier changes,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<Activity>> ListAsync(bool includeArchived = false)
    {
        await using var connection = await connections.OpenAsync();
        var sql = "SELECT * FROM Activities" + (includeArchived ? string.Empty : " WHERE ArchivedAtUtc IS NULL") + " ORDER BY Title";
        return (await connection.QueryAsync<Activity>(sql)).AsList();
    }

    public async Task<Activity?> GetAsync(string id)
    {
        await using var connection = await connections.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Activity>("SELECT * FROM Activities WHERE Id = @id", new { id });
    }

    public async Task<string> SaveAsync(Activity activity)
    {
        Validate(activity);
        var now = clock.GetUtcNow().ToString("O");
        var id = string.IsNullOrWhiteSpace(activity.Id) ? Guid.NewGuid().ToString("N") : activity.Id;
        await using var connection = await connections.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM Occurrences WHERE ActivityId = @Id AND State = @Pending",
            new { Id = id, Pending = OccurrenceState.Pending }, transaction);
        await connection.ExecuteAsync("""
            INSERT INTO Activities (
                Id, Title, Notes, RecurrenceKind, Interval, DaysOfWeekMask, DayOfMonth, Ordinal, Weekday,
                StartDate, EndDate, DeadlineMinutes, UrgencyMode, WarningMinutes, BleedOverrideMinutes,
                HolidayPolicy, IsPaused, ArchivedAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES (
                @Id, @Title, @Notes, @RecurrenceKind, @Interval, @DaysOfWeekMask, @DayOfMonth, @Ordinal, @Weekday,
                @StartDate, @EndDate, @DeadlineMinutes, @UrgencyMode, @WarningMinutes, @BleedOverrideMinutes,
                @HolidayPolicy, @IsPaused, @ArchivedAtUtc, @CreatedAtUtc, @UpdatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Notes = excluded.Notes,
                RecurrenceKind = excluded.RecurrenceKind,
                Interval = excluded.Interval,
                DaysOfWeekMask = excluded.DaysOfWeekMask,
                DayOfMonth = excluded.DayOfMonth,
                Ordinal = excluded.Ordinal,
                Weekday = excluded.Weekday,
                StartDate = excluded.StartDate,
                EndDate = excluded.EndDate,
                DeadlineMinutes = excluded.DeadlineMinutes,
                UrgencyMode = excluded.UrgencyMode,
                WarningMinutes = excluded.WarningMinutes,
                BleedOverrideMinutes = excluded.BleedOverrideMinutes,
                HolidayPolicy = excluded.HolidayPolicy,
                IsPaused = excluded.IsPaused,
                ArchivedAtUtc = excluded.ArchivedAtUtc,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """, new
        {
            Id = id,
            Title = activity.Title.Trim(),
            Notes = NullIfBlank(activity.Notes),
            activity.RecurrenceKind,
            activity.Interval,
            activity.DaysOfWeekMask,
            activity.DayOfMonth,
            activity.Ordinal,
            activity.Weekday,
            activity.StartDate,
            activity.EndDate,
            activity.DeadlineMinutes,
            activity.UrgencyMode,
            activity.WarningMinutes,
            activity.BleedOverrideMinutes,
            activity.HolidayPolicy,
            activity.IsPaused,
            activity.ArchivedAtUtc,
            CreatedAtUtc = string.IsNullOrWhiteSpace(activity.CreatedAtUtc) ? now : activity.CreatedAtUtc,
            UpdatedAtUtc = now,
        }, transaction);
        var revision = await IncrementRevisionAsync(connection, transaction);
        await transaction.CommitAsync();
        await changes.NotifyAsync(revision);
        return id;
    }

    public async Task ArchiveAsync(string id)
    {
        await using var connection = await connections.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM Occurrences WHERE ActivityId = @id AND State = @pending",
            new { id, pending = OccurrenceState.Pending }, transaction);
        await connection.ExecuteAsync(
            "UPDATE Activities SET ArchivedAtUtc = @now, UpdatedAtUtc = @now WHERE Id = @id",
            new { id, now = clock.GetUtcNow().ToString("O") }, transaction);
        var revision = await IncrementRevisionAsync(connection, transaction);
        await transaction.CommitAsync();
        await changes.NotifyAsync(revision);
    }

    public async Task RestoreAsync(string id)
    {
        await using var connection = await connections.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var restored = await connection.ExecuteAsync("""
            UPDATE Activities
            SET ArchivedAtUtc = NULL, UpdatedAtUtc = @Now
            WHERE Id = @Id AND ArchivedAtUtc IS NOT NULL;
            """, new { Id = id, Now = clock.GetUtcNow().ToString("O") }, transaction);
        if (restored == 0)
        {
            await transaction.RollbackAsync();
            return;
        }

        var revision = await IncrementRevisionAsync(connection, transaction);
        await transaction.CommitAsync();
        await changes.NotifyAsync(revision);
    }

    private static void Validate(Activity activity)
    {
        if (string.IsNullOrWhiteSpace(activity.Title))
        {
            throw new ArgumentException("Title is required.", nameof(activity));
        }

        if (!DateOnly.TryParse(activity.StartDate, out var start) ||
            (activity.EndDate is not null && (!DateOnly.TryParse(activity.EndDate, out var end) || end < start)))
        {
            throw new ArgumentException("The schedule dates are invalid.", nameof(activity));
        }

        if (activity.Interval is < 1 or > 365 || activity.WarningMinutes is < 0 or > 1440)
        {
            throw new ArgumentException("The interval or warning window is invalid.", nameof(activity));
        }

        if (activity.RecurrenceKind is RecurrenceKind.SelectedWeekdays or RecurrenceKind.EveryNWeeks && activity.DaysOfWeekMask == 0)
        {
            throw new ArgumentException("Select at least one weekday.", nameof(activity));
        }

        if (activity.RecurrenceKind == RecurrenceKind.MonthlyDate && activity.DayOfMonth is not (>= 1 and <= 31))
        {
            throw new ArgumentException("Choose a day of the month from 1 to 31.", nameof(activity));
        }

        if (activity.RecurrenceKind == RecurrenceKind.MonthlyOrdinalWeekday &&
            (activity.Ordinal is not (>= 1 and <= 5) || activity.Weekday is not (>= 0 and <= 6)))
        {
            throw new ArgumentException("Choose a valid monthly weekday occurrence.", nameof(activity));
        }
    }

    private static async Task<long> IncrementRevisionAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction) =>
        await connection.ExecuteScalarAsync<long>(
            "UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1 RETURNING BoardRevision",
            transaction: transaction);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
