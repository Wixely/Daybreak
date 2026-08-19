using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Daybreak.Data;

namespace Daybreak.Services;

public sealed record HolidayInfo(DateOnly Date, string Name, string LocalName);

public interface IHolidayProvider
{
    Task<IReadOnlySet<DateOnly>> GetHolidayDatesAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<DateOnly>> RefreshHolidayDatesAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        CancellationToken cancellationToken = default) =>
        GetHolidayDatesAsync(year, countryCode, subdivisionCode, cancellationToken);
}

public sealed class NagerDateHolidayProvider(
    DatabaseConnectionFactory connections,
    IHttpClientFactory httpClients,
    TimeProvider clock,
    ILogger<NagerDateHolidayProvider> logger) : IHolidayProvider
{
    private const string ProviderName = "Nager.Date/v3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlySet<DateOnly>> GetHolidayDatesAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        CancellationToken cancellationToken = default) =>
        await GetHolidayDatesCoreAsync(year, countryCode, subdivisionCode, forceRefresh: false, cancellationToken);

    public async Task<IReadOnlySet<DateOnly>> RefreshHolidayDatesAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        CancellationToken cancellationToken = default) =>
        await GetHolidayDatesCoreAsync(year, countryCode, subdivisionCode, forceRefresh: true, cancellationToken);

    private async Task<IReadOnlySet<DateOnly>> GetHolidayDatesCoreAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        countryCode = countryCode.Trim().ToUpperInvariant();
        subdivisionCode = string.IsNullOrWhiteSpace(subdivisionCode) ? string.Empty : subdivisionCode.Trim().ToUpperInvariant();

        await using var connection = await connections.OpenAsync(cancellationToken);
        var cached = await connection.QuerySingleOrDefaultAsync<CachedHoliday>("""
            SELECT FetchedAtUtc, Payload FROM HolidayCache
            WHERE Provider = @Provider AND CountryCode = @CountryCode
              AND SubdivisionCode = @SubdivisionCode AND Year = @Year
            """, new { Provider = ProviderName, CountryCode = countryCode, SubdivisionCode = subdivisionCode, Year = year });

        var now = clock.GetUtcNow();
        if (!forceRefresh && cached is not null && DateTimeOffset.Parse(cached.FetchedAtUtc) > now.AddDays(-30))
        {
            return Parse(cached.Payload, subdivisionCode);
        }

        try
        {
            var client = httpClients.CreateClient("NagerDate");
            using var response = await client.GetAsync(
                $"api/v3/PublicHolidays/{year}/{Uri.EscapeDataString(countryCode)}",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            _ = JsonSerializer.Deserialize<List<NagerHoliday>>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Nager.Date returned an invalid holiday response.");

            await connection.ExecuteAsync("""
                INSERT INTO HolidayCache (Provider, CountryCode, SubdivisionCode, Year, FetchedAtUtc, Payload)
                VALUES (@Provider, @CountryCode, @SubdivisionCode, @Year, @FetchedAtUtc, @Payload)
                ON CONFLICT(Provider, CountryCode, SubdivisionCode, Year) DO UPDATE SET
                    FetchedAtUtc = excluded.FetchedAtUtc,
                    Payload = excluded.Payload;
                """, new
            {
                Provider = ProviderName,
                CountryCode = countryCode,
                SubdivisionCode = subdivisionCode,
                Year = year,
                FetchedAtUtc = now.ToString("O"),
                Payload = payload,
            });
            return Parse(payload, subdivisionCode);
        }
        catch (Exception exception) when (cached is not null && exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not refresh {CountryCode} holiday data for {Year}; using cached data.", countryCode, year);
            return Parse(cached.Payload, subdivisionCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not load {CountryCode} holiday data for {Year}; holiday adjustments are unavailable.", countryCode, year);
            return new HashSet<DateOnly>();
        }
    }

    private static IReadOnlySet<DateOnly> Parse(string payload, string subdivisionCode)
    {
        var entries = JsonSerializer.Deserialize<List<NagerHoliday>>(payload, JsonOptions) ?? [];
        return entries
            .Where(entry => IsApplicable(entry, subdivisionCode))
            .Select(entry => entry.Date)
            .ToHashSet();
    }

    private static bool IsApplicable(NagerHoliday holiday, string subdivisionCode)
    {
        if (string.IsNullOrWhiteSpace(subdivisionCode))
        {
            return holiday.Global;
        }

        var codes = holiday.SubdivisionCodes ?? holiday.Counties;
        return holiday.Global || (codes?.Contains(subdivisionCode, StringComparer.OrdinalIgnoreCase) ?? false);
    }

    private sealed record CachedHoliday(string FetchedAtUtc, string Payload)
    {
        public CachedHoliday() : this(string.Empty, string.Empty) { }
    }

    private sealed record NagerHoliday(
        DateOnly Date,
        string LocalName,
        string Name,
        bool Global,
        [property: JsonPropertyName("counties")] string[]? Counties,
        [property: JsonPropertyName("subdivisionCodes")] string[]? SubdivisionCodes);
}
