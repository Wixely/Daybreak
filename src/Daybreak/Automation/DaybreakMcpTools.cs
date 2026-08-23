using System.ComponentModel;
using Daybreak.Domain;
using ModelContextProtocol.Server;

namespace Daybreak.Automation;

[McpServerToolType]
public sealed class DaybreakMcpTools
{
    [McpServerTool(Name = "get_board", UseStructuredContent = true)]
    [Description("Gets the authoritative current Daybreak board and revision.")]
    public static Task<BoardSnapshot> GetBoardAsync(AgentOperations operations, CancellationToken cancellationToken) =>
        operations.GetBoardAsync(cancellationToken);

    [McpServerTool(Name = "complete_occurrence", UseStructuredContent = true)]
    [Description("Completes an occurrence using its current concurrency version and returns the authoritative board.")]
    public static Task<OccurrenceCommandResult> CompleteOccurrenceAsync(
        AgentOperations operations,
        [Description("Occurrence identifier from get_board.")] string id,
        [Description("Current occurrence version from get_board.")] long expectedVersion,
        CancellationToken cancellationToken) =>
        operations.CompleteAsync(id, expectedVersion, cancellationToken);

    [McpServerTool(Name = "undo_occurrence", UseStructuredContent = true)]
    [Description("Undoes a completed occurrence using its current concurrency version and returns the authoritative board.")]
    public static Task<OccurrenceCommandResult> UndoOccurrenceAsync(
        AgentOperations operations,
        [Description("Occurrence identifier from get_board.")] string id,
        [Description("Current occurrence version from get_board.")] long expectedVersion,
        CancellationToken cancellationToken) =>
        operations.UndoAsync(id, expectedVersion, cancellationToken);

    [McpServerTool(Name = "list_activities", UseStructuredContent = true)]
    [Description("Lists recurring activities.")]
    public static Task<IReadOnlyList<Activity>> ListActivitiesAsync(
        AgentOperations operations,
        [Description("Include archived activities.")] bool includeArchived = false) =>
        operations.ListActivitiesAsync(includeArchived);

    [McpServerTool(Name = "save_activity", UseStructuredContent = true)]
    [Description("Creates a recurring activity, or updates one when id is supplied.")]
    public static Task<SavedEntityResult> SaveActivityAsync(
        AgentOperations operations,
        [Description("Complete recurring activity definition.")] ActivityWriteRequest activity,
        [Description("Existing activity identifier, or null to create one.")] string? id = null,
        CancellationToken cancellationToken = default) =>
        operations.SaveActivityAsync(activity, id, cancellationToken);

    [McpServerTool(Name = "archive_activity")]
    [Description("Archives an existing recurring activity and removes its pending occurrences.")]
    public static async Task<string> ArchiveActivityAsync(
        AgentOperations operations,
        [Description("Activity identifier.")] string id)
    {
        await operations.ArchiveActivityAsync(id);
        return "Activity archived.";
    }

    [McpServerTool(Name = "restore_activity")]
    [Description("Restores an archived recurring activity.")]
    public static async Task<string> RestoreActivityAsync(
        AgentOperations operations,
        [Description("Activity identifier.")] string id,
        CancellationToken cancellationToken)
    {
        await operations.RestoreActivityAsync(id, cancellationToken);
        return "Activity restored.";
    }

    [McpServerTool(Name = "list_one_off_tasks", UseStructuredContent = true)]
    [Description("Lists editable one-off tasks.")]
    public static Task<IReadOnlyList<OneOffTask>> ListOneOffTasksAsync(AgentOperations operations) =>
        operations.ListOneOffTasksAsync();

    [McpServerTool(Name = "save_one_off_task", UseStructuredContent = true)]
    [Description("Creates a one-off task, or updates one when id is supplied.")]
    public static Task<SavedEntityResult> SaveOneOffTaskAsync(
        AgentOperations operations,
        [Description("Complete one-off task definition.")] OneOffTaskWriteRequest task,
        [Description("Existing one-off task identifier, or null to create one.")] string? id = null,
        CancellationToken cancellationToken = default) =>
        operations.SaveOneOffTaskAsync(task, id, cancellationToken);

    [McpServerTool(Name = "delete_one_off_task")]
    [Description("Deletes an editable pending one-off task. Completed and expired tasks remain in history.")]
    public static async Task<string> DeleteOneOffTaskAsync(
        AgentOperations operations,
        [Description("One-off task identifier.")] string id)
    {
        await operations.DeleteOneOffTaskAsync(id);
        return "One-off task deleted.";
    }

    [McpServerTool(Name = "get_settings", UseStructuredContent = true)]
    [Description("Gets household scheduling and holiday settings.")]
    public static Task<HouseholdSettings> GetSettingsAsync(AgentOperations operations) => operations.GetSettingsAsync();

    [McpServerTool(Name = "update_settings")]
    [Description("Updates household scheduling and holiday settings.")]
    public static async Task<string> UpdateSettingsAsync(
        AgentOperations operations,
        [Description("Complete household settings definition.")] HouseholdSettingsWriteRequest settings,
        CancellationToken cancellationToken)
    {
        await operations.UpdateSettingsAsync(settings, cancellationToken);
        return "Household settings updated.";
    }

    [McpServerTool(Name = "preview_schedule", UseStructuredContent = true)]
    [Description("Previews nominal and holiday-adjusted dates, including collisions, without saving.")]
    public static Task<SchedulePreviewResult> PreviewScheduleAsync(
        AgentOperations operations,
        [Description("Activity schedule to preview.")] ActivityWriteRequest activity,
        [Description("Number of dates from 1 to 32.")] int count = 8,
        CancellationToken cancellationToken = default) =>
        operations.PreviewAsync(activity, count, cancellationToken);

    [McpServerTool(Name = "get_history", UseStructuredContent = true)]
    [Description("Gets operational occurrence history, audit events, and completion summaries.")]
    public static Task<HistorySnapshot> GetHistoryAsync(
        AgentOperations operations,
        [Description("Maximum recent rows from 1 to 500.")] int recentLimit = 100) =>
        operations.GetHistoryAsync(Math.Clamp(recentLimit, 1, 500));
}
