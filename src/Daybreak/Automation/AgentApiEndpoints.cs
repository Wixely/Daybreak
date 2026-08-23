namespace Daybreak.Automation;

public static class AgentApiEndpoints
{
    public static RouteGroupBuilder MapAgentApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet(string.Empty, () => new
        {
            name = "Daybreak API",
            version = "v1",
            endpoints = new[]
            {
                "board", "activities", "one-off-tasks", "settings", "history", "schedule-preview", "occurrences",
            },
        });
        api.MapGet("/board", (AgentOperations operations, CancellationToken cancellationToken) =>
            operations.GetBoardAsync(cancellationToken));
        api.MapGet("/activities", (bool? includeArchived, AgentOperations operations) =>
            operations.ListActivitiesAsync(includeArchived ?? false));
        api.MapPost("/activities", (ActivityWriteRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteCreatedAsync("/api/v1/activities", () => operations.SaveActivityAsync(request, cancellationToken: cancellationToken)));
        api.MapPut("/activities/{id}", (string id, ActivityWriteRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteAsync(() => operations.SaveActivityAsync(request, id, cancellationToken)));
        api.MapPost("/activities/{id}/archive", (string id, AgentOperations operations) =>
            ExecuteNoContentAsync(() => operations.ArchiveActivityAsync(id)));
        api.MapPost("/activities/{id}/restore", (string id, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteNoContentAsync(() => operations.RestoreActivityAsync(id, cancellationToken)));
        api.MapGet("/one-off-tasks", (AgentOperations operations) => operations.ListOneOffTasksAsync());
        api.MapPost("/one-off-tasks", (OneOffTaskWriteRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteCreatedAsync("/api/v1/one-off-tasks", () => operations.SaveOneOffTaskAsync(request, cancellationToken: cancellationToken)));
        api.MapPut("/one-off-tasks/{id}", (string id, OneOffTaskWriteRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteAsync(() => operations.SaveOneOffTaskAsync(request, id, cancellationToken)));
        api.MapDelete("/one-off-tasks/{id}", (string id, AgentOperations operations) =>
            ExecuteNoContentAsync(() => operations.DeleteOneOffTaskAsync(id)));
        api.MapGet("/settings", (AgentOperations operations) => operations.GetSettingsAsync());
        api.MapPut("/settings", (HouseholdSettingsWriteRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteNoContentAsync(() => operations.UpdateSettingsAsync(request, cancellationToken)));
        api.MapGet("/history", (int? recentLimit, AgentOperations operations) =>
            operations.GetHistoryAsync(Math.Clamp(recentLimit ?? 100, 1, 500)));
        api.MapPost("/schedule-preview", (ActivityWriteRequest request, int? count, AgentOperations operations, CancellationToken cancellationToken) =>
            ExecuteAsync(() => operations.PreviewAsync(request, count ?? 8, cancellationToken)));
        api.MapPost("/occurrences/{id}/complete", (string id, OccurrenceCommandRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            operations.CompleteAsync(id, request.ExpectedVersion, cancellationToken));
        api.MapPost("/occurrences/{id}/undo", (string id, OccurrenceCommandRequest request, AgentOperations operations, CancellationToken cancellationToken) =>
            operations.UndoAsync(id, request.ExpectedVersion, cancellationToken));
        return api;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ExecuteCreatedAsync(string collectionPath, Func<Task<SavedEntityResult>> operation)
    {
        try
        {
            var result = await operation();
            return Results.Created($"{collectionPath}/{result.Id}", result);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ExecuteNoContentAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return Results.NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
