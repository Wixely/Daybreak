using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class OccurrenceGenerator(
    DatabaseConnectionFactory connections,
    IHolidayProvider holidays,
    BoardChangeNotifier changes,
    TimeProvider clock)
{
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
            SELECT * FROM OneOffTasks WHERE ScheduledDate BETWEEN @From AND @Through
            """, new { From = Format(from), Through = Format(through) })).AsList();
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

                var effectiveDate = AdjustForHoliday(activity.HolidayPolicy, date, holidayDates);
                if (effectiveDate is null)
                {
                    continue;
                }

                changedRows += await InsertActivityOccurrenceAsync(
                    connection,
                    transaction,
                    activity,
                    date,
                    effectiveDate.Value,
                    settings.DefaultBleedMinutes,
                    timeZone,
                    createdAt);
            }
        }

        foreach (var task in oneOffTasks)
        {
            var date = DateOnly.Parse(task.ScheduledDate);
            changedRows += await InsertOneOffOccurrenceAsync(
                connection,
                transaction,
                task,
                date,
                settings.DefaultBleedMinutes,
                timeZone,
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

    public static DateOnly? AdjustForHoliday(HolidayPolicy policy, DateOnly nominalDate, IReadOnlySet<DateOnly> holidays)
    {
        if (policy == HolidayPolicy.Keep || !holidays.Contains(nominalDate))
        {
            return nominalDate;
        }

        if (policy == HolidayPolicy.Suppress)
        {
            return null;
        }

        var direction = policy == HolidayPolicy.MoveEarlier ? -1 : 1;
        var candidate = nominalDate;
        for (var attempt = 0; attempt < 14; attempt++)
        {
            candidate = candidate.AddDays(direction);
            if (!holidays.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Could not move the occurrence on {nominalDate:yyyy-MM-dd} away from a holiday within 14 days.");
    }

    private static async Task<int> InsertActivityOccurrenceAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Activity activity,
        DateOnly nominalDate,
        DateOnly effectiveDate,
        int defaultBleedMinutes,
        TimeZoneInfo timeZone,
        string createdAt)
    {
        var deadline = ResolveDeadline(effectiveDate, activity.DeadlineMinutes, timeZone);
        var visibleFrom = ResolveVisibleFrom(effectiveDate, activity.ShowAheadHours, timeZone);
        var actionWindowEnd = ResolveActionWindowEnd(
            effectiveDate,
            activity.BleedOverrideMinutes ?? defaultBleedMinutes,
            timeZone);
        return await connection.ExecuteAsync(InsertActivitySql, new
        {
            Id = Guid.NewGuid().ToString("N"),
            ActivityId = activity.Id,
            activity.Title,
            activity.Notes,
            ScheduleLabel = RecurrenceDescription.ForActivity(activity),
            NominalDate = Format(nominalDate),
            EffectiveDate = Format(effectiveDate),
            VisibleFromUtc = visibleFrom.ToString("O"),
            DeadlineUtc = deadline?.ToString("O"),
            ActionWindowEndUtc = actionWindowEnd.ToString("O"),
            activity.UrgencyMode,
            activity.WarningMinutes,
            CreatedAtUtc = createdAt,
        }, transaction);
    }

    private static async Task<int> InsertOneOffOccurrenceAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        OneOffTask task,
        DateOnly date,
        int defaultBleedMinutes,
        TimeZoneInfo timeZone,
        string createdAt)
    {
        var deadline = ResolveDeadline(date, task.DeadlineMinutes, timeZone);
        var visibleFrom = ResolveVisibleFrom(date, task.ShowAheadHours, timeZone);
        var actionWindowEnd = ResolveActionWindowEnd(date, task.BleedOverrideMinutes ?? defaultBleedMinutes, timeZone);
        return await connection.ExecuteAsync("""
            INSERT INTO Occurrences (
                Id, OneOffTaskId, TitleSnapshot, NotesSnapshot, ScheduleLabelSnapshot, NominalDate, EffectiveDate,
                VisibleFromUtc, DeadlineUtc, ActionWindowEndUtc, UrgencyMode, WarningMinutes, State, Version, CreatedAtUtc)
            VALUES (
                @Id, @OneOffTaskId, @Title, @Notes, @ScheduleLabel, @NominalDate, @EffectiveDate,
                @VisibleFromUtc, @DeadlineUtc, @ActionWindowEndUtc, @UrgencyMode, @WarningMinutes, 0, 0, @CreatedAtUtc)
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
                WarningMinutes = excluded.WarningMinutes
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
                Occurrences.WarningMinutes IS NOT excluded.WarningMinutes);
            """, new
        {
            Id = Guid.NewGuid().ToString("N"),
            OneOffTaskId = task.Id,
            task.Title,
            task.Notes,
            ScheduleLabel = RecurrenceDescription.OneOff,
            NominalDate = Format(date),
            EffectiveDate = Format(date),
            VisibleFromUtc = visibleFrom.ToString("O"),
            DeadlineUtc = deadline?.ToString("O"),
            ActionWindowEndUtc = actionWindowEnd.ToString("O"),
            task.UrgencyMode,
            task.WarningMinutes,
            CreatedAtUtc = createdAt,
        }, transaction);
    }

    private const string InsertActivitySql = """
        INSERT INTO Occurrences (
            Id, ActivityId, TitleSnapshot, NotesSnapshot, ScheduleLabelSnapshot, NominalDate, EffectiveDate,
            VisibleFromUtc, DeadlineUtc, ActionWindowEndUtc, UrgencyMode, WarningMinutes, State, Version, CreatedAtUtc)
        VALUES (
            @Id, @ActivityId, @Title, @Notes, @ScheduleLabel, @NominalDate, @EffectiveDate,
            @VisibleFromUtc, @DeadlineUtc, @ActionWindowEndUtc, @UrgencyMode, @WarningMinutes, 0, 0, @CreatedAtUtc)
        ON CONFLICT(ActivityId, NominalDate) WHERE ActivityId IS NOT NULL DO UPDATE SET
            TitleSnapshot = excluded.TitleSnapshot,
            NotesSnapshot = excluded.NotesSnapshot,
            ScheduleLabelSnapshot = excluded.ScheduleLabelSnapshot,
            EffectiveDate = excluded.EffectiveDate,
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
            Occurrences.VisibleFromUtc IS NOT excluded.VisibleFromUtc OR
            Occurrences.DeadlineUtc IS NOT excluded.DeadlineUtc OR
            Occurrences.ActionWindowEndUtc IS NOT excluded.ActionWindowEndUtc OR
            Occurrences.UrgencyMode IS NOT excluded.UrgencyMode OR
            Occurrences.WarningMinutes IS NOT excluded.WarningMinutes);
        """;

    private static DateTimeOffset? ResolveDeadline(DateOnly date, int? minutes, TimeZoneInfo timeZone) =>
        minutes is null ? null : LocalTimeResolver.Resolve(date, TimeOnly.MinValue.AddMinutes(minutes.Value), timeZone);

    private static DateTimeOffset ResolveVisibleFrom(DateOnly date, int showAheadHours, TimeZoneInfo timeZone) =>
        LocalTimeResolver.Resolve(date, TimeOnly.MinValue, timeZone).AddHours(-showAheadHours);

    private static DateTimeOffset ResolveActionWindowEnd(DateOnly date, int bleedMinutes, TimeZoneInfo timeZone) =>
        LocalTimeResolver.Resolve(date.AddDays(1), TimeOnly.MinValue, timeZone).AddMinutes(bleedMinutes);

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd");
}
