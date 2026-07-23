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
    private IEnumerable<AdministrationPlayer> OrderedPlayers => players
        .Where(player => !player.IsPlayerBot)
        .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private ServerStatus? status;
    private readonly HashSet<string> selectedPlayerNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BatchServiceResult> batchResults = [];
    private string? pendingService, message;
    private int newLevel = 80;
    private bool isLoading = true, isWorking, operationSucceeded;
    private bool CanApply => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;
    private bool CanSelectService(ServiceOption service) =>
        CanApply && selectedPlayerNames.Count > 0
        && (service.Key != "spells" || SelectedPlayers.All(player => player.Online))
        && (service.Key != "level" || newLevel is >= 1 and <= 80);
    private IReadOnlyList<AdministrationPlayer> SelectedPlayers => OrderedPlayers
        .Where(player => selectedPlayerNames.Contains(player.Name)).ToArray();

    protected override async Task OnInitializedAsync()
    {
        try { status = await AccountsClient.GetServerStatusAsync(); players = await AccountsClient.GetAdministrationPlayersAsync(); }
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

    private void TogglePlayer(string name, bool selected)
    {
        if (selected) selectedPlayerNames.Add(name);
        else selectedPlayerNames.Remove(name);
        batchResults = [];
    }

    private void SelectAllRealPlayers(bool selected)
    {
        selectedPlayerNames.Clear();
        if (selected)
            foreach (var player in OrderedPlayers) selectedPlayerNames.Add(player.Name);
        batchResults = [];
    }

    private sealed record BatchServiceResult(string PlayerName, bool Success, string Message);
}
