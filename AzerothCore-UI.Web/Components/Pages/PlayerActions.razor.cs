using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class PlayerActions
{
    private ServerStatus? status;
    private IReadOnlyList<AdministrationPlayer> administrationPlayers = [];
    private IEnumerable<AdministrationPlayer> OrderedAdministrationPlayers => administrationPlayers
        .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private IReadOnlyList<CharacterPickerItem> AdministrationPickerItems =>
        CharacterPickerItem.FromAdministrationPlayers(administrationPlayers);
    private bool showItemPicker;
    private string? selectedItemName;
    private bool showLocationPicker;
    private string teleportMode = "place";
    private bool showNpcTeleportPicker;
    private NpcTeleportSpawn? selectedTeleportNpc;
    private bool confirmNpcTeleport, confirmNpcReturn;
    private IReadOnlyList<string> npcReturnPlayerNames = [];
    private bool isLoading = true, isWorking, operationSucceeded;
    private string? errorMessage, resultMessage;
    private string teleportLocation = "", anchorPlayer = "";
    private readonly HashSet<string> selectedActionPlayerNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BatchActionResult> batchActionResults = [];
    private IReadOnlyList<AdministrationPlayer> SelectedActionPlayers => OrderedAdministrationPlayers
        .Where(player => selectedActionPlayerNames.Contains(player.Name)).ToArray();
    private uint itemId;
    private int quantity = 1;
    private decimal playerSpeed = 1m;
    private int moneyGold, moneySilver, moneyCopper;
    private GuildBankStatus? guildBank;
    private bool isLoadingGuildBank, confirmGuildTabUnlock;
    private IReadOnlyList<UtilityNpc> utilityNpcs = [];
    private uint selectedUtilityNpcId;
    private int utilityNpcDespawnMinutes = 10;
    private bool confirmUtilityNpcSummon;
    private UtilityNpc? SelectedUtilityNpc =>
        utilityNpcs.FirstOrDefault(npc => npc.CreatureId == selectedUtilityNpcId);
    private bool CanUseSoap => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;

    protected override Task OnInitializedAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        isLoading = status is null;
        try
        {
            var availability = await AccountsClient.GetToolAvailabilityAsync()
                ?? throw new InvalidOperationException("Tool availability was not returned.");
            status = RestrictedToolStatus(availability);
            administrationPlayers = await AccountsClient.GetAdministrationPlayersAsync();
            if (utilityNpcs.Count == 0)
            {
                utilityNpcs = await AccountsClient.GetUtilityNpcsAsync();
                selectedUtilityNpcId = utilityNpcs.FirstOrDefault()?.CreatureId ?? 0;
            }
            errorMessage = null;
        }
        catch (Exception exception)
        {
            errorMessage = $"Server status refresh failed: {exception.Message}";
        }
        finally { isLoading = false; }
    }

    private static ServerStatus RestrictedToolStatus(ToolAvailability availability) => new(
        new("Worldserver", availability.WorldServerRunning, null, null, null),
        new("Authserver", false, null, null, null),
        availability.SoapConfigured,
        availability.SoapReachable,
        null,
        [],
        new(0, 0, 0),
        0);

    private Task GiveItemAsync() => RunBatchAsync("Give item",
        player => AccountsClient.GiveItemAsync(new(player, itemId, quantity)));
    private Task MailItemAsync() => RunBatchAsync("Mail item",
        player => AccountsClient.MailItemAsync(new(player, itemId, quantity,
            "Server administration", "Items from the server administrator.")));
    private Task GiveMoneyAsync() => RunBatchAsync("Send money",
        player => AccountsClient.GiveMoneyAsync(new(player, moneyGold, moneySilver, moneyCopper)));
    private Task TeleportAsync() => RunBatchAsync("Teleport",
        player => AccountsClient.TeleportAsync(new(player, teleportLocation)));
    private async Task TeleportToNpcAsync()
    {
        if (selectedTeleportNpc is null) return;
        await RunBatchAsync("NPC teleport", player => AccountsClient.TeleportToNpcAsync(
            new(player, selectedTeleportNpc.SpawnId, confirmNpcTeleport)));
        npcReturnPlayerNames = batchActionResults.Where(result => result.Success)
            .Select(result => result.PlayerName).ToArray();
        if (npcReturnPlayerNames.Count > 0) confirmNpcTeleport = false;
    }

    private async Task ReturnFromNpcAsync()
    {
        await RunAsync(() => AccountsClient.ReturnPlayersAsync(
            new(npcReturnPlayerNames, confirmNpcReturn)));
        if (operationSucceeded)
        {
            npcReturnPlayerNames = [];
            confirmNpcReturn = false;
        }
    }
    private Task MoveToPlayerAsync() => RunBatchAsync("Move to anchor",
        player => AccountsClient.TeleportToPlayerAsync(new(player, anchorPlayer)));
    private Task SetPlayerSpeedAsync() => RunBatchAsync("Apply speed",
        player => AccountsClient.SetPlayerSpeedAsync(new(player, playerSpeed)));

    private void SetSelectedActionPlayers(IReadOnlySet<string> values)
    {
        selectedActionPlayerNames.Clear();
        selectedActionPlayerNames.UnionWith(values);
        batchActionResults = [];
        guildBank = null;
        confirmGuildTabUnlock = false;
        confirmUtilityNpcSummon = false;
    }

    private async Task InspectGuildBankAsync()
    {
        if (SelectedActionPlayers.Count != 1) return;
        isLoadingGuildBank = true;
        try
        {
            guildBank = await AccountsClient.GetGuildBankAsync(SelectedActionPlayers[0].Name);
            operationSucceeded = true;
            resultMessage = null;
        }
        catch (Exception exception)
        {
            guildBank = null;
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally { isLoadingGuildBank = false; }
    }

    private async Task UnlockGuildBankTabAsync()
    {
        if (guildBank is null) return;
        await RunAsync(() => AccountsClient.UnlockGuildBankTabAsync(
            new(guildBank.PlayerName, confirmGuildTabUnlock)));
        confirmGuildTabUnlock = false;
        if (operationSucceeded) await InspectGuildBankAsync();
    }

    private async Task SummonUtilityNpcAsync()
    {
        if (SelectedActionPlayers.Count != 1 || SelectedUtilityNpc is null) return;
        await RunAsync(() => AccountsClient.SummonUtilityNpcAsync(new(
            SelectedActionPlayers[0].Name, SelectedUtilityNpc.CreatureId,
            utilityNpcDespawnMinutes, confirmUtilityNpcSummon)));
        if (operationSucceeded) confirmUtilityNpcSummon = false;
    }

    private void SelectAnchorPlayer(string? value) => anchorPlayer = value ?? "";

    private async Task RunBatchAsync(string action, Func<string, Task<AdministrationResult?>> operation)
    {
        if (isWorking || SelectedActionPlayers.Count == 0) return;
        isWorking = true;
        resultMessage = null;
        var results = new List<BatchActionResult>();
        try
        {
            foreach (var player in SelectedActionPlayers)
            {
                try
                {
                    var response = await operation(player.Name);
                    results.Add(new(player.Name, response?.Success == true,
                        response?.Message ?? "No response returned."));
                }
                catch (Exception exception)
                {
                    results.Add(new(player.Name, false, exception.Message));
                }
            }
            batchActionResults = results;
            var successCount = results.Count(result => result.Success);
            operationSucceeded = successCount == results.Count;
            resultMessage = operationSucceeded
                ? $"{action} completed for all {successCount} selected characters."
                : $"{action} completed for {successCount} of {results.Count} selected characters.";
        }
        finally
        {
            isWorking = false;
            await RefreshAsync();
        }
    }

    private void OpenItemPicker() => showItemPicker = true;
    private void CloseItemPicker() => showItemPicker = false;

    private void SelectItem(AdministrationItem item)
    {
        itemId = item.ItemId;
        selectedItemName = item.Name;
        showItemPicker = false;
    }

    private void OpenLocationPicker() => showLocationPicker = true;
    private void CloseLocationPicker() => showLocationPicker = false;

    private void SelectLocation(TeleportLocation location)
    {
        teleportLocation = location.Name;
        showLocationPicker = false;
    }

    private void SetTeleportMode(string mode)
    {
        teleportMode = mode;
        confirmNpcTeleport = false;
    }

    private void OpenNpcTeleportPicker()
    {
        if (SelectedActionPlayers.Count > 0)
            showNpcTeleportPicker = true;
    }

    private void CloseNpcTeleportPicker() => showNpcTeleportPicker = false;

    private void SelectTeleportNpc(NpcTeleportSpawn npc)
    {
        selectedTeleportNpc = npc;
        confirmNpcTeleport = false;
        showNpcTeleportPicker = false;
    }

    private static string MapName(ushort mapId) => mapId switch
    {
        0 => "Eastern Kingdoms",
        1 => "Kalimdor",
        530 => "Outland",
        571 => "Northrend",
        _ => $"Map {mapId}"
    };

    private async Task RunAsync(Func<Task<AdministrationResult?>> operation)
    {
        if (isWorking) return;
        isWorking = true; resultMessage = null;
        try { var result = await operation(); operationSucceeded = result?.Success == true; resultMessage = result?.Message; }
        catch (Exception exception) { operationSucceeded = false; resultMessage = exception.Message; }
        finally
        {
            isWorking = false;
            await RefreshAsync();
        }
    }

    private static string FormatMoney(uint copper) =>
        $"{copper / 10000:N0}g {(copper / 100) % 100}s {copper % 100}c";
    private sealed record BatchActionResult(string PlayerName, bool Success, string Message);
}
