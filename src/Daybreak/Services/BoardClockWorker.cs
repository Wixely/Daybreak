namespace Daybreak.Services;

public sealed class BoardClockWorker(
    IServiceScopeFactory scopes,
    ILogger<BoardClockWorker> logger,
    TimeProvider clock) : BackgroundService
{
    private DateOnly? _currentDate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TickAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TickAsync(stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OccurrenceGenerator>()
                .EnsureRollingHorizonAsync(cancellationToken);
            var board = scope.ServiceProvider.GetRequiredService<BoardService>();
            var settings = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync();
            var today = Domain.LocalTimeResolver.Today(clock, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId));
            if (_currentDate is not null && today != _currentDate)
            {
                await board.BroadcastRolloverAsync(cancellationToken);
            }

            _currentDate = today;
            await board.ExpireAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The Daybreak board clock tick failed.");
        }
    }
}
