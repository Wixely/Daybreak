using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class HistoryService(
    DatabaseConnectionFactory connections,
    SettingsService settingsService,
    TimeProvider clock)
{
    public async Task<HistorySnapshot> GetAsync(int recentLimit = 100)
    {
        var settings = await settingsService.GetAsync();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var today = LocalTimeResolver.Today(clock, timeZone);
        await using var connection = await connections.OpenAsync();
        var rows = (await connection.QueryAsync<HistoryRow>("""
            SELECT Id, TitleSnapshot AS Title, NominalDate, EffectiveDate, State, DeadlineUtc, CompletedAtUtc
            FROM Occurrences
            WHERE State IN (@Completed, @Expired) AND NominalDate <= @Today
            ORDER BY NominalDate DESC, COALESCE(CompletedAtUtc, ActionWindowEndUtc) DESC
            """, new
        {
            Completed = OccurrenceState.Completed,
            Expired = OccurrenceState.Expired,
            Today = today.ToString("yyyy-MM-dd"),
        })).Select(row => Map(row, timeZone)).ToList();

        var activities = rows
            .GroupBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ActivityCompletionSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.State == OccurrenceState.Completed),
                group.Count(item => item.State == OccurrenceState.Expired)))
            .OrderByDescending(item => item.Total)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weeks = rows
            .GroupBy(row => StartOfWeek(row.NominalDate))
            .Select(group => new WeeklyCompletionSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.State == OccurrenceState.Completed)))
            .OrderByDescending(item => item.WeekStart)
            .Take(8)
            .OrderBy(item => item.WeekStart)
            .ToList();

        var months = rows
            .GroupBy(row => new DateOnly(row.NominalDate.Year, row.NominalDate.Month, 1))
            .Select(group => new MonthlyCompletionSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.State == OccurrenceState.Completed)))
            .OrderByDescending(item => item.MonthStart)
            .Take(12)
            .OrderBy(item => item.MonthStart)
            .ToList();

        var eventRows = await connection.QueryAsync<EventRow>("""
            SELECT event.Id, occurrence.TitleSnapshot AS Title, event.EventType,
                   event.OccurredAtUtc, event.PreviousState, event.NewState
            FROM OccurrenceEvents event
            INNER JOIN Occurrences occurrence ON occurrence.Id = event.OccurrenceId
            ORDER BY event.Id DESC
            LIMIT @RecentLimit;
            """, new { RecentLimit = recentLimit });
        var events = eventRows.Select(row => new AuditEventEntry(
            row.Id,
            row.Title,
            row.EventType,
            TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(row.OccurredAtUtc), timeZone),
            row.PreviousState,
            row.NewState)).ToList();

        return new HistorySnapshot(
            rows.Count,
            rows.Count(item => item.State == OccurrenceState.Completed),
            rows.Count(item => item.Timing == "On time"),
            rows.Count(item => item.Timing == "Late"),
            rows.Count(item => item.State == OccurrenceState.Expired),
            activities,
            weeks,
            months,
            rows.Take(recentLimit).ToList(),
            events);
    }

    private static HistoryEntry Map(HistoryRow row, TimeZoneInfo timeZone) => new(
        row.Id,
        row.Title,
        DateOnly.Parse(row.NominalDate),
        DateOnly.Parse(row.EffectiveDate),
        row.State,
        row.DeadlineUtc is null ? null : TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(row.DeadlineUtc), timeZone),
        row.CompletedAtUtc is null ? null : TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(row.CompletedAtUtc), timeZone));

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var difference = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-difference);
    }

    private sealed record HistoryRow(
        string Id,
        string Title,
        string NominalDate,
        string EffectiveDate,
        OccurrenceState State,
        string? DeadlineUtc,
        string? CompletedAtUtc)
    {
        public HistoryRow() : this(
            string.Empty, string.Empty, string.Empty, string.Empty,
            OccurrenceState.Pending, null, null)
        { }
    }

    private sealed record EventRow(
        long Id,
        string Title,
        string EventType,
        string OccurredAtUtc,
        OccurrenceState? PreviousState,
        OccurrenceState NewState)
    {
        public EventRow() : this(0, string.Empty, string.Empty, string.Empty, null, OccurrenceState.Pending) { }
    }
}
