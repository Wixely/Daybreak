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

    public async Task<HolidayCacheStatus> GetHolidayCacheStatusAsync()
    {
        await using var connection = await connections.OpenAsync();
        return await connection.QuerySingleAsync<HolidayCacheStatus>("""
            SELECT COUNT(*) AS CachedYears, MAX(FetchedAtUtc) AS LastFetchedAtUtc
            FROM HolidayCache;
            """);
    }

    public async Task UpdateAsync(string timeZoneId, int defaultBleedMinutes, string? countryCode, string? subdivisionCode)
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (defaultBleedMinutes is < 0 or > 720)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultBleedMinutes));
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
            CountryCode = NullIfBlank(countryCode)?.ToUpperInvariant(),
            SubdivisionCode = NullIfBlank(subdivisionCode)?.ToUpperInvariant(),
        });
        await changes.NotifyAsync(revision);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
