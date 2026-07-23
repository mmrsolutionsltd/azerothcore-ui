var builder = WebApplication.CreateBuilder(args);

// The Windows Event Log provider can throw AccessDenied for an unprivileged
// local admin process and mask the original API error. Console/debug logging is
// sufficient for this locally hosted administration service.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.AzerothCoreConnectionFactory>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Data.SpellMetadataProvider>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreSoapClient>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreServerManager>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreConfigurationManager>();
builder.Services.AddSingleton<AzerothCore_UI.Api.Services.AzerothCoreDiagnosticsService>();
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

app.UseAuthorization();

app.MapControllers();

app.Run();
