using Daybreak.Data;
using Daybreak.Domain;
using Daybreak.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daybreak.Tests;

[TestClass]
public sealed class BoardIntegrationTests
{
    private string _directory = null!;
    private DatabaseConnectionFactory _connections = null!;
    private ManualTimeProvider _clock = null!;
    private BoardChangeNotifier _changes = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daybreak-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Daybreak"] = $"Data Source={Path.Combine(_directory, "test.db")};Pooling=False",
            })
            .Build();
        _connections = new DatabaseConnectionFactory(configuration, new TestEnvironment(_directory));
        await new MigrationRunner(_connections, NullLogger<MigrationRunner>.Instance).MigrateAsync();
        _clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 7, 0, 0, TimeSpan.Zero));
        _changes = new BoardChangeNotifier(NullLogger<BoardChangeNotifier>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SimultaneousVersionedCompletionHasOneWinnerAndOneStoredEvent()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        await activities.SaveAsync(CreateDailyActivity());
        var original = (await board.GetSnapshotAsync()).Items.Single();
        var notifications = 0;
        _changes.Changed += _ =>
        {
            Interlocked.Increment(ref notifications);
            return Task.CompletedTask;
        };

        var results = await Task.WhenAll(
            board.CompleteAsync(original.Id, original.Version),
            board.CompleteAsync(original.Id, original.Version));

        Assert.AreEqual(1, results.Count(result => result));
        var updated = (await board.GetSnapshotAsync()).Items.Single();
        Assert.AreEqual(OccurrenceState.Completed, updated.State);
        Assert.AreEqual(1L, updated.Version);
        Assert.AreEqual(1, notifications);

        await using var connection = await _connections.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OccurrenceEvents WHERE EventType = 'Completed'";
        Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task MidnightBleedKeepsYesterdayActionableThenExpiresIt()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        await activities.SaveAsync(CreateDailyActivity(startDate: "2026-08-18"));
        await generator.EnsureAsync(new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 18));

        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 19, 0, 30, 0, TimeSpan.Zero));
        var duringBleed = await board.GetSnapshotAsync();
        Assert.IsTrue(duringBleed.Items.Any(item => item.EffectiveDate == new DateOnly(2026, 8, 18)));

        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 19, 1, 1, 0, TimeSpan.Zero));
        await board.ExpireAsync();
        var afterBleed = await board.GetSnapshotAsync();
        Assert.IsFalse(afterBleed.Items.Any(item => item.EffectiveDate == new DateOnly(2026, 8, 18)));
    }

    [TestMethod]
    public async Task ShowAheadMakesTomorrowActivityActionableOnPreviousDay()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        await activities.SaveAsync(CreateDailyActivity(startDate: "2026-08-20") with { ShowAheadHours = 6 });

        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 19, 16, 59, 0, TimeSpan.Zero));
        Assert.HasCount(0, (await board.GetSnapshotAsync()).Items);

        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 19, 17, 0, 0, TimeSpan.Zero));
        var earlyItem = (await board.GetSnapshotAsync()).Items.Single();
        Assert.AreEqual(new DateOnly(2026, 8, 20), earlyItem.EffectiveDate);
        Assert.IsTrue(await board.CompleteAsync(earlyItem.Id, earlyItem.Version));
    }

    [TestMethod]
    public async Task ShowAheadMakesTomorrowOneOffActionableOnPreviousDay()
    {
        var oneOffTasks = new OneOffTaskService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        await oneOffTasks.SaveAsync(CreateOneOff("Tomorrow once", null) with
        {
            ScheduledDate = "2026-08-20",
            ShowAheadHours = 6,
        });

        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 19, 16, 59, 0, TimeSpan.Zero));
        Assert.HasCount(0, (await board.GetSnapshotAsync()).Items);

        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 19, 17, 0, 0, TimeSpan.Zero));
        Assert.AreEqual("Tomorrow once", (await board.GetSnapshotAsync()).Items.Single().Title);
    }

    [TestMethod]
    public async Task OneCompletionNotificationRefreshesEverySubscribedDashboard()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        await activities.SaveAsync(CreateDailyActivity());
        var original = (await board.GetSnapshotAsync()).Items.Single();
        BoardSnapshot? firstDashboard = null;
        BoardSnapshot? secondDashboard = null;
        _changes.Changed += async _ => firstDashboard = await board.GetSnapshotAsync();
        _changes.Changed += async _ => secondDashboard = await board.GetSnapshotAsync();

        var completed = await board.CompleteAsync(original.Id, original.Version);

        Assert.IsTrue(completed);
        Assert.IsNotNull(firstDashboard);
        Assert.IsNotNull(secondDashboard);
        Assert.AreEqual(OccurrenceState.Completed, firstDashboard.Items.Single().State);
        Assert.AreEqual(OccurrenceState.Completed, secondDashboard.Items.Single().State);
        Assert.AreEqual(firstDashboard.Revision, secondDashboard.Revision);
    }

    [TestMethod]
    public async Task HistoryIncludesMonthlyTrendAndAuditEvents()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        var history = new HistoryService(_connections, new SettingsService(_connections, _changes), _clock);
        await activities.SaveAsync(CreateDailyActivity());
        var original = (await board.GetSnapshotAsync()).Items.Single();

        Assert.IsTrue(await board.CompleteAsync(original.Id, original.Version));
        var completed = (await board.GetSnapshotAsync()).Items.Single();
        Assert.IsTrue(await board.UndoAsync(completed.Id, completed.Version));
        var pendingAgain = (await board.GetSnapshotAsync()).Items.Single();
        Assert.IsTrue(await board.CompleteAsync(pendingAgain.Id, pendingAgain.Version));
        var snapshot = await history.GetAsync();

        Assert.AreEqual(1, snapshot.Total);
        Assert.AreEqual(1, snapshot.Completed);
        Assert.HasCount(1, snapshot.Months);
        Assert.AreEqual(new DateOnly(2026, 8, 1), snapshot.Months[0].MonthStart);
        Assert.AreEqual(100m, snapshot.Months[0].CompletionRate);
        Assert.HasCount(3, snapshot.Events);
        Assert.AreEqual("Completed", snapshot.Events[0].EventType);
        Assert.AreEqual(OccurrenceState.Pending, snapshot.Events[0].PreviousState);
        Assert.AreEqual(OccurrenceState.Completed, snapshot.Events[0].NewState);
        Assert.AreEqual("Undone", snapshot.Events[1].EventType);
    }

    [TestMethod]
    public async Task ArchivedActivityCanBeListedAndRestoredWithoutChangingHistory()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        var id = await activities.SaveAsync(CreateDailyActivity());
        var occurrence = (await board.GetSnapshotAsync()).Items.Single();
        Assert.IsTrue(await board.CompleteAsync(occurrence.Id, occurrence.Version));

        await activities.ArchiveAsync(id);

        Assert.HasCount(0, await activities.ListAsync());
        var archived = await activities.ListAsync(includeArchived: true);
        Assert.HasCount(1, archived);
        Assert.IsNotNull(archived[0].ArchivedAtUtc);
        Assert.AreEqual(OccurrenceState.Completed, (await board.GetSnapshotAsync()).Items.Single().State);

        await activities.RestoreAsync(id);
        await generator.EnsureRollingHorizonAsync();

        var restored = await activities.GetAsync(id);
        Assert.IsNotNull(restored);
        Assert.IsNull(restored.ArchivedAtUtc);
        Assert.AreEqual(OccurrenceState.Completed, (await board.GetSnapshotAsync()).Items.Single().State);
    }

    [TestMethod]
    public async Task PendingOneOffTaskAppearsAndCanBeRemoved()
    {
        var oneOffTasks = new OneOffTaskService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        var id = await oneOffTasks.SaveAsync(new OneOffTask(
            string.Empty, "One-time job", null, "2026-08-19", null, UrgencyMode.None,
            30, null, 0, null, null, string.Empty, string.Empty));

        var created = await board.GetSnapshotAsync();
        Assert.AreEqual("One-time job", created.Items.Single().Title);

        Assert.IsTrue(await oneOffTasks.DeletePendingAsync(id));
        var removed = await board.GetSnapshotAsync();
        Assert.HasCount(0, removed.Items);
    }

    [TestMethod]
    public async Task CompletedOneOffLeavesEditorAndRemainsInHistory()
    {
        var oneOffTasks = new OneOffTaskService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        var history = new HistoryService(_connections, new SettingsService(_connections, _changes), _clock);
        await oneOffTasks.SaveAsync(CreateOneOff("Single event", 9 * 60));
        var occurrence = (await board.GetSnapshotAsync()).Items.Single();

        Assert.IsTrue(await board.CompleteAsync(occurrence.Id, occurrence.Version));

        Assert.HasCount(0, await oneOffTasks.ListAsync());
        var historical = await history.GetAsync();
        Assert.AreEqual("Single event", historical.Recent.Single().Title);
        Assert.AreEqual(OccurrenceState.Completed, historical.Recent.Single().State);
    }

    [TestMethod]
    public async Task BoardOrdersOverdueThenUpcomingDeadlineThenNoDeadline()
    {
        var oneOffTasks = new OneOffTaskService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        await oneOffTasks.SaveAsync(CreateOneOff("No deadline", null));
        await oneOffTasks.SaveAsync(CreateOneOff("Upcoming", 9 * 60));
        await oneOffTasks.SaveAsync(CreateOneOff("Overdue", 6 * 60));

        var snapshot = await board.GetSnapshotAsync();

        CollectionAssert.AreEqual(
            new[] { "Overdue", "Upcoming", "No deadline" },
            snapshot.Items.Select(item => item.Title).ToArray());
    }

    [TestMethod]
    public async Task ActivityEditRebuildsPendingOccurrenceAndBroadcastsRevision()
    {
        var activities = new ActivityService(_connections, _changes, _clock);
        var generator = new OccurrenceGenerator(_connections, new EmptyHolidayProvider(), _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);
        var id = await activities.SaveAsync(CreateDailyActivity());
        _ = await board.GetSnapshotAsync();
        var revisions = new List<long>();
        _changes.Changed += revision =>
        {
            revisions.Add(revision);
            return Task.CompletedTask;
        };
        var activity = (await activities.GetAsync(id))!;

        await activities.SaveAsync(activity with { Title = "Updated routine", DeadlineMinutes = 10 * 60 });
        await generator.EnsureRollingHorizonAsync();
        var updated = (await board.GetSnapshotAsync()).Items.Single();

        Assert.AreEqual("Updated routine", updated.Title);
        Assert.AreEqual(10, TimeZoneInfo.ConvertTime(updated.Deadline!.Value, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).Hour);
        Assert.IsTrue(revisions.Count >= 2);
        Assert.IsTrue(revisions.SequenceEqual(revisions.OrderBy(value => value)));
    }

    [TestMethod]
    public async Task HolidayMoveLookbackKeepsCollidingOccurrencesSeparate()
    {
        var settings = new SettingsService(_connections, _changes);
        await settings.UpdateAsync("Europe/London", 120, "GB", null);
        var activities = new ActivityService(_connections, _changes, _clock);
        var activity = CreateDailyActivity(startDate: "2026-08-17") with { HolidayPolicy = HolidayPolicy.MoveLater };
        await activities.SaveAsync(activity);
        var holidays = new StaticHolidayProvider(new HashSet<DateOnly> { new(2026, 8, 17), new(2026, 8, 18) });
        var generator = new OccurrenceGenerator(_connections, holidays, _changes, _clock);
        var board = new BoardService(_connections, generator, _changes, _clock);

        var snapshot = await board.GetSnapshotAsync();

        var colliding = snapshot.Items.Where(item => item.EffectiveDate == new DateOnly(2026, 8, 19)).ToList();
        Assert.HasCount(3, colliding);
        CollectionAssert.AreEquivalent(
            new[] { new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 19) },
            colliding.Select(item => item.NominalDate).ToList());
        Assert.AreEqual(3, colliding.Select(item => item.Id).Distinct().Count());
    }

    private static Activity CreateDailyActivity(string startDate = "2026-08-19") => new(
        string.Empty, "Test routine", null, RecurrenceKind.Daily, 1, 0, null, null, null,
        startDate, null, 9 * 60, UrgencyMode.BeforeAndAfterDeadline, 30, null,
        0, HolidayPolicy.Keep, false, null, string.Empty, string.Empty);

    private static OneOffTask CreateOneOff(string title, int? deadlineMinutes) => new(
        string.Empty, title, null, "2026-08-19", deadlineMinutes, UrgencyMode.None,
        30, null, 0, null, null, string.Empty, string.Empty);

    private sealed class EmptyHolidayProvider : IHolidayProvider
    {
        public Task<IReadOnlySet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode, string? subdivisionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly>());
    }

    private sealed class StaticHolidayProvider(IReadOnlySet<DateOnly> dates) : IHolidayProvider
    {
        public Task<IReadOnlySet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode, string? subdivisionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(dates);
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
    }

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Daybreak.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
