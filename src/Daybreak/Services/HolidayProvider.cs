using Nager.Date;
using Nager.Date.Models;

namespace Daybreak.Services;

public interface IHolidayProvider
{
    Task<IReadOnlySet<DateOnly>> GetHolidayDatesAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        CancellationToken cancellationToken = default);

}

public sealed class NagerDateHolidayProvider : IHolidayProvider
{
    public Task<IReadOnlySet<DateOnly>> GetHolidayDatesAsync(
        int year,
        string countryCode,
        string? subdivisionCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        countryCode = countryCode.Trim().ToUpperInvariant();
        subdivisionCode = string.IsNullOrWhiteSpace(subdivisionCode)
            ? null
            : subdivisionCode.Trim().ToUpperInvariant();

        IReadOnlySet<DateOnly> dates = HolidaySystem.GetHolidays(year, countryCode)
            .Where(holiday => IsApplicable(holiday, subdivisionCode))
            .Select(holiday => DateOnly.FromDateTime(holiday.ObservedDate))
            .ToHashSet();
        return Task.FromResult(dates);
    }

    private static bool IsApplicable(Holiday holiday, string? subdivisionCode)
    {
        if (string.IsNullOrWhiteSpace(subdivisionCode))
        {
            return holiday.NationalHoliday;
        }

        return holiday.NationalHoliday ||
            (holiday.SubdivisionCodes?.Contains(subdivisionCode, StringComparer.OrdinalIgnoreCase) ?? false);
    }
}
