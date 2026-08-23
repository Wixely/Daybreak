using Daybreak.Domain;
using Daybreak.Services;

namespace Daybreak.Automation;

public sealed class AgentOperations(
    BoardService board,
    ActivityService activities,
    OneOffTaskService oneOffTasks,
    SettingsService settings,
    HistoryService history,
    OccurrenceGenerator generator,
    IHolidayProvider holidays)
{
    public Task<BoardSnapshot> GetBoardAsync(CancellationToken cancellationToken = default) =>
        board.GetSnapshotAsync(cancellationToken);

    public Task<IReadOnlyList<Activity>> ListActivitiesAsync(bool includeArchived = false) =>
        activities.ListAsync(includeArchived);

    public async Task<SavedEntityResult> SaveActivityAsync(
        ActivityWriteRequest request,
        string? id = null,
        CancellationToken cancellationToken = default)
    {
        var existing = string.IsNullOrWhiteSpace(id) ? null : await activities.GetAsync(id);
        if (id is not null && existing is null)
        {
            throw new KeyNotFoundException("The activity does not exist.");
        }

        var activity = new Activity(
            id ?? string.Empty,
            request.Title,
            request.Notes,
            request.RecurrenceKind,
            request.Interval,
            request.DaysOfWeekMask,
            request.DayOfMonth,
            request.Ordinal,
            request.Weekday,
            request.StartDate,
            request.EndDate,
            request.DeadlineMinutes,
            request.UrgencyMode,
            request.WarningMinutes,
            request.BleedOverrideMinutes,
            request.ShowAheadHours,
            request.HolidayPolicy,
            request.IsPaused,
            existing?.ArchivedAtUtc,
            existing?.CreatedAtUtc ?? string.Empty,
            existing?.UpdatedAtUtc ?? string.Empty);
        var savedId = await activities.SaveAsync(activity);
        await generator.EnsureRollingHorizonAsync(cancellationToken);
        return new SavedEntityResult(savedId);
    }

    public async Task ArchiveActivityAsync(string id)
    {
        if (await activities.GetAsync(id) is null)
        {
            throw new KeyNotFoundException("The activity does not exist.");
        }

        await activities.ArchiveAsync(id);
    }

    public async Task RestoreActivityAsync(string id, CancellationToken cancellationToken = default)
    {
        if (await activities.GetAsync(id) is null)
        {
            throw new KeyNotFoundException("The activity does not exist.");
        }

        await activities.RestoreAsync(id);
        await generator.EnsureRollingHorizonAsync(cancellationToken);
    }

    public Task<IReadOnlyList<OneOffTask>> ListOneOffTasksAsync() => oneOffTasks.ListAsync();

    public async Task<SavedEntityResult> SaveOneOffTaskAsync(
        OneOffTaskWriteRequest request,
        string? id = null,
        CancellationToken cancellationToken = default)
    {
        var existing = string.IsNullOrWhiteSpace(id)
            ? null
            : (await oneOffTasks.ListAsync()).SingleOrDefault(item => item.Id == id);
        if (id is not null && existing is null)
        {
            throw new KeyNotFoundException("The one-off task does not exist or is no longer editable.");
        }

        var task = new OneOffTask(
            id ?? string.Empty,
            request.Title,
            request.Notes,
            request.ScheduledDate,
            request.DeadlineMinutes,
            request.UrgencyMode,
            request.WarningMinutes,
            request.BleedOverrideMinutes,
            request.ShowAheadHours,
            existing?.SourceKind,
            existing?.SourceReference,
            existing?.CreatedAtUtc ?? string.Empty,
            existing?.UpdatedAtUtc ?? string.Empty);
        var savedId = await oneOffTasks.SaveAsync(task);
        await generator.EnsureRollingHorizonAsync(cancellationToken);
        return new SavedEntityResult(savedId);
    }

    public async Task DeleteOneOffTaskAsync(string id)
    {
        if (!await oneOffTasks.DeletePendingAsync(id))
        {
            throw new KeyNotFoundException("The one-off task does not exist or is retained in history.");
        }
    }

    public Task<HouseholdSettings> GetSettingsAsync() => settings.GetAsync();

    public async Task UpdateSettingsAsync(
        HouseholdSettingsWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        await settings.UpdateAsync(
            request.TimeZoneId,
            request.DefaultBleedMinutes,
            request.HolidayCountryCode,
            request.HolidaySubdivisionCode);
        await generator.EnsureRollingHorizonAsync(cancellationToken);
    }

    public Task<HistorySnapshot> GetHistoryAsync(int recentLimit = 100) => history.GetAsync(recentLimit);

    public async Task<OccurrenceCommandResult> CompleteAsync(
        string id,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var applied = await board.CompleteAsync(id, expectedVersion, cancellationToken);
        return new OccurrenceCommandResult(applied, await board.GetSnapshotAsync(cancellationToken));
    }

    public async Task<OccurrenceCommandResult> UndoAsync(
        string id,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var applied = await board.UndoAsync(id, expectedVersion, cancellationToken);
        return new OccurrenceCommandResult(applied, await board.GetSnapshotAsync(cancellationToken));
    }

    public async Task<SchedulePreviewResult> PreviewAsync(
        ActivityWriteRequest request,
        int count = 8,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Preview count must be between 1 and 32.");
        }

        if (!DateOnly.TryParse(request.StartDate, out var startDate))
        {
            throw new ArgumentException("A valid activity start date is required.", nameof(request));
        }

        var activity = new Activity(
            string.Empty,
            request.Title,
            request.Notes,
            request.RecurrenceKind,
            request.Interval,
            request.DaysOfWeekMask,
            request.DayOfMonth,
            request.Ordinal,
            request.Weekday,
            request.StartDate,
            request.EndDate,
            request.DeadlineMinutes,
            request.UrgencyMode,
            request.WarningMinutes,
            request.BleedOverrideMinutes,
            request.ShowAheadHours,
            request.HolidayPolicy,
            request.IsPaused,
            null,
            string.Empty,
            string.Empty);
        var household = await settings.GetAsync();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(household.TimeZoneId);
        var nominalDates = RecurrenceCalculator.Preview(activity, startDate, count);
        var holidayDates = new HashSet<DateOnly>();
        if (request.HolidayPolicy != HolidayPolicy.Keep && household.HolidayCountryCode is not null && nominalDates.Count > 0)
        {
            for (var year = nominalDates.Min().AddDays(-14).Year; year <= nominalDates.Max().AddDays(14).Year; year++)
            {
                holidayDates.UnionWith(await holidays.GetHolidayDatesAsync(
                    year,
                    household.HolidayCountryCode,
                    household.HolidaySubdivisionCode,
                    cancellationToken));
            }
        }

        var adjusted = nominalDates
            .Select(nominal => new
            {
                Nominal = nominal,
                Effective = OccurrenceGenerator.AdjustForHoliday(request.HolidayPolicy, nominal, holidayDates),
            })
            .ToList();
        var collisions = adjusted
            .Where(item => item.Effective is not null)
            .GroupBy(item => item.Effective)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        return new SchedulePreviewResult(adjusted
            .Select(item => new SchedulePreviewItem(
                item.Nominal,
                item.Effective,
                item.Effective is not null && request.DeadlineMinutes is not null
                    ? LocalTimeResolver.Resolve(
                        item.Effective.Value,
                        TimeOnly.MinValue.AddMinutes(request.DeadlineMinutes.Value),
                        timeZone)
                    : null,
                item.Effective is not null && collisions.Contains(item.Effective)))
            .ToList());
    }
}
