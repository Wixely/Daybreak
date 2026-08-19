namespace Daybreak.Domain;

public static class DeadlineCountdown
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public static double? Progress(DateTimeOffset now, DateTimeOffset? deadline)
    {
        if (deadline is null || now < deadline.Value - Window)
        {
            return null;
        }

        var elapsed = 1 - ((deadline.Value - now).TotalSeconds / Window.TotalSeconds);
        return Math.Clamp(elapsed, 0, 1);
    }
}
