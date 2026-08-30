using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class PlayerActions
{
    private IReadOnlyList<AdministrationPlayer> players = [];
    private readonly HashSet<string> selectedNames =
        new(StringComparer.OrdinalIgnoreCase);
    private bool isLoading = true;
    private string? errorMessage;

    private IReadOnlyList<PlayerActionTarget> SelectedTargets => players
        .Where(player => selectedNames.Contains(player.Name))
        .OrderBy(player => player.PickerOrder)
        .ThenBy(player => player.Name)
        .Select(PlayerActionTarget.From)
        .ToArray();

    protected override async Task OnInitializedAsync()
    {
        SelectedCharacterStore.SelectedCharactersChanged +=
            OnSelectedCharactersChanged;
        try
        {
            selectedNames.UnionWith(
                await SelectedCharacterStore.GetSelectedAsync());
            players = await AccountsClient.GetAdministrationPlayersAsync();
        }
        catch (Exception exception)
        {
            errorMessage = $"Characters could not be loaded: {exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void OnSelectedCharactersChanged(IReadOnlyList<string> names) =>
        _ = InvokeAsync(() =>
        {
            selectedNames.Clear();
            selectedNames.UnionWith(names);
            StateHasChanged();
        });

    public void Dispose() =>
        SelectedCharacterStore.SelectedCharactersChanged -=
            OnSelectedCharactersChanged;
}
