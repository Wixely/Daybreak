using Daybreak.Automation;
using Daybreak.Components;
using Daybreak.Data;
using Daybreak.Domain;
using Daybreak.Security;
using Daybreak.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<AgentFeatureOptions>(builder.Configuration.GetSection(AgentFeatureOptions.SectionName));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "daybreak.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.LoginPath = "/admin/login";
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<AdminPasswordValidator>();
            if (!validator.IsCurrent(context.Principal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("sqlite");
builder.Services.AddDataProtection()
    .SetApplicationName("Daybreak")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(
        builder.Configuration["Daybreak:DataProtectionKeysPath"] ?? "App_Data/keys",
        builder.Environment.ContentRootPath)));
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("admin-login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AdminPasswordValidator>();
builder.Services.AddSingleton<BoardChangeNotifier>();
builder.Services.AddSingleton<ScheduleProjector>();
builder.Services.AddSingleton<DatabaseConnectionFactory>();
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<OneOffTaskService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddScoped<OccurrenceGenerator>();
builder.Services.AddScoped<AgentAccessService>();
builder.Services.AddScoped<AgentOperations>();
builder.Services.AddScoped<IHolidayProvider, NagerDateHolidayProvider>();
builder.Services.AddHostedService<BoardClockWorker>();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<DaybreakMcpTools>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Daybreak encountered an unexpected error.");
    }));
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();
app.UseMiddleware<AgentAccessMiddleware>();

await app.Services.GetRequiredService<MigrationRunner>().MigrateAsync();
if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    return;
}

_ = app.Services.GetRequiredService<AdminPasswordValidator>();
await using (var startupScope = app.Services.CreateAsyncScope())
{
    await startupScope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

app.MapPost("/auth/login", async (HttpContext context, IAntiforgery antiforgery, AdminPasswordValidator passwords) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();
    var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());

    if (!passwords.IsValid(password))
    {
        return Results.LocalRedirect($"/admin/login?invalid=true&returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    await context.SignInAsync(passwords.CreatePrincipal());
    return Results.LocalRedirect(returnUrl);
}).RequireRateLimiting("admin-login");

app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await context.SignOutAsync();
    return Results.LocalRedirect("/");
}).RequireAuthorization();

app.MapHealthChecks("/health");
app.MapAgentApi();
app.MapMcp("/mcp");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

static string NormalizeReturnUrl(string? value)
{
    if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
    {
        return "/admin";
    }

    return value;
}

public partial class Program;
