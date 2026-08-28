namespace Daybreak.Data;

public sealed record DatabaseMigration(int Version, string Name, string Sql);

public static class SchemaManifest
{
    public const int CurrentVersion = 6;

    public static IReadOnlyList<DatabaseMigration> Migrations { get; } =
    [
        new(1, "Initial schema", """
            CREATE TABLE HouseholdSettings (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                TimeZoneId TEXT NOT NULL,
                DefaultBleedMinutes INTEGER NOT NULL CHECK (DefaultBleedMinutes BETWEEN 0 AND 720),
                HolidayCountryCode TEXT NULL,
                HolidaySubdivisionCode TEXT NULL,
                BoardRevision INTEGER NOT NULL DEFAULT 0
            );

            INSERT INTO HouseholdSettings (Id, TimeZoneId, DefaultBleedMinutes, BoardRevision)
            VALUES (1, 'Europe/London', 120, 0);

            CREATE TABLE Activities (
                Id TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Notes TEXT NULL,
                RecurrenceKind INTEGER NOT NULL,
                Interval INTEGER NOT NULL DEFAULT 1 CHECK (Interval BETWEEN 1 AND 365),
                DaysOfWeekMask INTEGER NOT NULL DEFAULT 0,
                DayOfMonth INTEGER NULL,
                Ordinal INTEGER NULL,
                Weekday INTEGER NULL,
                StartDate TEXT NOT NULL,
                EndDate TEXT NULL,
                DeadlineMinutes INTEGER NULL CHECK (DeadlineMinutes IS NULL OR DeadlineMinutes BETWEEN 0 AND 1439),
                UrgencyMode INTEGER NOT NULL DEFAULT 0,
                WarningMinutes INTEGER NOT NULL DEFAULT 30 CHECK (WarningMinutes BETWEEN 0 AND 1440),
                BleedOverrideMinutes INTEGER NULL CHECK (BleedOverrideMinutes IS NULL OR BleedOverrideMinutes BETWEEN 0 AND 720),
                HolidayPolicy INTEGER NOT NULL DEFAULT 0,
                IsPaused INTEGER NOT NULL DEFAULT 0,
                ArchivedAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE OneOffTasks (
                Id TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Notes TEXT NULL,
                ScheduledDate TEXT NOT NULL,
                DeadlineMinutes INTEGER NULL CHECK (DeadlineMinutes IS NULL OR DeadlineMinutes BETWEEN 0 AND 1439),
                UrgencyMode INTEGER NOT NULL DEFAULT 0,
                WarningMinutes INTEGER NOT NULL DEFAULT 30 CHECK (WarningMinutes BETWEEN 0 AND 1440),
                BleedOverrideMinutes INTEGER NULL CHECK (BleedOverrideMinutes IS NULL OR BleedOverrideMinutes BETWEEN 0 AND 720),
                SourceKind TEXT NULL,
                SourceReference TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE Occurrences (
                Id TEXT NOT NULL PRIMARY KEY,
                ActivityId TEXT NULL REFERENCES Activities(Id),
                OneOffTaskId TEXT NULL REFERENCES OneOffTasks(Id),
                TitleSnapshot TEXT NOT NULL,
                NotesSnapshot TEXT NULL,
                NominalDate TEXT NOT NULL,
                EffectiveDate TEXT NOT NULL,
                DeadlineUtc TEXT NULL,
                ActionWindowEndUtc TEXT NOT NULL,
                UrgencyMode INTEGER NOT NULL,
                WarningMinutes INTEGER NOT NULL,
                State INTEGER NOT NULL DEFAULT 0,
                CompletedAtUtc TEXT NULL,
                CompletedBy TEXT NULL,
                Version INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                CHECK ((ActivityId IS NOT NULL AND OneOffTaskId IS NULL) OR (ActivityId IS NULL AND OneOffTaskId IS NOT NULL))
            );

            CREATE UNIQUE INDEX UX_Occurrences_Activity_NominalDate
                ON Occurrences (ActivityId, NominalDate) WHERE ActivityId IS NOT NULL;
            CREATE UNIQUE INDEX UX_Occurrences_OneOffTask
                ON Occurrences (OneOffTaskId) WHERE OneOffTaskId IS NOT NULL;
            CREATE INDEX IX_Occurrences_EffectiveDate_State ON Occurrences (EffectiveDate, State);

            CREATE TABLE OccurrenceEvents (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                OccurrenceId TEXT NOT NULL REFERENCES Occurrences(Id),
                EventType TEXT NOT NULL,
                OccurredAtUtc TEXT NOT NULL,
                PreviousState INTEGER NULL,
                NewState INTEGER NOT NULL,
                Actor TEXT NULL,
                Details TEXT NULL
            );

            CREATE INDEX IX_OccurrenceEvents_OccurrenceId ON OccurrenceEvents (OccurrenceId, Id);

            """),
        new(2, "Snapshot recurrence labels", """
            ALTER TABLE Occurrences
            ADD COLUMN ScheduleLabelSnapshot TEXT NOT NULL DEFAULT 'Recurring';

            UPDATE Occurrences
            SET ScheduleLabelSnapshot = CASE
                WHEN OneOffTaskId IS NOT NULL THEN 'One-off'
                WHEN ActivityId IS NULL THEN 'Recurring'
                ELSE COALESCE((
                    SELECT CASE
                        WHEN RecurrenceKind = 0 THEN 'Daily'
                        WHEN RecurrenceKind = 1 THEN 'Selected weekdays'
                        WHEN RecurrenceKind = 2 AND Interval = 1 THEN 'Daily'
                        WHEN RecurrenceKind = 2 AND Interval = 7 THEN 'Weekly'
                        WHEN RecurrenceKind = 2 AND Interval BETWEEN 28 AND 31 THEN 'Roughly monthly'
                        WHEN RecurrenceKind = 2 THEN 'Every ' || Interval || ' days'
                        WHEN RecurrenceKind = 3 AND Interval = 1 THEN 'Weekly'
                        WHEN RecurrenceKind = 3 AND Interval = 4 THEN 'Roughly monthly'
                        WHEN RecurrenceKind = 3 THEN 'Every ' || Interval || ' weeks'
                        WHEN RecurrenceKind IN (4, 5) THEN 'Monthly'
                        ELSE 'Recurring'
                    END
                    FROM Activities
                    WHERE Activities.Id = Occurrences.ActivityId
                ), 'Recurring')
            END;
            """),
        new(3, "Schedule early dashboard visibility", """
            ALTER TABLE Activities
            ADD COLUMN ShowAheadHours INTEGER NOT NULL DEFAULT 0 CHECK (ShowAheadHours BETWEEN 0 AND 168);

            ALTER TABLE OneOffTasks
            ADD COLUMN ShowAheadHours INTEGER NOT NULL DEFAULT 0 CHECK (ShowAheadHours BETWEEN 0 AND 168);

            ALTER TABLE Occurrences
            ADD COLUMN VisibleFromUtc TEXT NULL;

            CREATE INDEX IX_Occurrences_VisibleFromUtc_State
                ON Occurrences (VisibleFromUtc, State);
            """),
        new(4, "Agent API and MCP access", """
            ALTER TABLE HouseholdSettings
            ADD COLUMN ApiEnabled INTEGER NOT NULL DEFAULT 0 CHECK (ApiEnabled IN (0, 1));

            ALTER TABLE HouseholdSettings
            ADD COLUMN McpEnabled INTEGER NOT NULL DEFAULT 0 CHECK (McpEnabled IN (0, 1));

            CREATE TABLE AgentCredentials (
                Kind TEXT NOT NULL PRIMARY KEY CHECK (Kind IN ('Api', 'Mcp')),
                SecretHash TEXT NOT NULL,
                Suffix TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE AgentAccessEvents (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Surface TEXT NOT NULL,
                CredentialSuffix TEXT NULL,
                Method TEXT NOT NULL,
                Path TEXT NOT NULL,
                StatusCode INTEGER NOT NULL,
                CorrelationId TEXT NOT NULL,
                OccurredAtUtc TEXT NOT NULL
            );

            CREATE INDEX IX_AgentAccessEvents_OccurredAtUtc
                ON AgentAccessEvents (OccurredAtUtc DESC);
            """),
        new(5, "Explainable weekday holiday adjustments", """
            ALTER TABLE Activities
            ADD COLUMN HolidayTargetWeekday INTEGER NULL CHECK (HolidayTargetWeekday IS NULL OR HolidayTargetWeekday BETWEEN 0 AND 6);

            ALTER TABLE Occurrences
            ADD COLUMN AdjustmentDescriptionSnapshot TEXT NULL;

            UPDATE Occurrences
            SET AdjustmentDescriptionSnapshot =
                'This occurrence moved from ' || NominalDate || ' to ' || EffectiveDate || ' under its holiday rule.'
            WHERE NominalDate <> EffectiveDate;
            """),
        new(6, "Permanent one-off tasks", """
            ALTER TABLE OneOffTasks
            ADD COLUMN IsPermanent INTEGER NOT NULL DEFAULT 0 CHECK (IsPermanent IN (0, 1));

            ALTER TABLE Occurrences
            ADD COLUMN IsPermanent INTEGER NOT NULL DEFAULT 0 CHECK (IsPermanent IN (0, 1));
            """),
    ];
}
