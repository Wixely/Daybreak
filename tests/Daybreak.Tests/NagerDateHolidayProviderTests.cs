using Daybreak.Services;

namespace Daybreak.Tests;

[TestClass]
public sealed class NagerDateHolidayProviderTests
{
    private readonly NagerDateHolidayProvider _provider = new();

    [TestMethod]
    public async Task CalculatesSubdivisionHolidaysLocally()
    {
        var england = await _provider.GetHolidayDatesAsync(2026, "gb", "gb-eng");
        var scotland = await _provider.GetHolidayDatesAsync(2026, "GB", "GB-SCT");

        Assert.IsTrue(england.Contains(new DateOnly(2026, 8, 31)));
        Assert.IsFalse(england.Contains(new DateOnly(2026, 8, 3)));
        Assert.IsFalse(england.Contains(new DateOnly(2026, 6, 15)));
        Assert.IsTrue(scotland.Contains(new DateOnly(2026, 8, 3)));
        Assert.IsTrue(scotland.Contains(new DateOnly(2026, 6, 15)));
        Assert.IsFalse(scotland.Contains(new DateOnly(2026, 8, 31)));
    }

    [TestMethod]
    public async Task UsesObservedDates()
    {
        var dates = await _provider.GetHolidayDatesAsync(2022, "GB", "GB-ENG");

        Assert.IsTrue(dates.Contains(new DateOnly(2022, 1, 3)));
        Assert.IsFalse(dates.Contains(new DateOnly(2022, 1, 1)));
    }

    [TestMethod]
    public async Task IncludesNationalHolidaysForSubdivision()
    {
        var dates = await _provider.GetHolidayDatesAsync(2026, "GB", "GB-ENG");

        Assert.IsTrue(dates.Contains(new DateOnly(2026, 12, 25)));
    }

    [TestMethod]
    public async Task RejectsUnknownCountryCodes()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _provider.GetHolidayDatesAsync(2026, "XX", null));
    }

    [TestMethod]
    public async Task HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _provider.GetHolidayDatesAsync(2026, "GB", null, cancellation.Token));
    }

    [TestMethod]
    public void HolidayCatalogOnlyIncludesCountriesBackedByBundledProviders()
    {
        Assert.IsTrue(HolidayCatalog.Countries.Any(item => item.Code == "GB"));
        Assert.IsFalse(HolidayCatalog.Countries.Any(item => item.Code == "XX"));
    }

    [TestMethod]
    public void HolidayCatalogReturnsNamedSubdivisionsForSelectedCountry()
    {
        var subdivisions = HolidayCatalog.GetSubdivisions("GB");

        Assert.HasCount(4, subdivisions);
        Assert.IsTrue(subdivisions.Any(item => item.Code == "GB-ENG" && item.Name == "England"));
        Assert.IsTrue(HolidayCatalog.IsSupportedSubdivision("GB", "gb-sct"));
        Assert.IsFalse(HolidayCatalog.IsSupportedSubdivision("GB", "US-CA"));
    }
}
