using Daybreak.Domain;

namespace Daybreak.Tests;

[TestClass]
public sealed class RecurrenceCalculatorTests
{
    [TestMethod]
    public void SelectedWeekdaysOnlyMatchConfiguredDays()
    {
        var activity = Create(
            RecurrenceKind.SelectedWeekdays,
            daysMask: RecurrenceCalculator.DayMask(DayOfWeek.Monday) | RecurrenceCalculator.DayMask(DayOfWeek.Friday));

        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 21)));
        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 22)));
        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 24)));
    }

    [TestMethod]
    public void EveryNDaysAnchorsToStartDate()
    {
        var activity = Create(RecurrenceKind.EveryNDays, interval: 3);

        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 19)));
        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 20)));
        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 22)));
    }

    [TestMethod]
    public void EveryNWeeksUsesStartDateWeekAndSelectedWeekdays()
    {
        var activity = Create(
            RecurrenceKind.EveryNWeeks,
            interval: 2,
            daysMask: RecurrenceCalculator.DayMask(DayOfWeek.Monday));

        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 24)));
        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 31)));
        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 9, 7)));
    }

    [TestMethod]
    public void StartAndEndDatesBoundEveryRecurrenceKind()
    {
        var activity = Create(RecurrenceKind.Daily) with { EndDate = "2026-08-21" };

        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 18)));
        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 21)));
        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 22)));
    }

    [TestMethod]
    public void MonthlyDateDoesNotClampToShortMonth()
    {
        var activity = Create(RecurrenceKind.MonthlyDate, dayOfMonth: 31);

        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 31)));
        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 9, 30)));
    }

    [TestMethod]
    public void FifthOrdinalMeansLastWeekday()
    {
        var activity = Create(RecurrenceKind.MonthlyOrdinalWeekday, ordinal: 5, weekday: (int)DayOfWeek.Monday);

        Assert.IsFalse(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 24)));
        Assert.IsTrue(RecurrenceCalculator.OccursOn(activity, new DateOnly(2026, 8, 31)));
    }

    private static Activity Create(
        RecurrenceKind kind,
        int interval = 1,
        int daysMask = 0,
        int? dayOfMonth = null,
        int? ordinal = null,
        int? weekday = null) => new(
            "id", "Test", null, kind, interval, daysMask, dayOfMonth, ordinal, weekday,
            "2026-08-19", null, null, UrgencyMode.None, 30, null, HolidayPolicy.Keep,
            false, null, string.Empty, string.Empty);
}
