using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class ServerAdministration
{
    [Parameter] public bool PlayerActionsOnly { get; set; }
    [Parameter] public bool DungeonAssistantOnly { get; set; }
    private string PageHeading => DungeonAssistantOnly ? "Dungeon assistant" : PlayerActionsOnly ? "Player actions" : "Server administration";
    private string PageDescription => DungeonAssistantOnly ? "Build a role-aware party and launch it into a dungeon."
        : PlayerActionsOnly ? "Allowlisted actions for individual players." : "Local lifecycle controls and server configuration.";
    private ServerStatus? status;
    private PlayerBotSettings? playerBotSettings;
    private GameplayRateSettings? gameplayRates;
    private IReadOnlyList<AdministrationPlayer> administrationPlayers = [];
    private IEnumerable<AdministrationPlayer> OrderedAdministrationPlayers => administrationPlayers
        .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private AdministrationItemSearchResult itemResults = new([], 1, 30, 0, 0);
    private CancellationTokenSource? itemSearchCancellation;
    private bool showItemPicker, isLoadingItems;
    private string itemSearch = "", itemCategory = "all";
    private string? selectedItemName;
    private string partyLeader = "";
    private PartySnapshot? party;
    private bool isLoadingParty;
    private IReadOnlyList<DungeonDestination> dungeons = [];
    private string dungeonSearch = "";
    private uint selectedDungeonId;
    private bool confirmDungeonLaunch;
    private DungeonDestination? SelectedDungeon => dungeons.FirstOrDefault(dungeon => dungeon.DungeonId == selectedDungeonId);
    private IEnumerable<DungeonDestination> FilteredDungeons => dungeons
        .Where(dungeon => string.IsNullOrWhiteSpace(dungeonSearch)
            || dungeon.Name.Contains(dungeonSearch, StringComparison.OrdinalIgnoreCase))
        .Take(12);
    private bool PartyHasLevelWarning => party is not null && SelectedDungeon is not null
        && party.Members.Any(member => member.Level < SelectedDungeon.MinimumLevel || member.Level > SelectedDungeon.MaximumLevel);
    private TeleportLocationSearchResult locationResults = new([], 1, 30, 0, 0);
    private CancellationTokenSource? locationSearchCancellation;
    private bool showLocationPicker, isLoadingLocations;
    private string locationSearch = "";
    private bool isLoading = true, isWorking, forceStop, operationSucceeded;
    private string? errorMessage, resultMessage;
    private string activeView = "PlayerBots", playerName = "", teleportPlayer = "", teleportLocation = "", relativePlayer = "", anchorPlayer = "";
    private uint itemId;
    private int quantity = 1;
    private string moneyPlayer = "";
    private string speedPlayer = "";
    private decimal playerSpeed = 1m;
    private int moneyGold, moneySilver, moneyCopper;
    private bool CanUseSoap => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;
    private bool CanStop => !isWorking && (status?.WorldServer.IsRunning != true || status.SoapReachable || forceStop);
    private int PopulationPercent => status is null || status.PlayerLimit <= 0 ? 0
        : Math.Min(100, (int)Math.Round(status.Population.Total * 100d / status.PlayerLimit));

    protected override Task OnInitializedAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        isLoading = status is null;
        try
        {
            status = await AccountsClient.GetServerStatusAsync();
            if (PlayerActionsOnly || DungeonAssistantOnly)
            {
                if (administrationPlayers.Count == 0)
                    administrationPlayers = await AccountsClient.GetAdministrationPlayersAsync();
            }
            else
            {
                playerBotSettings ??= await AccountsClient.GetPlayerBotSettingsAsync();
                gameplayRates ??= await AccountsClient.GetGameplayRateSettingsAsync();
            }
            errorMessage = null;
        }
        catch (Exception exception)
        {
            errorMessage = $"Server status refresh failed: {exception.Message}";
        }
        finally { isLoading = false; }
    }

    private Task StartAsync() => RunAsync(() => AccountsClient.StartServersAsync());
    private Task StopAsync() => RunAsync(() => AccountsClient.StopServersAsync(forceStop));
    private Task RestartAsync() => RunAsync(() => AccountsClient.RestartServersAsync(forceStop));
    private Task GiveItemAsync() => RunAsync(() => AccountsClient.GiveItemAsync(new(playerName, itemId, quantity)));
    private Task MailItemAsync() => RunAsync(() => AccountsClient.MailItemAsync(new(playerName, itemId, quantity, "Server administration", "Items from the server administrator.")));
    private Task GiveMoneyAsync() => RunAsync(() => AccountsClient.GiveMoneyAsync(
        new(moneyPlayer, moneyGold, moneySilver, moneyCopper)));
    private Task TeleportAsync() => RunAsync(() => AccountsClient.TeleportAsync(new(teleportPlayer, teleportLocation)));
    private Task MoveToPlayerAsync() => RunAsync(() => AccountsClient.TeleportToPlayerAsync(new(relativePlayer, anchorPlayer)));
    private Task SetPlayerSpeedAsync() => RunAsync(() => AccountsClient.SetPlayerSpeedAsync(new(speedPlayer, playerSpeed)));

    private async Task LoadPartyAsync()
    {
        if (string.IsNullOrWhiteSpace(partyLeader)) return;
        isLoadingParty = true;
        try
        {
            party = await AccountsClient.GetPartyAsync(partyLeader);
            if (dungeons.Count == 0) dungeons = await AccountsClient.GetDungeonsAsync();
            operationSucceeded = true;
            resultMessage = null;
        }
        catch (Exception exception) { operationSucceeded = false; resultMessage = exception.Message; }
        finally { isLoadingParty = false; }
    }

    private Task AddPartyBotAsync(string botName) => RunPartyOperationAsync(
        () => AccountsClient.AddPartyBotAsync(new(partyLeader, botName)));
    private Task RemovePartyBotAsync(string botName) => RunPartyOperationAsync(
        () => AccountsClient.RemovePartyBotAsync(new(partyLeader, botName)));
    private Task ClearPartyBotsAsync() => RunPartyOperationAsync(
        () => AccountsClient.ClearPartyBotsAsync(new(partyLeader)));
    private Task FillPartyWithBotsAsync() => RunPartyOperationAsync(
        () => AccountsClient.FillPartyWithBotsAsync(new(partyLeader)));
    private void SelectDungeon(uint dungeonId)
    {
        selectedDungeonId = dungeonId;
        confirmDungeonLaunch = false;
    }
    private async Task LaunchPartyAsync()
    {
        await RunPartyOperationAsync(
            () => AccountsClient.LaunchPartyAsync(new(partyLeader, selectedDungeonId, confirmDungeonLaunch)));
        if (operationSucceeded) confirmDungeonLaunch = false;
    }

    private async Task RunPartyOperationAsync(Func<Task<AdministrationResult?>> operation)
    {
        await RunAsync(operation);
        if (operationSucceeded) await LoadPartyAsync();
    }

    private static string CharacterClassName(int characterClass) => characterClass switch
    {
        1 => "Warrior", 2 => "Paladin", 3 => "Hunter", 4 => "Rogue", 5 => "Priest",
        6 => "Death Knight", 7 => "Shaman", 8 => "Mage", 9 => "Warlock", 11 => "Druid", _ => "Unknown"
    };

    private async Task OpenItemPickerAsync()
    {
        showItemPicker = true;
        await LoadItemsAsync(1);
    }

    private async Task OnItemSearchAsync(ChangeEventArgs args)
    {
        itemSearch = args.Value?.ToString() ?? "";
        itemSearchCancellation?.Cancel();
        itemSearchCancellation?.Dispose();
        itemSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, itemSearchCancellation.Token);
            await LoadItemsAsync(1, itemSearchCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private Task OnItemCategoryChangedAsync() => LoadItemsAsync(1);

    private async Task LoadItemsAsync(int page, CancellationToken cancellationToken = default)
    {
        isLoadingItems = true;
        try { itemResults = await AccountsClient.GetAdministrationItemsAsync(itemSearch, itemCategory, page, cancellationToken); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException exception) { operationSucceeded = false; resultMessage = exception.Message; }
        finally { isLoadingItems = false; }
    }

    private void SelectItem(AdministrationItem item)
    {
        itemId = item.ItemId;
        selectedItemName = item.Name;
        showItemPicker = false;
    }

    private async Task OpenLocationPickerAsync()
    {
        showLocationPicker = true;
        await LoadLocationsAsync(1);
    }

    private async Task OnLocationSearchAsync(ChangeEventArgs args)
    {
        locationSearch = args.Value?.ToString() ?? "";
        locationSearchCancellation?.Cancel();
        locationSearchCancellation?.Dispose();
        locationSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, locationSearchCancellation.Token);
            await LoadLocationsAsync(1, locationSearchCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadLocationsAsync(int page, CancellationToken cancellationToken = default)
    {
        isLoadingLocations = true;
        try { locationResults = await AccountsClient.GetTeleportLocationsAsync(locationSearch, page, cancellationToken); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException exception) { operationSucceeded = false; resultMessage = exception.Message; }
        finally { isLoadingLocations = false; }
    }

    private void SelectLocation(TeleportLocation location)
    {
        teleportLocation = location.Name;
        showLocationPicker = false;
    }

    private static string MapName(ushort mapId) => mapId switch
    {
        0 => "Eastern Kingdoms", 1 => "Kalimdor", 530 => "Outland", 571 => "Northrend",
        _ => $"Map {mapId}"
    };

    private static string ItemQualityName(byte quality) => quality switch
    {
        0 => "Poor", 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Epic",
        5 => "Legendary", 6 => "Artifact", 7 => "Heirloom", _ => "Unknown"
    };

    private static string ItemQualityClass(byte quality) => quality switch
    {
        0 => "text-secondary", 2 => "text-success", 3 => "text-primary", 4 => "text-purple",
        5 => "text-warning", 6 => "text-danger", 7 => "text-warning", _ => ""
    };

    private async Task SavePlayerBotSettingsAsync()
    {
        if (playerBotSettings is null) return;
        isWorking = true;
        try
        {
            playerBotSettings = await AccountsClient.UpdatePlayerBotSettingsAsync(playerBotSettings);
            operationSucceeded = true;
            resultMessage = "PlayerBots settings saved. Restart the worldserver to apply them.";
        }
        catch (Exception exception) { operationSucceeded = false; resultMessage = exception.Message; }
        finally { isWorking = false; }
    }

    private async Task SaveGameplayRatesAsync()
    {
        if (gameplayRates is null) return;
        isWorking = true;
        try
        {
            gameplayRates = await AccountsClient.UpdateGameplayRateSettingsAsync(gameplayRates);
            operationSucceeded = true;
            resultMessage = "Gameplay rates saved. Restart the worldserver to apply them.";
        }
        catch (Exception exception) { operationSucceeded = false; resultMessage = exception.Message; }
        finally { isWorking = false; }
    }

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

    private static string FormatBytes(long? bytes) => bytes.HasValue ? $"{bytes.Value / 1024d / 1024d:0.0} MB" : "—";
}
