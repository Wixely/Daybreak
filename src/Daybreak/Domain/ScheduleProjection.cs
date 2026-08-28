using System.Globalization;

namespace Daybreak.Domain;

public sealed record ScheduleProjectionItem(
    DateOnly NominalDate,
    DateOnly? EffectiveDate,
    DateTimeOffset? VisibleFrom,
    DateTimeOffset? Deadline,
    DateTimeOffset? UrgentFrom,
    DateTimeOffset? EffectiveDayEnd,
    DateTimeOffset? ActionWindowEnd,
    bool Collides,
    string AdjustmentExplanation,
    bool IsPermanent);

public sealed record ScheduleExplanation(
    string Recurrence,
    string Holiday,
    string ShowEarly,
    string Bleed,
    string Urgency)
{
    public string Combined => string.Join(' ', new[] { Recurrence, Holiday, ShowEarly, Bleed, Urgency }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class ScheduleProjector
{
    public IReadOnlyList<ScheduleProjectionItem> ProjectActivity(
        Activity activity,
        IEnumerable<DateOnly> nominalDates,
        IReadOnlySet<DateOnly> holidayDates,
        int defaultBleedMinutes,
        TimeZoneInfo timeZone)
    {
        var projected = nominalDates.Select(nominal =>
        {
            var effective = AdjustForHoliday(
                activity.HolidayPolicy,
                activity.HolidayTargetWeekday,
                nominal,
                holidayDates);
            return Project(
                nominal,
                effective,
                activity.DeadlineMinutes,
                activity.UrgencyMode,
                activity.WarningMinutes,
                activity.BleedOverrideMinutes ?? defaultBleedMinutes,
                activity.ShowAheadHours,
                timeZone,
                AdjustmentExplanation(activity, nominal, effective, holidayDates),
                false);
        }).ToList();

        var collisions = projected
            .Where(item => item.EffectiveDate is not null)
            .GroupBy(item => item.EffectiveDate)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        return projected
            .Select(item => item with { Collides = item.EffectiveDate is not null && collisions.Contains(item.EffectiveDate) })
            .ToList();
    }

    public ScheduleProjectionItem ProjectOneOff(
        OneOffTask task,
        int defaultBleedMinutes,
        TimeZoneInfo timeZone)
    {
        var date = DateOnly.Parse(task.ScheduledDate, CultureInfo.InvariantCulture);
        return Project(
            date,
            date,
            task.DeadlineMinutes,
            task.UrgencyMode,
            task.WarningMinutes,
            task.BleedOverrideMinutes ?? defaultBleedMinutes,
            task.ShowAheadHours,
            timeZone,
            string.Empty,
            task.IsPermanent);
    }

    public ScheduleExplanation Explain(Activity activity, int defaultBleedMinutes) => new(
        RecurrenceSentence(activity),
        HolidaySentence(activity),
        ShowEarlySentence(activity.ShowAheadHours),
        BleedSentence(activity.BleedOverrideMinutes, defaultBleedMinutes),
        UrgencySentence(activity.DeadlineMinutes, activity.UrgencyMode, activity.WarningMinutes));

    public ScheduleExplanation Explain(OneOffTask task, int defaultBleedMinutes) => new(
        task.IsPermanent
            ? $"This task starts on {FormatDate(DateOnly.Parse(task.ScheduledDate, CultureInfo.InvariantCulture))} and stays on the board until completed."
            : $"This task is scheduled once, on {FormatDate(DateOnly.Parse(task.ScheduledDate, CultureInfo.InvariantCulture))}.",
        string.Empty,
        ShowEarlySentence(task.ShowAheadHours),
        task.IsPermanent
            ? "If unfinished, it carries into each following day until completed."
            : BleedSentence(task.BleedOverrideMinutes, defaultBleedMinutes),
        UrgencySentence(task.DeadlineMinutes, task.UrgencyMode, task.WarningMinutes));

    public static DateOnly? AdjustForHoliday(
        HolidayPolicy policy,
        int? targetWeekday,
        DateOnly nominalDate,
        IReadOnlySet<DateOnly> holidays)
    {
        if (policy == HolidayPolicy.Keep || !holidays.Contains(nominalDate))
        {
            return nominalDate;
        }

        if (policy == HolidayPolicy.Suppress)
        {
            return null;
        }

        if (policy is HolidayPolicy.MoveToPreviousWeekday or HolidayPolicy.MoveToNextWeekday)
        {
            if (targetWeekday is not (>= 0 and <= 6))
            {
                throw new InvalidOperationException("A target weekday is required for this holiday rule.");
            }

            var direction = policy == HolidayPolicy.MoveToPreviousWeekday ? -1 : 1;
            var candidate = nominalDate;
            do
            {
                candidate = candidate.AddDays(direction);
            }
            while ((int)candidate.DayOfWeek != targetWeekday.Value);
            return candidate;
        }

        var dayDirection = policy == HolidayPolicy.MoveEarlier ? -1 : 1;
        var dayCandidate = nominalDate;
        for (var attempt = 0; attempt < 14; attempt++)
        {
            dayCandidate = dayCandidate.AddDays(dayDirection);
            if (!holidays.Contains(dayCandidate))
            {
                return dayCandidate;
            }
        }

        throw new InvalidOperationException($"Could not move the occurrence on {nominalDate:yyyy-MM-dd} away from a holiday within 14 days.");
    }

    private static ScheduleProjectionItem Project(
        DateOnly nominalDate,
        DateOnly? effectiveDate,
        int? deadlineMinutes,
        UrgencyMode urgencyMode,
        int warningMinutes,
        int bleedMinutes,
        int showAheadHours,
        TimeZoneInfo timeZone,
        string adjustmentExplanation,
        bool isPermanent)
    {
        if (effectiveDate is null)
        {
            return new(nominalDate, null, null, null, null, null, null, false, adjustmentExplanation, isPermanent);
        }

        var dayStart = LocalTimeResolver.Resolve(effectiveDate.Value, TimeOnly.MinValue, timeZone);
        var dayEnd = LocalTimeResolver.Resolve(effectiveDate.Value.AddDays(1), TimeOnly.MinValue, timeZone);
        DateTimeOffset? deadline = deadlineMinutes is null
            ? null
            : LocalTimeResolver.Resolve(effectiveDate.Value, TimeOnly.MinValue.AddMinutes(deadlineMinutes.Value), timeZone);
        var urgentFrom = urgencyMode switch
        {
            UrgencyMode.AfterDeadline => deadline,
            UrgencyMode.BeforeAndAfterDeadline when deadline is not null => deadline.Value.AddMinutes(-warningMinutes),
            _ => null,
        };
        return new(
            nominalDate,
            effectiveDate,
            dayStart.AddHours(-showAheadHours),
            deadline,
            urgentFrom,
            dayEnd,
            dayEnd.AddMinutes(bleedMinutes),
            false,
            adjustmentExplanation,
            isPermanent);
    }

    private static string RecurrenceSentence(Activity activity)
    {
        var title = string.IsNullOrWhiteSpace(activity.Title) ? "This activity" : activity.Title.Trim();
        return activity.RecurrenceKind switch
        {
            RecurrenceKind.Daily => $"{title} runs every day.",
            RecurrenceKind.SelectedWeekdays => $"{title} runs every {JoinWeekdays(activity.DaysOfWeekMask)}.",
            RecurrenceKind.EveryNDays => $"{title} runs every {activity.Interval} day{Plural(activity.Interval)} from {FormatDate(DateOnly.Parse(activity.StartDate, CultureInfo.InvariantCulture))}.",
            RecurrenceKind.EveryNWeeks => $"{title} runs every {activity.Interval} week{Plural(activity.Interval)} on {JoinWeekdays(activity.DaysOfWeekMask)}.",
            RecurrenceKind.MonthlyDate => $"{title} runs on day {activity.DayOfMonth} of each month.",
            RecurrenceKind.MonthlyOrdinalWeekday => $"{title} runs on the {Ordinal(activity.Ordinal)} {Weekday(activity.Weekday)} of each month.",
            _ => $"{title} has a recurring schedule.",
        };
    }

    private static string HolidaySentence(Activity activity) => activity.HolidayPolicy switch
    {
        HolidayPolicy.Suppress => "If an occurrence falls on a public holiday, it is not generated.",
        HolidayPolicy.MoveEarlier => "If an occurrence falls on a public holiday, it moves to the previous non-holiday date.",
        HolidayPolicy.MoveLater => "If an occurrence falls on a public holiday, it moves to the next non-holiday date.",
        HolidayPolicy.MoveToPreviousWeekday => $"If an occurrence falls on a public holiday, it moves to the previous {Weekday(activity.HolidayTargetWeekday)}.",
        HolidayPolicy.MoveToNextWeekday => $"If an occurrence falls on a public holiday, it moves to the next {Weekday(activity.HolidayTargetWeekday)}.",
        _ => "Public holidays do not change its date.",
    };

    private static string ShowEarlySentence(int hours) => hours == 0
        ? "It appears at midnight at the start of its effective date."
        : $"It appears {hours} hour{Plural(hours)} before its effective date begins and can be completed from then. This does not change its effective date, deadline, or expiry time.";

    private static string BleedSentence(int? overrideMinutes, int defaultMinutes)
    {
        var minutes = overrideMinutes ?? defaultMinutes;
        var source = overrideMinutes is null ? "the household default" : "its override";
        return minutes == 0
            ? $"It uses {source} of no bleed: if unfinished, it expires at midnight after its effective date."
            : $"It uses {source} of {Duration(minutes)}: if unfinished, it remains available after midnight for that long, then expires.";
    }

    private static string UrgencySentence(int? deadlineMinutes, UrgencyMode mode, int warningMinutes)
    {
        if (deadlineMinutes is null)
        {
            return "It has no deadline, so urgency cannot activate.";
        }

        var time = TimeOnly.MinValue.AddMinutes(deadlineMinutes.Value).ToString("HH:mm", CultureInfo.InvariantCulture);
        return mode switch
        {
            UrgencyMode.AfterDeadline => $"Its deadline is {time}; it becomes urgent when that deadline is reached and remains urgent until completed or expired.",
            UrgencyMode.BeforeAndAfterDeadline => $"Its deadline is {time}; it becomes urgent {Duration(warningMinutes)} before the deadline and remains urgent until completed or expired.",
            _ => $"Its deadline is {time}; it can become overdue, but urgency highlighting and animation are never used.",
        };
    }

    private static string AdjustmentExplanation(
        Activity activity,
        DateOnly nominal,
        DateOnly? effective,
        IReadOnlySet<DateOnly> holidayDates)
    {
        if (!holidayDates.Contains(nominal) || activity.HolidayPolicy == HolidayPolicy.Keep)
        {
            return string.Empty;
        }

        if (effective is null)
        {
            return $"{FormatDate(nominal)} is a public holiday, so this occurrence is not generated.";
        }

        var targetWarning = holidayDates.Contains(effective.Value)
            ? " The target date is also marked as a public holiday."
            : string.Empty;
        return $"{FormatDate(nominal)} is a public holiday, so it moves to {FormatDate(effective.Value)}.{targetWarning}";
    }

    private static string JoinWeekdays(int mask)
    {
        var days = Enum.GetValues<DayOfWeek>()
            .Where(day => (mask & RecurrenceCalculator.DayMask(day)) != 0)
            .Select(day => day.ToString())
            .ToList();
        return days.Count switch
        {
            0 => "no selected weekdays",
            1 => days[0],
            2 => $"{days[0]} and {days[1]}",
            _ => $"{string.Join(", ", days[..^1])}, and {days[^1]}",
        };
    }

    private static string Duration(int minutes)
    {
        if (minutes == 0) return "0 minutes";
        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return $"{hours} hour{Plural(hours)}";
        }
        return $"{minutes} minute{Plural(minutes)}";
    }

    private static string Weekday(int? value) => value is >= 0 and <= 6
        ? ((DayOfWeek)value.Value).ToString()
        : "selected weekday";

    private static string Ordinal(int? value) => value switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "last",
        _ => "selected",
    };

    private static string Plural(int value) => value == 1 ? string.Empty : "s";
    private static string FormatDate(DateOnly date) => date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
}
