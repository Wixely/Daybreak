using Daybreak.Domain;

namespace Daybreak.Tests;

[TestClass]
public sealed class BoardItemTests
{
    [TestMethod]
    public void NonUrgentItemNeverBecomesUrgent()
    {
        var deadline = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(UrgencyMode.None, deadline);

        Assert.IsFalse(item.IsUrgent(deadline.AddHours(1)));
    }

    [TestMethod]
    public void BeforeAndAfterItemBecomesUrgentInsideWarningWindow()
    {
        var deadline = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(UrgencyMode.BeforeAndAfterDeadline, deadline);

        Assert.IsFalse(item.IsUrgent(deadline.AddMinutes(-31)));
        Assert.IsTrue(item.IsUrgent(deadline.AddMinutes(-30)));
        Assert.IsTrue(item.IsUrgent(deadline.AddMinutes(1)));
    }

    [TestMethod]
    public void DeadlineCountdownRunsFromFullToEmptyDuringFinalHour()
    {
        var deadline = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.IsNull(DeadlineCountdown.Progress(deadline.AddMinutes(-61), deadline));
        Assert.AreEqual(0d, DeadlineCountdown.Progress(deadline.AddHours(-1), deadline));
        Assert.AreEqual(0.5d, DeadlineCountdown.Progress(deadline.AddMinutes(-30), deadline));
        Assert.AreEqual(1d, DeadlineCountdown.Progress(deadline, deadline));
        Assert.AreEqual(1d, DeadlineCountdown.Progress(deadline.AddMinutes(10), deadline));
    }

    [TestMethod]
    [DataRow(20, "About 20 minutes")]
    [DataRow(62, "About an hour")]
    [DataRow(190, "About 3 hours")]
    [DataRow(2880, "About 2 days")]
    [DataRow(-20, "About 20 minutes ago")]
    [DataRow(-62, "About an hour ago")]
    public void DeadlineEstimateUsesReadableRoundedDurations(int minutes, string expected)
    {
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(expected, DeadlineEstimate.Format(now, now.AddMinutes(minutes)));
    }

    private static BoardItem CreateItem(UrgencyMode urgencyMode, DateTimeOffset deadline) => new(
        "id",
        "Example",
        null,
        "Daily",
        new DateOnly(2026, 8, 19),
        new DateOnly(2026, 8, 19),
        deadline,
        deadline.AddHours(14),
        urgencyMode,
        30,
        OccurrenceState.Pending,
        null,
        0);
}
