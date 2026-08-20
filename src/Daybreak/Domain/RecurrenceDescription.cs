namespace Daybreak.Domain;

public static class RecurrenceDescription
{
    public const string OneOff = "One-off";

    public static string ForActivity(Activity activity) => activity.RecurrenceKind switch
    {
        RecurrenceKind.Daily => "Daily",
        RecurrenceKind.SelectedWeekdays => "Selected weekdays",
        RecurrenceKind.EveryNDays when activity.Interval == 1 => "Daily",
        RecurrenceKind.EveryNDays when activity.Interval == 7 => "Weekly",
        RecurrenceKind.EveryNDays when activity.Interval is >= 28 and <= 31 => "Roughly monthly",
        RecurrenceKind.EveryNDays => $"Every {activity.Interval} days",
        RecurrenceKind.EveryNWeeks when activity.Interval == 1 => "Weekly",
        RecurrenceKind.EveryNWeeks when activity.Interval == 4 => "Roughly monthly",
        RecurrenceKind.EveryNWeeks => $"Every {activity.Interval} weeks",
        RecurrenceKind.MonthlyDate or RecurrenceKind.MonthlyOrdinalWeekday => "Monthly",
        _ => "Recurring",
    };
}
