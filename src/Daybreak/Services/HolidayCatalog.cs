using System.Globalization;
using Nager.Date;
using Nager.Date.HolidayProviders;

namespace Daybreak.Services;

public sealed record HolidayCountryOption(string Code, string Name);

public sealed record HolidaySubdivisionOption(string Code, string Name);

public static class HolidayCatalog
{
    private static readonly IReadOnlyList<HolidayCountryOption> SupportedCountries = BuildCountries();

    public static IReadOnlyList<HolidayCountryOption> Countries => SupportedCountries;

    public static IReadOnlyList<HolidaySubdivisionOption> GetSubdivisions(string? countryCode)
    {
        if (!Enum.TryParse<CountryCode>(countryCode, ignoreCase: true, out var parsed) ||
            !HolidaySystem.TryGetHolidayProvider(parsed, out var provider) ||
            provider is not ISubdivisionCodesProvider subdivisions)
        {
            return [];
        }

        return subdivisions.GetSubdivisionCodes()
            .Select(item => new HolidaySubdivisionOption(item.Key, item.Value))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsSupportedCountry(string countryCode) =>
        SupportedCountries.Any(item => item.Code.Equals(countryCode, StringComparison.OrdinalIgnoreCase));

    public static bool IsSupportedSubdivision(string countryCode, string subdivisionCode) =>
        GetSubdivisions(countryCode).Any(item => item.Code.Equals(subdivisionCode, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<HolidayCountryOption> BuildCountries() => Enum.GetValues<CountryCode>()
        .Where(code => HolidaySystem.TryGetHolidayProvider(code, out _))
        .Select(code => new HolidayCountryOption(code.ToString(), CountryName(code)))
        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string CountryName(CountryCode countryCode)
    {
        try
        {
            return new RegionInfo(countryCode.ToString()).EnglishName;
        }
        catch (ArgumentException)
        {
            return countryCode.ToString();
        }
    }
}
