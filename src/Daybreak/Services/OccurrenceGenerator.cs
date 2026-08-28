using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class OccurrenceGenerator(
    DatabaseConnectionFactory connections,
    IHolidayProvider holidays,
    ScheduleProjector projector,
    BoardChangeNotifier changes,
    TimeProvider clock)
{
    public OccurrenceGenerator(
        DatabaseConnectionFactory connections,
        IHolidayProvider holidays,
        BoardChangeNotifier changes,
        TimeProvider clock)
        : this(connections, holidays, new ScheduleProjector(), changes, clock)
    {
    }

    public async Task EnsureRollingHorizonAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var settings = await connection.QuerySingleAsync<HouseholdSettings>("SELECT * FROM HouseholdSettings WHERE Id = 1");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var today = LocalTimeResolver.Today(clock, timeZone);
        await EnsureAsync(today.AddDays(-14), today.AddDays(90), cancellationToken);
    }

    public async Task EnsureAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
    {
        if (through < from)
        {
            throw new ArgumentOutOfRangeException(nameof(through));
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var settings = await connection.QuerySingleAsync<HouseholdSettings>("SELECT * FROM HouseholdSettings WHERE Id = 1");
        var activities = (await connection.QueryAsync<Activity>("""
            SELECT * FROM Activities
            WHERE IsPaused = 0 AND ArchivedAtUtc IS NULL
              AND StartDate <= @Through
              AND (EndDate IS NULL OR EndDate >= @From)
            """, new { From = Format(from), Through = Format(through) })).AsList();
        var oneOffTasks = (await connection.QueryAsync<OneOffTask>("""
            SELECT * FROM OneOffTasks task
            WHERE task.ScheduledDate BETWEEN @From AND @Through
               OR (task.IsPermanent = 1 AND task.ScheduledDate <= @Through AND NOT EXISTS (
                    SELECT 1 FROM Occurrences occurrence
                    WHERE occurrence.OneOffTaskId = task.Id AND occurrence.State != @Pending))
            """, new { From = Format(from), Through = Format(through), Pending = OccurrenceState.Pending })).AsList();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var createdAt = clock.GetUtcNow().ToString("O");
        var holidayDates = await LoadHolidaysAsync(settings, activities, from, through, cancellationToken);
        var changedRows = 0;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var activity in activities)
        {
            for (var date = from; date <= through; date = date.AddDays(1))
            {
                if (!RecurrenceCalculator.OccursOn(activity, date))
                {
                    continue;
                }

                var projection = projector.ProjectActivity(activity, [date], holidayDates, settings.DefaultBleedMinutes, timeZone)[0];
                if (projection.EffectiveDate is null)
                {
                    continue;
                }

                changedRows += await InsertActivityOccurrenceAsync(
                    connection,
                    transaction,
                    activity,
                    projection,
                    createdAt);
            }
        }

        foreach (var task in oneOffTasks)
        {
            var projection = projector.ProjectOneOff(task, settings.DefaultBleedMinutes, timeZone);
            changedRows += await InsertOneOffOccurrenceAsync(
                connection,
                transaction,
                task,
                projection,
                createdAt);
        }

        long? revision = null;
        if (changedRows > 0)
        {
            revision = await connection.ExecuteScalarAsync<long>(
                "UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1 RETURNING BoardRevision",
                transaction: transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        if (revision is not null)
        {
            await changes.NotifyAsync(revision.Value);
        }
    }

    private async Task<IReadOnlySet<DateOnly>> LoadHolidaysAsync(
        HouseholdSettings settings,
        IReadOnlyCollection<Activity> activities,
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.HolidayCountryCode) || activities.All(item => item.HolidayPolicy == HolidayPolicy.Keep))
        {
            return new HashSet<DateOnly>();
        }

        var dates = new HashSet<DateOnly>();
        for (var year = from.AddDays(-14).Year; year <= through.AddDays(14).Year; year++)
        {
            dates.UnionWith(await holidays.GetHolidayDatesAsync(
                year,
                settings.HolidayCountryCode,
                settings.HolidaySubdivisionCode,
                cancellationToken));
        }

        return dates;
    }

    public static DateOnly? AdjustForHoliday(HolidayPolicy policy, DateOnly nominalDate, IReadOnlySet<DateOnly> holidays) =>
        ScheduleProjector.AdjustForHoliday(policy, null, nominalDate, holidays);

    private static async Task<int> InsertActivityOccurrenceAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Activity activity,
        ScheduleProjectionItem projection,
        string createdAt)
    {
        return await connection.ExecuteAsync(InsertActivitySql, new
        {
            Id = Guid.NewGuid().ToString("N"),
            ActivityId = activity.Id,
            activity.Title,
            activity.Notes,
            ScheduleLabel = RecurrenceDescription.ForActivity(activity),
            NominalDate = Format(projection.NominalDate),
            EffectiveDate = Format(projection.EffectiveDate!.Value),
            AdjustmentDescriptionSnapshot = NullIfBlank(projection.AdjustmentExplanation),
            VisibleFromUtc = projection.VisibleFrom!.Value.ToString("O"),
            DeadlineUtc = projection.Deadline?.ToString("O"),
            ActionWindowEndUtc = projection.ActionWindowEnd!.Value.ToString("O"),
            activity.UrgencyMode,
            activity.WarningMinutes,
            CreatedAtUtc = createdAt,
        }, transaction);
    }

    private static async Task<int> InsertOneOffOccurrenceAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        OneOffTask task,
        ScheduleProjectionItem projection,
        string createdAt)
    {
        return await connection.ExecuteAsync("""
            INSERT INTO Occurrences (
                Id, OneOffTaskId, TitleSnapshot, NotesSnapshot, ScheduleLabelSnapshot, NominalDate, EffectiveDate, AdjustmentDescriptionSnapshot,
                VisibleFromUtc, DeadlineUtc, ActionWindowEndUtc, UrgencyMode, WarningMinutes, State, Version, IsPermanent, CreatedAtUtc)
            VALUES (
                @Id, @OneOffTaskId, @Title, @Notes, @ScheduleLabel, @NominalDate, @EffectiveDate, NULL,
                @VisibleFromUtc, @DeadlineUtc, @ActionWindowEndUtc, @UrgencyMode, @WarningMinutes, 0, 0, @IsPermanent, @CreatedAtUtc)
            ON CONFLICT(OneOffTaskId) WHERE OneOffTaskId IS NOT NULL DO UPDATE SET
                TitleSnapshot = excluded.TitleSnapshot,
                NotesSnapshot = excluded.NotesSnapshot,
                ScheduleLabelSnapshot = excluded.ScheduleLabelSnapshot,
                NominalDate = excluded.NominalDate,
                EffectiveDate = excluded.EffectiveDate,
                VisibleFromUtc = excluded.VisibleFromUtc,
                DeadlineUtc = excluded.DeadlineUtc,
                ActionWindowEndUtc = excluded.ActionWindowEndUtc,
                UrgencyMode = excluded.UrgencyMode,
                WarningMinutes = excluded.WarningMinutes,
                IsPermanent = excluded.IsPermanent
            WHERE Occurrences.State = 0 AND (
                Occurrences.TitleSnapshot IS NOT excluded.TitleSnapshot OR
                Occurrences.NotesSnapshot IS NOT excluded.NotesSnapshot OR
                Occurrences.ScheduleLabelSnapshot IS NOT excluded.ScheduleLabelSnapshot OR
                Occurrences.NominalDate IS NOT excluded.NominalDate OR
                Occurrences.EffectiveDate IS NOT excluded.EffectiveDate OR
                Occurrences.VisibleFromUtc IS NOT excluded.VisibleFromUtc OR
                Occurrences.DeadlineUtc IS NOT excluded.DeadlineUtc OR
                Occurrences.ActionWindowEndUtc IS NOT excluded.ActionWindowEndUtc OR
                Occurrences.UrgencyMode IS NOT excluded.UrgencyMode OR
                Occurrences.WarningMinutes IS NOT excluded.WarningMinutes OR
                Occurrences.IsPermanent IS NOT excluded.IsPermanent);
            """, new
        {
            Id = Guid.NewGuid().ToString("N"),
            OneOffTaskId = task.Id,
            task.Title,
            task.Notes,
            ScheduleLabel = task.IsPermanent ? RecurrenceDescription.PermanentOneOff : RecurrenceDescription.OneOff,
            NominalDate = Format(projection.NominalDate),
            EffectiveDate = Format(projection.EffectiveDate!.Value),
            VisibleFromUtc = projection.VisibleFrom!.Value.ToString("O"),
            DeadlineUtc = projection.Deadline?.ToString("O"),
            ActionWindowEndUtc = projection.ActionWindowEnd!.Value.ToString("O"),
            task.UrgencyMode,
            task.WarningMinutes,
            task.IsPermanent,
            CreatedAtUtc = createdAt,
        }, transaction);
    }

    private const string InsertActivitySql = """
        INSERT INTO Occurrences (
            Id, ActivityId, TitleSnapshot, NotesSnapshot, ScheduleLabelSnapshot, NominalDate, EffectiveDate, AdjustmentDescriptionSnapshot,
            VisibleFromUtc, DeadlineUtc, ActionWindowEndUtc, UrgencyMode, WarningMinutes, State, Version, CreatedAtUtc)
        VALUES (
            @Id, @ActivityId, @Title, @Notes, @ScheduleLabel, @NominalDate, @EffectiveDate, @AdjustmentDescriptionSnapshot,
            @VisibleFromUtc, @DeadlineUtc, @ActionWindowEndUtc, @UrgencyMode, @WarningMinutes, 0, 0, @CreatedAtUtc)
        ON CONFLICT(ActivityId, NominalDate) WHERE ActivityId IS NOT NULL DO UPDATE SET
            TitleSnapshot = excluded.TitleSnapshot,
            NotesSnapshot = excluded.NotesSnapshot,
            ScheduleLabelSnapshot = excluded.ScheduleLabelSnapshot,
            EffectiveDate = excluded.EffectiveDate,
            AdjustmentDescriptionSnapshot = excluded.AdjustmentDescriptionSnapshot,
            VisibleFromUtc = excluded.VisibleFromUtc,
            DeadlineUtc = excluded.DeadlineUtc,
            ActionWindowEndUtc = excluded.ActionWindowEndUtc,
            UrgencyMode = excluded.UrgencyMode,
            WarningMinutes = excluded.WarningMinutes
        WHERE Occurrences.State = 0 AND (
            Occurrences.TitleSnapshot IS NOT excluded.TitleSnapshot OR
            Occurrences.NotesSnapshot IS NOT excluded.NotesSnapshot OR
            Occurrences.ScheduleLabelSnapshot IS NOT excluded.ScheduleLabelSnapshot OR
            Occurrences.EffectiveDate IS NOT excluded.EffectiveDate OR
            Occurrences.AdjustmentDescriptionSnapshot IS NOT excluded.AdjustmentDescriptionSnapshot OR
            Occurrences.VisibleFromUtc IS NOT excluded.VisibleFromUtc OR
            Occurrences.DeadlineUtc IS NOT excluded.DeadlineUtc OR
            Occurrences.ActionWindowEndUtc IS NOT excluded.ActionWindowEndUtc OR
            Occurrences.UrgencyMode IS NOT excluded.UrgencyMode OR
            Occurrences.WarningMinutes IS NOT excluded.WarningMinutes);
        """;

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd");
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
