using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class WeaponTraining
{
    private IReadOnlyList<AdministrationPlayer> players = [];
    private IReadOnlyList<WeaponTrainingStatus> training = [];
    private IEnumerable<AdministrationPlayer> OrderedPlayers => players
        .OrderBy(player => player.PickerOrder)
        .ThenBy(player => player.Name);
    private IReadOnlyList<CharacterPickerItem> PickerItems => OrderedPlayers
        .Select(player => new CharacterPickerItem(
            player.Name, player.Name, $"Account {player.Username}", player.Online, player.IsPlayerBot))
        .ToArray();
    private AdministrationPlayer? SelectedPlayer => players.FirstOrDefault(
        player => player.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

    private ServerStatus? status;
    private WeaponTrainingStatus? pendingTraining;
    private string playerName = "";
    private string loadedPlayerName = "";
    private string? message;
    private bool isLoadingPage = true;
    private bool isLoadingTraining;
    private bool isWorking;
    private bool operationSucceeded;

    private bool CanUseWeaponTraining => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;
    private bool CanLoad => CanUseWeaponTraining && !isLoadingTraining && SelectedPlayer?.Online == true;
    private void SelectPlayer(string? value)
    {
        playerName = value ?? "";
        training = [];
        loadedPlayerName = "";
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            status = await AccountsClient.GetServerStatusAsync();
            players = await AccountsClient.GetAdministrationPlayersAsync();
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally
        {
            isLoadingPage = false;
        }
    }

    private async Task LoadTrainingAsync()
    {
        if (!CanLoad) return;

        isLoadingTraining = true;
        message = null;
        training = [];
        pendingTraining = null;
        try
        {
            var selectedPlayer = playerName.Trim();
            training = await AccountsClient.GetWeaponTrainingAsync(selectedPlayer) ?? [];
            loadedPlayerName = selectedPlayer;
            operationSucceeded = true;
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally
        {
            isLoadingTraining = false;
        }
    }

    private void SelectTraining(WeaponTrainingStatus item) => pendingTraining = item;
    private void CancelTraining() => pendingTraining = null;

    private async Task GrantTrainingAsync()
    {
        if (pendingTraining is null || string.IsNullOrWhiteSpace(loadedPlayerName) || isWorking) return;

        isWorking = true;
        try
        {
            var result = await AccountsClient.GrantWeaponTrainingAsync(
                new GrantWeaponTrainingRequest(loadedPlayerName, pendingTraining.Key, true));
            operationSucceeded = result?.Success == true;
            message = result?.Message;
            if (operationSucceeded)
            {
                pendingTraining = null;
                training = await AccountsClient.GetWeaponTrainingAsync(loadedPlayerName) ?? [];
            }
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally
        {
            isWorking = false;
        }
    }
}
