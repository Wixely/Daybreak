using Dapper;
using Daybreak.Data;
using Daybreak.Domain;

namespace Daybreak.Services;

public sealed class BoardService(
    DatabaseConnectionFactory connections,
    OccurrenceGenerator generator,
    BoardChangeNotifier changes,
    TimeProvider clock)
{
    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await generator.EnsureRollingHorizonAsync(cancellationToken);
        await ExpireAsync(cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var settings = await connection.QuerySingleAsync<HouseholdSettings>("SELECT * FROM HouseholdSettings WHERE Id = 1");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var today = LocalTimeResolver.Today(clock, timeZone);
        var now = clock.GetUtcNow();
        var todayStart = LocalTimeResolver.Resolve(today, TimeOnly.MinValue, timeZone);
        var occurrences = await connection.QueryAsync<Occurrence>("""
            SELECT * FROM Occurrences
            WHERE State != @Expired
              AND (VisibleFromUtc IS NULL OR VisibleFromUtc <= @Now)
              AND (
                  ActionWindowEndUtc > @Now
                  OR (IsPermanent = 1 AND State = @Pending)
                  OR (IsPermanent = 1 AND State = @Completed AND CompletedAtUtc >= @TodayStart))
            """, new
        {
            Expired = OccurrenceState.Expired,
            Pending = OccurrenceState.Pending,
            Completed = OccurrenceState.Completed,
            Now = now.ToString("O"),
            TodayStart = todayStart.ToString("O"),
        });

        var items = occurrences
            .Select(Map)
            .OrderBy(item => SortBucket(item, now))
            .ThenBy(item => item.Deadline ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new BoardSnapshot(today, settings.TimeZoneId, settings.BoardRevision, items);
    }

    public Task<bool> CompleteAsync(string id, long expectedVersion, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, expectedVersion, OccurrenceState.Pending, OccurrenceState.Completed, "Completed", cancellationToken);

    public Task<bool> UndoAsync(string id, long expectedVersion, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, expectedVersion, OccurrenceState.Completed, OccurrenceState.Pending, "Undone", cancellationToken);

    public async Task BroadcastRolloverAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var revision = await connection.ExecuteScalarAsync<long>(
            "UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1 RETURNING BoardRevision");
        await changes.NotifyAsync(revision);
    }

    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().ToString("O");
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var ids = (await connection.QueryAsync<string>("""
            SELECT Id FROM Occurrences
            WHERE State = @Pending AND IsPermanent = 0 AND ActionWindowEndUtc <= @Now
            """, new { Pending = OccurrenceState.Pending, Now = now }, transaction)).AsList();
        if (ids.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        await connection.ExecuteAsync("""
            INSERT INTO OccurrenceEvents (OccurrenceId, EventType, OccurredAtUtc, PreviousState, NewState)
            SELECT Id, 'Expired', @Now, @Pending, @Expired
            FROM Occurrences
            WHERE Id IN @Ids AND State = @Pending;

            UPDATE Occurrences
            SET State = @Expired, Version = Version + 1
            WHERE Id IN @Ids AND State = @Pending;
            """, new
        {
            Ids = ids,
            Now = now,
            Pending = OccurrenceState.Pending,
            Expired = OccurrenceState.Expired,
        }, transaction);
        var revision = await IncrementRevisionAsync(connection, transaction);
        await transaction.CommitAsync(cancellationToken);
        await changes.NotifyAsync(revision);
        return ids.Count;
    }

    private async Task<bool> TransitionAsync(
        string id,
        long expectedVersion,
        OccurrenceState from,
        OccurrenceState to,
        string eventType,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var settings = await connection.QuerySingleAsync<HouseholdSettings>(
            "SELECT * FROM HouseholdSettings WHERE Id = 1", transaction: transaction);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var today = LocalTimeResolver.Today(clock, timeZone);
        var todayStart = LocalTimeResolver.Resolve(today, TimeOnly.MinValue, timeZone).ToString("O");
        var affected = await connection.ExecuteAsync("""
            UPDATE Occurrences
            SET State = @To,
                CompletedAtUtc = CASE WHEN @To = @Completed THEN @Now ELSE NULL END,
                CompletedBy = NULL,
                Version = Version + 1
            WHERE Id = @Id
              AND State = @From
              AND Version = @ExpectedVersion
              AND (VisibleFromUtc IS NULL OR VisibleFromUtc <= @Now)
              AND (
                  ActionWindowEndUtc > @Now
                  OR (IsPermanent = 1 AND @From = @Pending)
                  OR (IsPermanent = 1 AND @From = @Completed AND CompletedAtUtc >= @TodayStart));
            """, new
        {
            Id = id,
            From = from,
            To = to,
            Completed = OccurrenceState.Completed,
            Pending = OccurrenceState.Pending,
            Now = now.ToString("O"),
            TodayStart = todayStart,
            ExpectedVersion = expectedVersion,
        }, transaction);

        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await connection.ExecuteAsync("""
            INSERT INTO OccurrenceEvents (
                OccurrenceId, EventType, OccurredAtUtc, PreviousState, NewState, Actor)
            VALUES (@Id, @EventType, @Now, @From, @To, NULL);
            """, new { Id = id, EventType = eventType, Now = now.ToString("O"), From = from, To = to }, transaction);
        var revision = await IncrementRevisionAsync(connection, transaction);
        await transaction.CommitAsync(cancellationToken);
        await changes.NotifyAsync(revision);
        return true;
    }

    private static BoardItem Map(Occurrence occurrence) => new(
        occurrence.Id,
        occurrence.TitleSnapshot,
        occurrence.NotesSnapshot,
        occurrence.ScheduleLabelSnapshot,
        DateOnly.Parse(occurrence.NominalDate),
        DateOnly.Parse(occurrence.EffectiveDate),
        occurrence.DeadlineUtc is null ? null : DateTimeOffset.Parse(occurrence.DeadlineUtc),
        DateTimeOffset.Parse(occurrence.ActionWindowEndUtc),
        occurrence.UrgencyMode,
        occurrence.WarningMinutes,
        occurrence.State,
        occurrence.CompletedAtUtc is null ? null : DateTimeOffset.Parse(occurrence.CompletedAtUtc),
        occurrence.Version);

    private static int SortBucket(BoardItem item, DateTimeOffset now) => item.State switch
    {
        OccurrenceState.Pending when item.IsOverdue(now) => 0,
        OccurrenceState.Pending when item.Deadline is not null => 1,
        OccurrenceState.Pending => 2,
        _ => 3,
    };

    private static Task<long> IncrementRevisionAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction) =>
        connection.ExecuteScalarAsync<long>(
            "UPDATE HouseholdSettings SET BoardRevision = BoardRevision + 1 WHERE Id = 1 RETURNING BoardRevision",
            transaction: transaction);
}
