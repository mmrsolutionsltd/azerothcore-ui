using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class CharacterServices
{
    private sealed record ServiceOption(string Key, string Title, string Description, string Button, bool Dangerous = false);
    private static readonly ServiceOption[] Services =
    [
        new("rename", "Force rename", "Requires the character to choose a new name at next login.", "Require rename"),
        new("customize", "Appearance customization", "Opens normal appearance customization at next login.", "Enable customization"),
        new("race", "Race change", "Enables the normal race-change screen at next login.", "Enable race change", true),
        new("faction", "Faction change", "Enables the normal faction-change screen at next login.", "Enable faction change", true),
        new("talents", "Reset talents", "Resets character talents and all pet talents.", "Reset talents", true),
        new("spells", "Reset spells", "Rebuilds an online character's learned spells using AzerothCore's reset command.", "Reset spells", true),
        new("revive", "Revive", "Resurrects an online or offline dead character.", "Revive character"),
        new("unstuck", "Return to home inn", "Moves an online or offline character to their bound inn.", "Move to inn"),
        new("level", "Set level", "Changes the character level and lets AzerothCore update level-dependent data.", "Set level", true)
    ];
    private IReadOnlyList<AdministrationPlayer> players = [];
    private IReadOnlyList<CharacterTransferAccount> transferAccounts = [];
    private IEnumerable<AdministrationPlayer> OrderedPlayers => players
        .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private ServerStatus? status;
    private readonly HashSet<string> selectedPlayerNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BatchServiceResult> batchResults = [];
    private string? pendingService, message;
    private uint? destinationAccountId;
    private int newLevel = 80;
    private bool isLoading = true, isWorking, operationSucceeded,
        pendingAccountTransfer;
    private bool CanApply => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;
    private bool CanSelectService(ServiceOption service) =>
        CanApply && selectedPlayerNames.Count > 0
        && (service.Key != "spells" || SelectedPlayers.All(player => player.Online))
        && (service.Key != "level" || newLevel is >= 1 and <= 80);
    private IReadOnlyList<AdministrationPlayer> SelectedPlayers => OrderedPlayers
        .Where(player => selectedPlayerNames.Contains(player.Name)).ToArray();
    private AdministrationPlayer? SelectedTransferPlayer =>
        SelectedPlayers.Count == 1 ? SelectedPlayers[0] : null;
    private CharacterTransferAccount? SelectedDestinationAccount =>
        transferAccounts.FirstOrDefault(account =>
            account.AccountId == destinationAccountId);
    private bool CanTransferAccount =>
        CanApply
        && SelectedTransferPlayer is not null
        && SelectedDestinationAccount is { CharacterCount: < 10 } destination
        && !SelectedTransferPlayer.Username.Equals(
            destination.Username, StringComparison.OrdinalIgnoreCase);
    private IReadOnlyList<CharacterPickerItem> PickerItems =>
        CharacterPickerItem.FromAdministrationPlayers(players);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var statusTask = AccountsClient.GetServerStatusAsync();
            var playersTask = AccountsClient.GetAdministrationPlayersAsync();
            var accountsTask = AccountsClient.GetCharacterTransferAccountsAsync();
            await Task.WhenAll(statusTask, playersTask, accountsTask);
            status = await statusTask;
            players = await playersTask;
            transferAccounts = await accountsTask;
        }
        catch (Exception exception) { message = exception.Message; }
        finally { isLoading = false; }
    }
    private void SelectService(string service)
    {
        message = null;
        batchResults = [];
        pendingService = service;
    }
    private void CancelService() => pendingService = null;
    private void SelectAccountTransfer()
    {
        if (!CanTransferAccount) return;
        message = null;
        batchResults = [];
        pendingAccountTransfer = true;
    }
    private void CancelAccountTransfer()
    {
        if (!isWorking) pendingAccountTransfer = false;
    }
    private static string ServiceTitle(string key) => Services.First(option => option.Key == key).Title;
    private async Task ApplyServiceAsync()
    {
        if (pendingService is null || isWorking) return;
        isWorking = true;
        try
        {
            var results = new List<BatchServiceResult>();
            foreach (var player in SelectedPlayers)
            {
                try
                {
                    var result = await AccountsClient.ApplyCharacterServiceAsync(new(player.Name, pendingService,
                        pendingService == "level" ? newLevel : null, true));
                    results.Add(new(player.Name, result?.Success == true, result?.Message ?? "No response returned."));
                }
                catch (Exception exception)
                {
                    results.Add(new(player.Name, false, exception.Message));
                }
            }
            batchResults = results;
            var successCount = results.Count(result => result.Success);
            operationSucceeded = successCount == results.Count;
            message = operationSucceeded
                ? $"{ServiceTitle(pendingService)} completed for all {successCount} selected characters."
                : $"{successCount} of {results.Count} character services completed. Review the results below.";
            pendingService = null;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }

    private void SetSelectedPlayers(IReadOnlySet<string> values)
    {
        selectedPlayerNames.Clear();
        selectedPlayerNames.UnionWith(values);
        batchResults = [];
    }

    private async Task TransferAccountAsync()
    {
        var player = SelectedTransferPlayer;
        var destination = SelectedDestinationAccount;
        if (player is null || destination is null || isWorking) return;
        isWorking = true;
        try
        {
            var result = await AccountsClient.TransferCharacterAccountAsync(
                new(player.Name, destination.AccountId, true));
            operationSucceeded = result?.Success == true;
            message = result?.Message ?? "No response returned.";
            if (operationSucceeded)
            {
                players = await AccountsClient.GetAdministrationPlayersAsync();
                transferAccounts =
                    await AccountsClient.GetCharacterTransferAccountsAsync();
                destinationAccountId = null;
                pendingAccountTransfer = false;
            }
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally { isWorking = false; }
    }

    private sealed record BatchServiceResult(string PlayerName, bool Success, string Message);
}
