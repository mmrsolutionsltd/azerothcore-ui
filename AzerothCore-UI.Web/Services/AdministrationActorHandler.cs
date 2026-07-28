using System.Security.Claims;

namespace AzerothCore_UI.Web.Services;

public sealed class AdministrationActorHandler(IHttpContextAccessor contextAccessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var principal = contextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            request.Headers.TryAddWithoutValidation(
                "X-AzerothCore-Actor",
                principal.Identity.Name ?? "authenticated-user");
            request.Headers.TryAddWithoutValidation(
                "X-AzerothCore-Role",
                principal.FindFirstValue(ClaimTypes.Role) ?? "unknown");
            request.Headers.TryAddWithoutValidation(
                "X-AzerothCore-Actor-Id",
                principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
            request.Headers.TryAddWithoutValidation(
                "X-AzerothCore-Remote-Address",
                contextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "");
        }
        return base.SendAsync(request, cancellationToken);
    }
}
