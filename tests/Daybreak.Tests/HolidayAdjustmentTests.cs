using Daybreak.Domain;
using Daybreak.Services;

namespace Daybreak.Tests;

[TestClass]
public sealed class HolidayAdjustmentTests
{
    private static readonly HashSet<DateOnly> Holidays =
    [
        new(2026, 12, 25),
        new(2026, 12, 26),
    ];

    [TestMethod]
    public void KeepLeavesHolidayOnNominalDate() =>
        Assert.AreEqual(new DateOnly(2026, 12, 25), OccurrenceGenerator.AdjustForHoliday(HolidayPolicy.Keep, new(2026, 12, 25), Holidays));

    [TestMethod]
    public void SuppressReturnsNoEffectiveDate() =>
        Assert.IsNull(OccurrenceGenerator.AdjustForHoliday(HolidayPolicy.Suppress, new(2026, 12, 25), Holidays));

    [TestMethod]
    public void MoveLaterRepeatsAcrossAdjacentHolidays() =>
        Assert.AreEqual(new DateOnly(2026, 12, 27), OccurrenceGenerator.AdjustForHoliday(HolidayPolicy.MoveLater, new(2026, 12, 25), Holidays));

    [TestMethod]
    public void MoveEarlierUsesPreviousNonHolidayDate() =>
        Assert.AreEqual(new DateOnly(2026, 12, 24), OccurrenceGenerator.AdjustForHoliday(HolidayPolicy.MoveEarlier, new(2026, 12, 25), Holidays));

    [TestMethod]
    public void MoveToPreviousWeekdayDoesNotStopOnIntermediateNonHolidayDate() =>
        Assert.AreEqual(
            new DateOnly(2026, 8, 29),
            ScheduleProjector.AdjustForHoliday(
                HolidayPolicy.MoveToPreviousWeekday,
                (int)DayOfWeek.Saturday,
                new DateOnly(2026, 8, 31),
                new HashSet<DateOnly> { new(2026, 8, 31) }));
}
