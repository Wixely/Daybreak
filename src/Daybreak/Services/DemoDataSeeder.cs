using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class DemoDataSeeder(
    IConfiguration configuration,
    ActivityService activities,
    OneOffTaskService oneOffTasks,
    OccurrenceGenerator generator,
    SettingsService settingsService,
    TimeProvider clock)
{
    public async Task SeedAsync()
    {
        if (!configuration.GetValue("Daybreak:SeedDemoData", false) || (await activities.ListAsync(includeArchived: true)).Count != 0)
        {
            return;
        }

        var settings = await settingsService.GetAsync();
        var today = LocalTimeResolver.Today(clock, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId));
        await activities.SaveAsync(CreateActivity(
            "Take vitamins",
            "With breakfast",
            today,
            deadlineMinutes: 9 * 60,
            UrgencyMode.BeforeAndAfterDeadline));
        await activities.SaveAsync(CreateActivity(
            "Water the plants",
            "Check the soil first",
            today,
            deadlineMinutes: null,
            UrgencyMode.None));
        await activities.SaveAsync(CreateActivity(
            "Take out the bins",
            "General waste and recycling",
            today,
            deadlineMinutes: 19 * 60,
            UrgencyMode.AfterDeadline));
        await oneOffTasks.SaveAsync(new OneOffTask(
            string.Empty,
            "Clean the fridge shelf",
            "Use the food-safe spray",
            today.ToString("yyyy-MM-dd"),
            20 * 60,
            UrgencyMode.None,
            30,
            null,
            0,
            "Demo",
            null,
            string.Empty,
            string.Empty));
        await generator.EnsureRollingHorizonAsync();
    }

    private static Activity CreateActivity(
        string title,
        string notes,
        DateOnly start,
        int? deadlineMinutes,
        UrgencyMode urgencyMode) => new(
            string.Empty,
            title,
            notes,
            RecurrenceKind.Daily,
            1,
            0,
            null,
            null,
            null,
            start.ToString("yyyy-MM-dd"),
            null,
            deadlineMinutes,
            urgencyMode,
            30,
            null,
            0,
            HolidayPolicy.Keep,
            false,
            null,
            string.Empty,
            string.Empty);
}
