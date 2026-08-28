using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class DemoDataSeeder(
    IConfiguration configuration,
    ActivityService activities,
    OneOffTaskService oneOffTasks,
    OccurrenceGenerator generator,
    SettingsService settingsService,
    DatabaseConnectionFactory connections,
    TimeProvider clock)
{
    public async Task SeedAsync()
    {
        if (!configuration.GetValue("Daybreak:SeedDemoData", false) || (await activities.ListAsync(includeArchived: true)).Count != 0)
        {
            return;
        }

        var settings = await settingsService.GetAsync();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var today = LocalTimeResolver.Today(clock, timeZone);
        var historyStart = today.AddDays(-90);
        var localNow = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone);
        var currentMinute = (localNow.Hour * 60) + localNow.Minute;

        var activityDefinitions = new[]
        {
            CreateActivity("Take morning vitamins", "With breakfast", historyStart, Math.Max(0, currentMinute - 120), UrgencyMode.BeforeAndAfterDeadline, warningMinutes: 60),
            CreateActivity("Feed the cat", "Fresh water too", historyStart, Math.Min(1439, currentMinute + 40), UrgencyMode.BeforeAndAfterDeadline, warningMinutes: 60),
            CreateActivity("Open the curtains", "Let some daylight in", historyStart, 9 * 60, UrgencyMode.None),
            CreateActivity("Empty the dishwasher", "Put everything away", historyStart, 12 * 60, UrgencyMode.AfterDeadline),
            CreateActivity("Take a screen break", "Stretch and look outside", historyStart, 15 * 60, UrgencyMode.BeforeAndAfterDeadline, warningMinutes: 45),
            CreateActivity("Put recycling out", "Paper, glass and cardboard", historyStart, 18 * 60 + 30, UrgencyMode.AfterDeadline),
            CreateActivity("Evening walk", "A short loop around the block", historyStart, 20 * 60, UrgencyMode.BeforeAndAfterDeadline, warningMinutes: 60),
            CreateActivity("Lock the back door", "Check the kitchen window", historyStart, 22 * 60 + 30, UrgencyMode.AfterDeadline),
            CreateActivity("Wipe kitchen counters", "Use the food-safe spray", historyStart, null, UrgencyMode.None),
            CreateActivity("Check tomorrow's calendar", "Make sure everyone knows the plan", historyStart, null, UrgencyMode.None),
            CreateActivity("Water the houseplants", "Check the soil before watering", today.AddDays(-30), null, UrgencyMode.None, RecurrenceKind.EveryNDays, interval: 3),
            CreateActivity("Change the bed linen", "Fresh sheets from the airing cupboard", today.AddDays(-28), null, UrgencyMode.None, RecurrenceKind.EveryNWeeks, interval: 1, daysOfWeekMask: RecurrenceCalculator.DayMask(today.DayOfWeek)),
        };

        foreach (var activity in activityDefinitions)
        {
            await activities.SaveAsync(activity);
        }

        var oneOffDefinitions = new[]
        {
            CreateOneOff("Collect the parcel", "Bring photo identification", today, 13 * 60, UrgencyMode.AfterDeadline),
            CreateOneOff("Call the vet", "Ask about the repeat prescription", today, 16 * 60, UrgencyMode.BeforeAndAfterDeadline, warningMinutes: 60),
            CreateOneOff("Replace the hallway bulb", "Warm white bulb is in the drawer", today, null, UrgencyMode.None),
            CreateOneOff("Book the boiler service", "Use the number on the last invoice", today.AddDays(1), 11 * 60, UrgencyMode.None, showAheadHours: 24),
        };

        foreach (var oneOff in oneOffDefinitions)
        {
            await oneOffTasks.SaveAsync(oneOff);
        }

        await generator.EnsureRollingHorizonAsync();
        await SeedHistoryAsync(today);
    }

    private async Task SeedHistoryAsync(DateOnly today)
    {
        await using var connection = await connections.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var todayText = today.ToString("yyyy-MM-dd");

        await connection.ExecuteAsync("""
            UPDATE Occurrences
            SET State = CASE
                    WHEN (CAST(strftime('%d', NominalDate) AS INTEGER) + length(TitleSnapshot)) % 6 = 0 THEN @Expired
                    ELSE @Completed
                END,
                CompletedAtUtc = CASE
                    WHEN (CAST(strftime('%d', NominalDate) AS INTEGER) + length(TitleSnapshot)) % 6 = 0 THEN NULL
                    WHEN DeadlineUtc IS NULL THEN strftime('%Y-%m-%dT18:00:00.000Z', EffectiveDate)
                    WHEN (CAST(strftime('%d', NominalDate) AS INTEGER) + length(TitleSnapshot)) % 4 = 0
                        THEN strftime('%Y-%m-%dT%H:%M:%fZ', DeadlineUtc, '+25 minutes')
                    ELSE strftime('%Y-%m-%dT%H:%M:%fZ', DeadlineUtc, '-20 minutes')
                END,
                Version = 1
            WHERE NominalDate < @Today AND State = @Pending;

            INSERT INTO OccurrenceEvents (
                OccurrenceId, EventType, OccurredAtUtc, PreviousState, NewState, Actor, Details)
            SELECT Id,
                   CASE WHEN State = @Expired THEN 'Expired' ELSE 'Completed' END,
                   COALESCE(CompletedAtUtc, ActionWindowEndUtc),
                   @Pending,
                   State,
                   NULL,
                   'Development sample data'
            FROM Occurrences
            WHERE NominalDate < @Today AND State IN (@Completed, @Expired);

            UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1;
            """, new
        {
            Today = todayText,
            Pending = OccurrenceState.Pending,
            Completed = OccurrenceState.Completed,
            Expired = OccurrenceState.Expired,
        }, transaction);

        await transaction.CommitAsync();
    }

    private static Activity CreateActivity(
        string title,
        string notes,
        DateOnly start,
        int? deadlineMinutes,
        UrgencyMode urgencyMode,
        RecurrenceKind recurrenceKind = RecurrenceKind.Daily,
        int interval = 1,
        int daysOfWeekMask = 0,
        int warningMinutes = 30) => new(
            string.Empty,
            title,
            notes,
            recurrenceKind,
            interval,
            daysOfWeekMask,
            null,
            null,
            null,
            start.ToString("yyyy-MM-dd"),
            null,
            deadlineMinutes,
            urgencyMode,
            warningMinutes,
            null,
            0,
            HolidayPolicy.Keep,
            null,
            false,
            null,
            string.Empty,
            string.Empty);

    private static OneOffTask CreateOneOff(
        string title,
        string notes,
        DateOnly scheduledDate,
        int? deadlineMinutes,
        UrgencyMode urgencyMode,
        int warningMinutes = 30,
        int showAheadHours = 0) => new(
            string.Empty,
            title,
            notes,
            scheduledDate.ToString("yyyy-MM-dd"),
            deadlineMinutes,
            urgencyMode,
            warningMinutes,
            null,
            showAheadHours,
            false,
            "Demo",
            null,
            string.Empty,
            string.Empty);
}
