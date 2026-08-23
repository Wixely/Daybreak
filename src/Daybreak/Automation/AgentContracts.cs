using Daybreak.Domain;

namespace Daybreak.Automation;

public sealed record ActivityWriteRequest(
    string Title,
    string? Notes,
    RecurrenceKind RecurrenceKind,
    int Interval,
    int DaysOfWeekMask,
    int? DayOfMonth,
    int? Ordinal,
    int? Weekday,
    string StartDate,
    string? EndDate,
    int? DeadlineMinutes,
    UrgencyMode UrgencyMode,
    int WarningMinutes,
    int? BleedOverrideMinutes,
    int ShowAheadHours,
    HolidayPolicy HolidayPolicy,
    bool IsPaused);

public sealed record OneOffTaskWriteRequest(
    string Title,
    string? Notes,
    string ScheduledDate,
    int? DeadlineMinutes,
    UrgencyMode UrgencyMode,
    int WarningMinutes,
    int? BleedOverrideMinutes,
    int ShowAheadHours);

public sealed record HouseholdSettingsWriteRequest(
    string TimeZoneId,
    int DefaultBleedMinutes,
    string? HolidayCountryCode,
    string? HolidaySubdivisionCode);

public sealed record OccurrenceCommandRequest(long ExpectedVersion);

public sealed record OccurrenceCommandResult(bool Applied, BoardSnapshot Board);

public sealed record SavedEntityResult(string Id);

public sealed record SchedulePreviewItem(
    DateOnly NominalDate,
    DateOnly? EffectiveDate,
    DateTimeOffset? Deadline,
    bool Collides);

public sealed record SchedulePreviewResult(IReadOnlyList<SchedulePreviewItem> Occurrences);
