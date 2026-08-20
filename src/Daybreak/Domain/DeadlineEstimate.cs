namespace Daybreak.Domain;

public static class DeadlineEstimate
{
    public static string Format(DateTimeOffset now, DateTimeOffset? deadline)
    {
        if (deadline is null)
        {
            return "No deadline";
        }

        var remaining = deadline.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return $"{FormatDuration(-remaining)} ago";
        }

        return FormatDuration(remaining);
    }

    private static string FormatDuration(TimeSpan remaining)
    {
        if (remaining.TotalMinutes < 1)
        {
            return "Less than a minute";
        }

        if (remaining.TotalMinutes < 2)
        {
            return "About a minute";
        }

        if (remaining.TotalMinutes < 5)
        {
            return $"About {Math.Round(remaining.TotalMinutes):0} minutes";
        }

        if (remaining.TotalMinutes < 45)
        {
            var minutes = Math.Max(5, Math.Round(remaining.TotalMinutes / 5) * 5);
            return $"About {minutes:0} minutes";
        }

        if (remaining.TotalMinutes < 90)
        {
            return "About an hour";
        }

        if (remaining.TotalHours < 36)
        {
            var hours = Math.Round(remaining.TotalHours);
            return $"About {hours:0} hours";
        }

        var days = Math.Round(remaining.TotalDays);
        return days == 1 ? "About a day" : $"About {days:0} days";
    }
}
