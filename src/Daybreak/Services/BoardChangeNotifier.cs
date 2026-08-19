namespace Daybreak.Services;

public sealed class BoardChangeNotifier(ILogger<BoardChangeNotifier> logger)
{
    public event Func<long, Task>? Changed;

    public async Task NotifyAsync(long revision)
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<long, Task>>())
        {
            try
            {
                await handler(revision);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "A disconnected dashboard could not receive board revision {Revision}.", revision);
            }
        }
    }
}
