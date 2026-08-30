using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var externalConfig = builder.Configuration["ExternalConfig"];
if (!string.IsNullOrWhiteSpace(externalConfig))
    builder.Configuration.AddJsonFile(
        Path.GetFullPath(externalConfig), optional: false, reloadOnChange: true);
var apiKey = builder.Configuration["Security:ApiKey"];
if (!builder.Environment.IsDevelopment())
{
    AzerothCore_UI.Api.Security.ApiAccessPolicy.ValidateProductionKey(apiKey);
    var allowedHosts = builder.Configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
        throw new InvalidOperationException(
            "AllowedHosts must list the API host name in Production.");
}

// The Windows Event Log provider can throw AccessDenied for an unprivileged
// local admin process and mask the original API error. Console/debug logging is
// sufficient for this locally hosted administration service.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.

builder.Services.AddControllers();
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService(options =>
        options.ServiceName = "AzerothCore UI API");
else if (OperatingSystem.IsLinux())
    builder.Services.AddSystemd();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.AzerothCoreConnectionFactory>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.AdministrationAccountStore>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.CompanionLogisticsStore>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.CompanionPartySessionStore>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Security.AdministrationPasswordHasher>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Security.AdministrationRequestAuthorizer>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Security.AdministrationActivityAudit>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.SpellMetadataProvider>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Operations", client =>
    client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreSoapClient>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreServerManager>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreConfigurationManager>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.GatheringAbundanceService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreDiagnosticsService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.DatabaseBackupService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.DatabaseBackupScheduler>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.SecurityDashboardService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.OperationsAlertStore>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.OperationsEmailSender>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.OperationsDashboardService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.DungeonGuideService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.CraftingRecipeCatalog>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.CraftingUpgradeService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.RealmRosterService>();
builder.Services.AddHostedService<AzerothCore_UI.Api.Services.DatabaseBackupWorker>();
builder.Services.AddHostedService<AzerothCore_UI.Api.Services.OperationsAlertWorker>();
builder.Services.AddHealthChecks()
    .AddCheck<AzerothCore_UI.Api.Services.ApiReadinessHealthCheck>("api-readiness");
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnhandledApiException");
        logger.LogError(exception,
            "Unhandled API exception. TraceId: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new AzerothCore_UI.Api.Models.AdministrationResult(
            false,
            app.Environment.IsDevelopment()
                ? exception?.Message ?? "An unexpected server administration error occurred."
                : $"An unexpected administration error occurred. Reference: {context.TraceIdentifier}"));
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var suppliedKey = context.Request.Headers[
        AzerothCore_UI.Api.Security.ApiAccessPolicy.HeaderName].ToString();
    if (!AzerothCore_UI.Api.Security.ApiAccessPolicy.IsAuthorized(
            context.Connection.RemoteIpAddress,
            suppliedKey,
            apiKey,
            app.Environment.IsDevelopment()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "API authentication is required."
        });
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api")
        || context.Request.Method is "GET" or "HEAD" or "OPTIONS"
        || context.Request.Path.StartsWithSegments(
            "/api/administration-users/validate-session"))
    {
        await next();
        return;
    }

    var activityAudit = context.RequestServices.GetRequiredService<
        AzerothCore_UI.Api.Security.AdministrationActivityAudit>();
    var requestBody = await activityAudit.ReadRequestBodyAsync(context.Request);
    var started = Stopwatch.GetTimestamp();
    Exception? failure = null;
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        failure = exception;
        throw;
    }
    finally
    {
        var statusCode = failure is null
            ? context.Response.StatusCode
            : StatusCodes.Status500InternalServerError;
        var actor = context.Request.Headers["X-AzerothCore-Actor"].ToString();
        var role = context.Request.Headers["X-AzerothCore-Role"].ToString();
        app.Logger.LogWarning(
            "ADMIN AUDIT: {Method} {Path} by {Actor} ({Role}) returned {StatusCode} in {ElapsedMs:F1} ms. TraceId: {TraceId}",
            context.Request.Method, context.Request.Path,
            string.IsNullOrWhiteSpace(actor) ? "web-service" : actor,
            string.IsNullOrWhiteSpace(role) ? "unknown" : role,
            statusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            context.TraceIdentifier);
        var isExistingSecuritySuccess =
            context.Request.Path.StartsWithSegments("/api/administration-users")
            && statusCode is >= 200 and < 400;
        if (!isExistingSecuritySuccess)
            await activityAudit.RecordAsync(
                context,
                requestBody,
                statusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var authorizer = context.RequestServices.GetRequiredService<
            AzerothCore_UI.Api.Security.AdministrationRequestAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(context);
        if (!decision.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = decision.Message
            });
            return;
        }
    }
    await next();
});

app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();
