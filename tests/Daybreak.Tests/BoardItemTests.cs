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

    private static BoardItem CreateItem(UrgencyMode urgencyMode, DateTimeOffset deadline) => new(
        "id",
        "Example",
        null,
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
