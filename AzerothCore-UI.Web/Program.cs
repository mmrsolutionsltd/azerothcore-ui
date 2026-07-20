using AzerothCore_UI.Web.Components;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.Cookie.Name = "AzerothCore.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<AzerothCore_UI.Web.Clients.AccountsApiClient>(client =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("ApiBaseUrl is not configured.");
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapGet("/admin/login", () => Results.Content("""
    <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
    <title>Administrator sign in</title><style>body{font:16px system-ui;max-width:28rem;margin:5rem auto;padding:1rem}input,button{font:inherit;width:100%;box-sizing:border-box;padding:.65rem;margin:.4rem 0}</style></head>
    <body><h1>Administrator sign in</h1><form method="post"><label>Password<input name="password" type="password" required autofocus></label><button>Sign in</button></form></body></html>
    """, "text/html"));

app.MapPost("/admin/login", async (HttpContext context, IConfiguration configuration) =>
{
    var form = await context.Request.ReadFormAsync();
    var supplied = form["password"].ToString();
    var expected = configuration["Administration:Password"];
    if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
        return Results.Unauthorized();

    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Administrator")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));
    return Results.Redirect("/server");
}).DisableAntiforgery();

app.MapPost("/admin/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
