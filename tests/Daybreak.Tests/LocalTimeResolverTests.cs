using Daybreak.Domain;

namespace Daybreak.Tests;

[TestClass]
public sealed class LocalTimeResolverTests
{
    [TestMethod]
    public void InvalidSpringTimeMovesToFirstValidMinute()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        var instant = LocalTimeResolver.Resolve(new DateOnly(2026, 3, 29), new TimeOnly(1, 30), zone);

        Assert.AreEqual(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), instant);
    }

    [TestMethod]
    public void AmbiguousAutumnTimeUsesTheLaterStandardTimeInstant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        var instant = LocalTimeResolver.Resolve(new DateOnly(2026, 10, 25), new TimeOnly(1, 30), zone);

        Assert.AreEqual(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), instant);
    }
}
