namespace Daybreak.Domain;

public enum RecurrenceKind
{
    Daily = 0,
    SelectedWeekdays = 1,
    EveryNDays = 2,
    EveryNWeeks = 3,
    MonthlyDate = 4,
    MonthlyOrdinalWeekday = 5,
}

public enum UrgencyMode
{
    None = 0,
    AfterDeadline = 1,
    BeforeAndAfterDeadline = 2,
}

public enum HolidayPolicy
{
    Keep = 0,
    Suppress = 1,
    MoveEarlier = 2,
    MoveLater = 3,
}

public enum OccurrenceState
{
    Pending = 0,
    Completed = 1,
    Expired = 2,
}

public sealed record HouseholdSettings(
    long Id,
    string TimeZoneId,
    int DefaultBleedMinutes,
    string? HolidayCountryCode,
    string? HolidaySubdivisionCode,
    long BoardRevision)
{
    public HouseholdSettings() : this(0, string.Empty, 0, null, null, 0) { }
}

public sealed record Activity(
    string Id,
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
    bool IsPaused,
    string? ArchivedAtUtc,
    string CreatedAtUtc,
    string UpdatedAtUtc)
{
    public Activity() : this(
        string.Empty, string.Empty, null, RecurrenceKind.Daily, 1, 0, null, null, null,
        string.Empty, null, null, UrgencyMode.None, 30, null, 0, HolidayPolicy.Keep,
        false, null, string.Empty, string.Empty)
    { }
}

public sealed record OneOffTask(
    string Id,
    string Title,
    string? Notes,
    string ScheduledDate,
    int? DeadlineMinutes,
    UrgencyMode UrgencyMode,
    int WarningMinutes,
    int? BleedOverrideMinutes,
    int ShowAheadHours,
    string? SourceKind,
    string? SourceReference,
    string CreatedAtUtc,
    string UpdatedAtUtc)
{
    public OneOffTask() : this(
        string.Empty, string.Empty, null, string.Empty, null, UrgencyMode.None, 30,
        null, 0, null, null, string.Empty, string.Empty)
    { }
}

public sealed record Occurrence(
    string Id,
    string? ActivityId,
    string? OneOffTaskId,
    string TitleSnapshot,
    string? NotesSnapshot,
    string ScheduleLabelSnapshot,
    string NominalDate,
    string EffectiveDate,
    string? VisibleFromUtc,
    string? DeadlineUtc,
    string ActionWindowEndUtc,
    UrgencyMode UrgencyMode,
    int WarningMinutes,
    OccurrenceState State,
    string? CompletedAtUtc,
    string? CompletedBy,
    long Version,
    string CreatedAtUtc)
{
    public Occurrence() : this(
        string.Empty, null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
        null, null, string.Empty, UrgencyMode.None, 30, OccurrenceState.Pending,
        null, null, 0, string.Empty)
    { }
}

public sealed record BoardItem(
    string Id,
    string Title,
    string? Notes,
    string ScheduleLabel,
    DateOnly NominalDate,
    DateOnly EffectiveDate,
    DateTimeOffset? Deadline,
    DateTimeOffset ActionWindowEnd,
    UrgencyMode UrgencyMode,
    int WarningMinutes,
    OccurrenceState State,
    DateTimeOffset? CompletedAt,
    long Version)
{
    public bool IsCarried(DateOnly today) => EffectiveDate < today;

    public bool IsOverdue(DateTimeOffset now) =>
        State == OccurrenceState.Pending && Deadline is not null && now >= Deadline;

    public bool IsDueSoon(DateTimeOffset now) =>
        State == OccurrenceState.Pending &&
        UrgencyMode == UrgencyMode.BeforeAndAfterDeadline &&
        Deadline is not null &&
        now >= Deadline.Value.AddMinutes(-WarningMinutes) &&
        now < Deadline;

    public bool IsUrgent(DateTimeOffset now) => UrgencyMode switch
    {
        UrgencyMode.None => false,
        UrgencyMode.AfterDeadline => IsOverdue(now),
        UrgencyMode.BeforeAndAfterDeadline => IsDueSoon(now) || IsOverdue(now),
        _ => false,
    };
}

public sealed record BoardSnapshot(DateOnly Today, string TimeZoneId, long Revision, IReadOnlyList<BoardItem> Items);

public sealed record HistoryEntry(
    string Id,
    string Title,
    DateOnly NominalDate,
    DateOnly EffectiveDate,
    OccurrenceState State,
    DateTimeOffset? Deadline,
    DateTimeOffset? CompletedAt)
{
    public string Timing => State switch
    {
        OccurrenceState.Expired => "Unfinished",
        OccurrenceState.Completed when Deadline is null || CompletedAt <= Deadline => "On time",
        OccurrenceState.Completed => "Late",
        _ => "Pending",
    };
}

public sealed record ActivityCompletionSummary(string Title, int Total, int Completed, int Expired)
{
    public decimal CompletionRate => Total == 0 ? 0 : decimal.Round((decimal)Completed / Total * 100, 1);
}

public sealed record WeeklyCompletionSummary(DateOnly WeekStart, int Total, int Completed)
{
    public decimal CompletionRate => Total == 0 ? 0 : decimal.Round((decimal)Completed / Total * 100, 1);
}

public sealed record MonthlyCompletionSummary(DateOnly MonthStart, int Total, int Completed)
{
    public decimal CompletionRate => Total == 0 ? 0 : decimal.Round((decimal)Completed / Total * 100, 1);
}

public sealed record AuditEventEntry(
    long Id,
    string Title,
    string EventType,
    DateTimeOffset OccurredAt,
    OccurrenceState? PreviousState,
    OccurrenceState NewState);

public sealed record HistorySnapshot(
    int Total,
    int Completed,
    int OnTime,
    int Late,
    int Unfinished,
    IReadOnlyList<ActivityCompletionSummary> Activities,
    IReadOnlyList<WeeklyCompletionSummary> Weeks,
    IReadOnlyList<MonthlyCompletionSummary> Months,
    IReadOnlyList<HistoryEntry> Recent,
    IReadOnlyList<AuditEventEntry> Events)
{
    public decimal CompletionRate => Total == 0 ? 0 : decimal.Round((decimal)Completed / Total * 100, 1);
}
