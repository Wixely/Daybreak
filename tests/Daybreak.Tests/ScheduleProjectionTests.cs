using Daybreak.Domain;

namespace Daybreak.Tests;

[TestClass]
public sealed class ScheduleProjectionTests
{
    private readonly ScheduleProjector _projector = new();
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [TestMethod]
    public void MondayHolidayMovesToPreviousSaturdayWithConcreteExplanation()
    {
        var activity = CreateActivity(
            RecurrenceKind.SelectedWeekdays,
            RecurrenceCalculator.DayMask(DayOfWeek.Monday)) with
        {
            Title = "Bins",
            HolidayPolicy = HolidayPolicy.MoveToPreviousWeekday,
            HolidayTargetWeekday = (int)DayOfWeek.Saturday,
        };
        var holiday = new DateOnly(2026, 8, 31);

        var result = _projector.ProjectActivity(activity, [holiday], new HashSet<DateOnly> { holiday }, 120, _timeZone).Single();

        Assert.AreEqual(new DateOnly(2026, 8, 29), result.EffectiveDate);
        StringAssert.Contains(result.AdjustmentExplanation, "Monday 31 August 2026");
        StringAssert.Contains(result.AdjustmentExplanation, "Saturday 29 August 2026");
        StringAssert.Contains(_projector.Explain(activity, 120).Holiday, "previous Saturday");
    }

    [TestMethod]
    public void ProjectionProvidesShowEarlyUrgencyDeadlineAndBleedBoundaries()
    {
        var activity = CreateActivity(RecurrenceKind.Daily) with
        {
            ShowAheadHours = 24,
            DeadlineMinutes = 7 * 60,
            UrgencyMode = UrgencyMode.BeforeAndAfterDeadline,
            WarningMinutes = 30,
            BleedOverrideMinutes = 120,
        };

        var item = _projector.ProjectActivity(
            activity,
            [new DateOnly(2026, 8, 29)],
            new HashSet<DateOnly>(),
            60,
            _timeZone).Single();

        Assert.AreEqual("2026-08-28 00:00", Local(item.VisibleFrom));
        Assert.AreEqual("2026-08-29 06:30", Local(item.UrgentFrom));
        Assert.AreEqual("2026-08-29 07:00", Local(item.Deadline));
        Assert.AreEqual("2026-08-30 02:00", Local(item.ActionWindowEnd));
    }

    [TestMethod]
    public void DailyOccurrencesOverlapDuringBleed()
    {
        var activity = CreateActivity(RecurrenceKind.Daily) with { BleedOverrideMinutes = 120 };
        var items = _projector.ProjectActivity(
            activity,
            [new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 19)],
            new HashSet<DateOnly>(),
            0,
            _timeZone);
        var simulatedNow = LocalTimeResolver.Resolve(new DateOnly(2026, 8, 19), new TimeOnly(1, 0), _timeZone);

        Assert.HasCount(2, items.Where(item => item.VisibleFrom <= simulatedNow && item.ActionWindowEnd > simulatedNow));
        Assert.AreNotEqual(items[0].NominalDate, items[1].NominalDate);
    }

    [TestMethod]
    public void ProjectionUsesRealDstDayLength()
    {
        var item = _projector.ProjectActivity(
            CreateActivity(RecurrenceKind.Daily),
            [new DateOnly(2026, 3, 29)],
            new HashSet<DateOnly>(),
            0,
            _timeZone).Single();

        Assert.AreEqual(23, (item.EffectiveDayEnd!.Value - item.VisibleFrom!.Value).TotalHours);
    }

    [TestMethod]
    public void NoDeadlineExplanationStatesThatUrgencyCannotActivate()
    {
        var explanation = _projector.Explain(
            CreateActivity(RecurrenceKind.Daily) with { UrgencyMode = UrgencyMode.BeforeAndAfterDeadline },
            120);

        StringAssert.Contains(explanation.Urgency, "no deadline");
        StringAssert.Contains(explanation.Urgency, "cannot activate");
    }

    private string? Local(DateTimeOffset? value) => value is null
        ? null
        : TimeZoneInfo.ConvertTime(value.Value, _timeZone).ToString("yyyy-MM-dd HH:mm");

    private static Activity CreateActivity(RecurrenceKind kind, int daysMask = 0) => new(
        string.Empty, "Test", null, kind, 1, daysMask, null, null, null,
        "2026-01-01", null, null, UrgencyMode.None, 30, null, 0,
        HolidayPolicy.Keep, null, false, null, string.Empty, string.Empty);
}
