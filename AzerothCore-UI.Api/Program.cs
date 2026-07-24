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
builder.Services.AddWindowsService(options =>
    options.ServiceName = "AzerothCore UI API");
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.AzerothCoreConnectionFactory>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.AdministrationAccountStore>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Security.AdministrationPasswordHasher>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.SpellMetadataProvider>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreSoapClient>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreServerManager>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreConfigurationManager>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreDiagnosticsService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.DatabaseBackupService>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.DatabaseBackupScheduler>();
builder.Services.AddHostedService<AzerothCore_UI.Api.Services.DatabaseBackupWorker>();
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
    var started = Stopwatch.GetTimestamp();
    await next();
    if (context.Request.Path.StartsWithSegments("/api")
        && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS")
        && !context.Request.Path.StartsWithSegments(
            "/api/administration-users/validate-session"))
    {
        var actor = context.Request.Headers["X-AzerothCore-Actor"].ToString();
        var role = context.Request.Headers["X-AzerothCore-Role"].ToString();
        app.Logger.LogWarning(
            "ADMIN AUDIT: {Method} {Path} by {Actor} ({Role}) returned {StatusCode} in {ElapsedMs:F1} ms. TraceId: {TraceId}",
            context.Request.Method, context.Request.Path,
            string.IsNullOrWhiteSpace(actor) ? "web-service" : actor,
            string.IsNullOrWhiteSpace(role) ? "unknown" : role,
            context.Response.StatusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            context.TraceIdentifier);
    }
});

app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();
