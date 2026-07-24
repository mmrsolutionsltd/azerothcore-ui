using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Security;

public static class AdministrationScopeExtensions
{
    public static AdministrationUserIdentity? AdministrationIdentity(this HttpContext context) =>
        context.Items.TryGetValue(typeof(AdministrationUserIdentity), out var value)
            ? value as AdministrationUserIdentity : null;
}
