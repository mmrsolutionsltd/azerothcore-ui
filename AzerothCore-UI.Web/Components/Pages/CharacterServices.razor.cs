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
        new("spells", "Reset spells", "Rebuilds the character's learned spells using AzerothCore's reset command.", "Reset spells", true),
        new("revive", "Revive", "Resurrects an online or offline dead character.", "Revive character"),
        new("unstuck", "Return to home inn", "Moves an online or offline character to their bound inn.", "Move to inn"),
        new("level", "Set level", "Changes the character level and lets AzerothCore update level-dependent data.", "Set level", true)
    ];
    private IReadOnlyList<AdministrationPlayer> players = [];
    private IEnumerable<AdministrationPlayer> OrderedPlayers => players.OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private ServerStatus? status;
    private string playerName = "";
    private string? pendingService, message;
    private int newLevel = 80;
    private bool isLoading = true, isWorking, operationSucceeded;
    private bool CanApply => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;

    protected override async Task OnInitializedAsync()
    {
        try { status = await AccountsClient.GetServerStatusAsync(); players = await AccountsClient.GetAdministrationPlayersAsync(); }
        catch (Exception exception) { message = exception.Message; }
        finally { isLoading = false; }
    }
    private void SelectService(string service) => pendingService = service;
    private void CancelService() => pendingService = null;
    private static string ServiceTitle(string key) => Services.First(option => option.Key == key).Title;
    private async Task ApplyServiceAsync()
    {
        if (pendingService is null || isWorking) return;
        isWorking = true;
        try
        {
            var result = await AccountsClient.ApplyCharacterServiceAsync(new(playerName, pendingService,
                pendingService == "level" ? newLevel : null, true));
            operationSucceeded = result?.Success == true; message = result?.Message;
            if (operationSucceeded) pendingService = null;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }
}
