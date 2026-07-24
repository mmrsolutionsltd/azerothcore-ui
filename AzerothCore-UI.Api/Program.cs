var builder = WebApplication.CreateBuilder(args);
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
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new AzerothCore_UI.Api.Models.AdministrationResult(
            false,
            exception?.Message ?? "An unexpected server administration error occurred."));
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

app.UseAuthorization();

app.MapControllers();

app.Run();
