namespace Daybreak.Domain;

public static class RecurrenceCalculator
{
    public static bool OccursOn(Activity activity, DateOnly date)
    {
        var start = DateOnly.Parse(activity.StartDate);
        if (date < start || (activity.EndDate is not null && date > DateOnly.Parse(activity.EndDate)))
        {
            return false;
        }

        var daysSinceStart = date.DayNumber - start.DayNumber;
        var interval = Math.Max(1, activity.Interval);
        return activity.RecurrenceKind switch
        {
            RecurrenceKind.Daily => true,
            RecurrenceKind.SelectedWeekdays => HasWeekday(activity.DaysOfWeekMask, date.DayOfWeek),
            RecurrenceKind.EveryNDays => daysSinceStart % interval == 0,
            RecurrenceKind.EveryNWeeks =>
                (daysSinceStart / 7) % interval == 0 &&
                HasWeekday(activity.DaysOfWeekMask == 0 ? DayMask(start.DayOfWeek) : activity.DaysOfWeekMask, date.DayOfWeek),
            RecurrenceKind.MonthlyDate => activity.DayOfMonth is not null && date.Day == activity.DayOfMonth,
            RecurrenceKind.MonthlyOrdinalWeekday => IsOrdinalWeekday(date, activity.Ordinal, activity.Weekday),
            _ => false,
        };
    }

    public static IReadOnlyList<DateOnly> Preview(Activity activity, DateOnly from, int count, int maximumDays = 730)
    {
        var results = new List<DateOnly>(count);
        for (var offset = 0; offset < maximumDays && results.Count < count; offset++)
        {
            var candidate = from.AddDays(offset);
            if (OccursOn(activity, candidate))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    public static int DayMask(DayOfWeek day) => 1 << (int)day;

    private static bool HasWeekday(int mask, DayOfWeek day) => (mask & DayMask(day)) != 0;

    private static bool IsOrdinalWeekday(DateOnly date, int? ordinal, int? weekday)
    {
        if (ordinal is null || weekday is null || weekday < 0 || weekday > 6)
        {
            return false;
        }

        if ((int)date.DayOfWeek != weekday)
        {
            return false;
        }

        if (ordinal == 5)
        {
            return date.AddDays(7).Month != date.Month;
        }

        return ordinal is >= 1 and <= 4 && ((date.Day - 1) / 7) + 1 == ordinal;
    }
}
