using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class WeaponTraining
{
    private IReadOnlyList<AdministrationPlayer> players = [];
    private IReadOnlyList<WeaponTrainingStatus> training = [];
    private IEnumerable<AdministrationPlayer> OrderedOnlinePlayers => players
        .Where(player => player.Online)
        .OrderBy(player => player.PickerOrder)
        .ThenBy(player => player.Name);

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
    private bool CanLoad => CanUseWeaponTraining && !isLoadingTraining && !string.IsNullOrWhiteSpace(playerName);

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
