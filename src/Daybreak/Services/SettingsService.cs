using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class SettingsService(DatabaseConnectionFactory connections, BoardChangeNotifier changes)
{
    public async Task<HouseholdSettings> GetAsync()
    {
        await using var connection = await connections.OpenAsync();
        return await connection.QuerySingleAsync<HouseholdSettings>("SELECT * FROM HouseholdSettings WHERE Id = 1");
    }

    public async Task UpdateAsync(string timeZoneId, int defaultBleedMinutes, string? countryCode, string? subdivisionCode)
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (defaultBleedMinutes is < 0 or > 720)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultBleedMinutes));
        }

        countryCode = NullIfBlank(countryCode)?.ToUpperInvariant();
        subdivisionCode = NullIfBlank(subdivisionCode)?.ToUpperInvariant();
        if (countryCode is not null && !HolidayCatalog.IsSupportedCountry(countryCode))
        {
            throw new ArgumentException("Choose a country supported by the bundled holiday engine.", nameof(countryCode));
        }

        if (subdivisionCode is not null &&
            (countryCode is null || !HolidayCatalog.IsSupportedSubdivision(countryCode, subdivisionCode)))
        {
            throw new ArgumentException("Choose a subdivision supported for the selected country.", nameof(subdivisionCode));
        }

        await using var connection = await connections.OpenAsync();
        var revision = await connection.ExecuteScalarAsync<long>("""
            UPDATE HouseholdSettings
            SET TimeZoneId = @TimeZoneId,
                DefaultBleedMinutes = @DefaultBleedMinutes,
                HolidayCountryCode = @CountryCode,
                HolidaySubdivisionCode = @SubdivisionCode,
                BoardRevision = BoardRevision + 1
            WHERE Id = 1
            RETURNING BoardRevision;
            """, new
        {
            TimeZoneId = timeZoneId,
            DefaultBleedMinutes = defaultBleedMinutes,
            CountryCode = countryCode,
            SubdivisionCode = subdivisionCode,
        });
        await changes.NotifyAsync(revision);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
