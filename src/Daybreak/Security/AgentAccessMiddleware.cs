namespace Daybreak.Security;

public sealed class AgentAccessMiddleware(
    RequestDelegate next,
    ILogger<AgentAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, AgentAccessService access)
    {
        var surface = SurfaceFor(context.Request.Path);
        if (surface is null)
        {
            await next(context);
            return;
        }

        var authentication = await access.AuthenticateAsync(
            surface.Value,
            context.Request.Headers.Authorization,
            context.RequestAborted);
        if (authentication.Outcome == AgentAccessOutcome.Unavailable)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (authentication.Outcome == AgentAccessOutcome.Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            try
            {
                await access.RecordAccessAsync(
                    surface.Value,
                    authentication.CredentialSuffix,
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    context.TraceIdentifier,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not record {Surface} access audit event.", surface);
            }
        }
    }

    private static AgentSurface? SurfaceFor(PathString path)
    {
        if (path.StartsWithSegments("/api/v1"))
        {
            return AgentSurface.Api;
        }

        return path.StartsWithSegments("/mcp") ? AgentSurface.Mcp : null;
    }
}
