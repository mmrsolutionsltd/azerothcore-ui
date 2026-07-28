using AzerothCore_UI.Web.Components;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var externalConfig = builder.Configuration["ExternalConfig"];
if (!string.IsNullOrWhiteSpace(externalConfig))
    builder.Configuration.AddJsonFile(
        Path.GetFullPath(externalConfig), optional: false, reloadOnChange: true);
var apiKey = builder.Configuration["Security:ApiKey"];
var dataProtectionKeysPath = builder.Configuration["Security:DataProtectionKeysPath"];
if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32)
        throw new InvalidOperationException(
            "Security:ApiKey must contain at least 32 characters in Production.");
    if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        throw new InvalidOperationException(
            "Security:DataProtectionKeysPath must be configured in Production.");
    var allowedHosts = builder.Configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
        throw new InvalidOperationException(
            "AllowedHosts must list the public host name in Production.");
}

// Add services to the container.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("AzerothCore-UI.Web");
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    dataProtection.PersistKeysToFileSystem(
        new DirectoryInfo(Path.GetFullPath(dataProtectionKeysPath)));
builder.Services.AddWindowsService(options =>
    options.ServiceName = "AzerothCore UI Web");
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AzerothCore_UI.Web.Services.AdministrationActorHandler>();
builder.Services.AddScoped<AzerothCore_UI.Web.Services.SelectedCharacterStore>();
builder.Services.AddScoped<AzerothCore_UI.Web.Services.DungeonWishlistStore>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.Cookie.Name = "AzerothCore.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/admin/login";
        options.Events.OnValidatePrincipal = async context =>
        {
            var idValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = context.Principal?.FindFirstValue("security_stamp");
            if (!ulong.TryParse(idValue, out var id) || string.IsNullOrEmpty(stamp))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
                return;
            }
            try
            {
                var client = context.HttpContext.RequestServices.GetRequiredService<
                    AzerothCore_UI.Web.Clients.AccountsApiClient>();
                if (!await client.ValidateAdministrationSessionAsync(
                        new(id, stamp)))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                }
            }
            catch
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("must_change_password", bool.FalseString)
        .Build();
    options.AddPolicy("PasswordChange", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Owner", policy => policy
        .RequireRole("Owner")
        .RequireClaim("must_change_password", bool.FalseString));
    foreach (var permission in AzerothCore_UI.Web.Security.AdministrationPermissions.All)
        options.AddPolicy(permission, policy => policy
            .RequireClaim(
                AzerothCore_UI.Web.Security.AdministrationPermissions.ClaimType,
                permission)
            .RequireClaim("must_change_password", bool.FalseString));
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("admin-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
void ConfigureApiClient(HttpClient client)
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("ApiBaseUrl is not configured.");
    client.BaseAddress = new Uri(apiBaseUrl);
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-AzerothCore-Admin-Key", apiKey);
}
builder.Services.AddHttpClient<AzerothCore_UI.Web.Clients.AccountsApiClient>(ConfigureApiClient)
    .AddHttpMessageHandler<AzerothCore_UI.Web.Services.AdministrationActorHandler>();
builder.Services.AddHttpClient("ApiHealth", ConfigureApiClient);
builder.Services.AddHealthChecks()
    .AddCheck<AzerothCore_UI.Web.Services.ApiReadinessHealthCheck>("private-api");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
        context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
        context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
        context.Response.Headers.TryAdd(
            "Permissions-Policy", "camera=(), geolocation=(), microphone=()");
        return Task.CompletedTask;
    });
    await next();
});
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.MapGet("/admin/login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    AzerothCore_UI.Web.Clients.AccountsApiClient client) =>
{
    if (!await client.HasAdministrationUsersAsync())
        return Results.Redirect("/admin/setup");
    var tokens = antiforgery.GetAndStoreTokens(context);
    var fieldName = WebUtility.HtmlEncode(tokens.FormFieldName);
    var requestToken = WebUtility.HtmlEncode(tokens.RequestToken);
    var error = context.Request.Query.ContainsKey("error")
        ? """<p role="alert" style="color:#b42318">The password was not accepted.</p>"""
        : "";
    return Results.Content($$"""
    <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
    <title>Administrator sign in</title><style>body{font:16px system-ui;max-width:28rem;margin:5rem auto;padding:1rem}input,button{font:inherit;width:100%;box-sizing:border-box;padding:.65rem;margin:.4rem 0}</style></head>
    <body><h1>Administrator sign in</h1>{{error}}<form method="post">
    <input name="{{fieldName}}" type="hidden" value="{{requestToken}}">
    <label>Username<input name="username" autocomplete="username" required autofocus></label>
    <label>Password<input name="password" type="password" autocomplete="current-password" required></label>
    <button>Sign in</button></form></body></html>
    """, "text/html");
}).AllowAnonymous();

app.MapPost("/admin/login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    AzerothCore_UI.Web.Clients.AccountsApiClient client) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var supplied = form["password"].ToString();
    var result = await client.AuthenticateAdministratorAsync(new(
        username, supplied, context.Connection.RemoteIpAddress?.ToString()));
    if (result is not { Succeeded: true, User: { } user })
        return Results.Redirect("/admin/login?error=1");

    var claims = new List<Claim> {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim("security_stamp", user.SecurityStamp),
        new Claim("must_change_password", user.MustChangePassword.ToString()),
        new Claim("account_scope", user.AccountScope)
    };
    claims.AddRange(user.Permissions.Select(permission =>
        new Claim(AzerothCore_UI.Web.Security.AdministrationPermissions.ClaimType, permission)));
    claims.AddRange(user.GameAccountIds.Select(accountId =>
        new Claim("game_account", accountId.ToString())));
    var identity = new ClaimsIdentity(claims,
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = true
        });
    return Results.Redirect(user.MustChangePassword ? "/my-security"
        : user.Permissions.Contains("server.control") ? "/server" : "/characters");
}).AllowAnonymous().RequireRateLimiting("admin-login");

app.MapGet("/admin/setup", async (
    HttpContext context,
    IAntiforgery antiforgery,
    AzerothCore_UI.Web.Clients.AccountsApiClient client) =>
{
    if (await client.HasAdministrationUsersAsync())
        return Results.Redirect("/admin/login");
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Content($$"""
    <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
    <title>Create owner account</title><style>body{font:16px system-ui;max-width:28rem;margin:5rem auto;padding:1rem}input,button{font:inherit;width:100%;box-sizing:border-box;padding:.65rem;margin:.4rem 0}</style></head>
    <body><h1>Create owner account</h1><p>This one-time page is disabled after the first account is created.</p>
    <form method="post"><input name="{{WebUtility.HtmlEncode(tokens.FormFieldName)}}" type="hidden"
    value="{{WebUtility.HtmlEncode(tokens.RequestToken)}}">
    <label>Username<input name="username" autocomplete="username" required autofocus></label>
    <label>Password<input name="password" type="password" minlength="12" autocomplete="new-password" required></label>
    <label>Confirm password<input name="confirmPassword" type="password" minlength="12" autocomplete="new-password" required></label>
    <button>Create owner</button></form></body></html>
    """, "text/html");
}).AllowAnonymous();

app.MapPost("/admin/setup", async (
    HttpContext context,
    IAntiforgery antiforgery,
    AzerothCore_UI.Web.Clients.AccountsApiClient client) =>
{
    await antiforgery.ValidateRequestAsync(context);
    if (await client.HasAdministrationUsersAsync())
        return Results.Redirect("/admin/login");
    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();
    if (password != form["confirmPassword"].ToString())
        return Results.BadRequest("Passwords do not match.");
    await client.BootstrapAdministratorAsync(new(
        form["username"].ToString(), password,
        context.Connection.RemoteIpAddress?.ToString()));
    return Results.Redirect("/admin/login");
}).AllowAnonymous().RequireRateLimiting("admin-login");

app.MapPost("/admin/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
