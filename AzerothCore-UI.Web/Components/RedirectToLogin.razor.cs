namespace AzerothCore_UI.Web.Components;

public partial class RedirectToLogin
{
    protected override void OnInitialized() =>
        Navigation.NavigateTo("/admin/login", forceLoad: true);
}
