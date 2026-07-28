using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class ServerAdministration
{
    private const int MaximumRandomBotCount = 5000;
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
    private IReadOnlyList<CharacterPickerItem> AdministrationPickerItems => OrderedAdministrationPlayers
        .Select(player => new CharacterPickerItem(
            player.Name, player.Name, $"Account {player.Username}", player.Online, player.IsPlayerBot))
        .ToArray();
    private AdministrationItemSearchResult itemResults = new([], 1, 30, 0, 0);
    private CancellationTokenSource? itemSearchCancellation;
    private bool showItemPicker, isLoadingItems;
    private string itemSearch = "", itemCategory = "all";
    private int? itemQuality, minimumItemLevel, maximumItemLevel;
    private int? minimumRequiredLevel, maximumRequiredLevel;
    private string? selectedItemName;
    private string partyLeader = "";
    private PartySnapshot? party;
    private bool isLoadingParty;
    private IReadOnlyList<DungeonDestination> dungeons = [];
    private string dungeonSearch = "";
    private uint selectedDungeonId;
    private DungeonReadiness? dungeonReadiness;
    private DungeonGuide? dungeonGuide;
    private bool isLoadingReadiness;
    private bool showPartyLootOnly = true;
    private bool confirmDungeonLaunch;
    private readonly HashSet<uint> confirmedQuestTeleports = [];
    private IReadOnlyList<string> questReturnPlayerNames = [];
    private bool confirmQuestReturn;
    private DungeonDestination? SelectedDungeon => dungeons.FirstOrDefault(dungeon => dungeon.DungeonId == selectedDungeonId);
    private IEnumerable<DungeonDestination> FilteredDungeons => dungeons
        .Where(dungeon => string.IsNullOrWhiteSpace(dungeonSearch)
            || dungeon.Name.Contains(dungeonSearch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(dungeon => DungeonRecommendationScore(dungeon))
        .ThenBy(dungeon => dungeon.MinimumLevel)
        .Take(12);
    private IReadOnlySet<uint> RecommendedDungeonIds => party is null
        ? new HashSet<uint>()
        : dungeons.OrderBy(DungeonRecommendationScore).Take(3)
            .Select(dungeon => dungeon.DungeonId).ToHashSet();
    private bool PartyHasLevelWarning => party is not null && SelectedDungeon is not null
        && party.Members.Any(member => member.Level < SelectedDungeon.MinimumLevel || member.Level > SelectedDungeon.MaximumLevel);
    private TeleportLocationSearchResult locationResults = new([], 1, 30, 0, 0);
    private CancellationTokenSource? locationSearchCancellation;
    private bool showLocationPicker, isLoadingLocations;
    private string locationSearch = "";
    private string teleportMode = "place";
    private NpcTeleportSearchResult npcTeleportResults = new([], 1, 30, 0, 0);
    private CancellationTokenSource? npcTeleportSearchCancellation;
    private bool showNpcTeleportPicker, isLoadingNpcTeleports;
    private string npcTeleportSearch = "";
    private NpcTeleportSpawn? selectedTeleportNpc;
    private bool confirmNpcTeleport, confirmNpcReturn;
    private IReadOnlyList<string> npcReturnPlayerNames = [];
    private bool isLoading = true, isWorking, forceStop, operationSucceeded;
    private string? errorMessage, resultMessage;
    private string activeView = "PlayerBots", teleportLocation = "", anchorPlayer = "";
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
    private bool CanStop => !isWorking && (status?.WorldServer.IsRunning != true || status.SoapReachable || forceStop);
    private int PopulationPercent => status is null || status.PlayerLimit <= 0 ? 0
        : Math.Min(100, (int)Math.Round(status.Population.Total * 100d / status.PlayerLimit));
    private string? PlayerBotValidationMessage => playerBotSettings switch
    {
        { MinRandomBots: < 0 or > MaximumRandomBotCount } or { MaxRandomBots: < 0 or > MaximumRandomBotCount }
            => $"Bot counts must be between 0 and {MaximumRandomBotCount:N0}.",
        { } settings when settings.MinRandomBots > settings.MaxRandomBots
            => "Minimum random bots cannot exceed maximum random bots.",
        _ => null
    };
    private bool PlayerBotSettingsValid => PlayerBotValidationMessage is null;

    protected override Task OnInitializedAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        isLoading = status is null;
        try
        {
            if (PlayerActionsOnly || DungeonAssistantOnly)
            {
                var availability = await AccountsClient.GetToolAvailabilityAsync()
                    ?? throw new InvalidOperationException("Tool availability was not returned.");
                status = RestrictedToolStatus(availability);
                administrationPlayers = await AccountsClient.GetAdministrationPlayersAsync();
                if (PlayerActionsOnly && utilityNpcs.Count == 0)
                {
                    utilityNpcs = await AccountsClient.GetUtilityNpcsAsync();
                    selectedUtilityNpcId = utilityNpcs.FirstOrDefault()?.CreatureId ?? 0;
                }
            }
            else
            {
                status = await AccountsClient.GetServerStatusAsync();
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

    private static ServerStatus RestrictedToolStatus(ToolAvailability availability) => new(
        new("Worldserver", availability.WorldServerRunning, null, null, null),
        new("Authserver", false, null, null, null),
        availability.SoapConfigured,
        availability.SoapReachable,
        null,
        [],
        new(0, 0, 0),
        0);

    private Task StartAsync() => RunAsync(() => AccountsClient.StartServersAsync());
    private Task StopAsync() => RunAsync(() => AccountsClient.StopServersAsync(forceStop));
    private Task RestartAsync() => RunAsync(() => AccountsClient.RestartServersAsync(forceStop));
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

    private void SelectPartyLeader(string? value)
    {
        partyLeader = value ?? "";
        party = null;
        selectedDungeonId = 0;
        dungeonReadiness = null;
        confirmedQuestTeleports.Clear();
    }

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

    private async Task LoadPartyAsync()
    {
        if (string.IsNullOrWhiteSpace(partyLeader)) return;
        isLoadingParty = true;
        try
        {
            party = await AccountsClient.GetPartyAsync(partyLeader);
            if (dungeons.Count == 0) dungeons = await AccountsClient.GetDungeonsAsync();
            dungeonReadiness = null;
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
    private async Task SelectDungeonAsync(uint dungeonId)
    {
        selectedDungeonId = dungeonId;
        confirmDungeonLaunch = false;
        confirmedQuestTeleports.Clear();
        dungeonReadiness = null;
        dungeonGuide = null;
        isLoadingReadiness = true;
        try
        {
            dungeonReadiness = await AccountsClient.GetDungeonReadinessAsync(partyLeader, dungeonId);
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = $"Readiness check failed: {exception.Message}";
        }
        try
        {
            var libraryCharacters =
                await AccountsClient.GetDungeonLibraryCharactersAsync();
            var partyNames = party?.Members.Select(member => member.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dungeonGuide = await AccountsClient.GetDungeonLibraryGuideAsync(new(
                dungeonId,
                libraryCharacters.Where(character => partyNames.Contains(character.Name))
                    .Select(character => character.Guid).ToArray()));
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = $"Dungeon guide failed: {exception.Message}";
        }
        finally { isLoadingReadiness = false; }
    }
    private async Task LaunchPartyAsync()
    {
        await RunPartyOperationAsync(
            () => AccountsClient.LaunchPartyAsync(new(partyLeader, selectedDungeonId, confirmDungeonLaunch)));
        if (operationSucceeded) confirmDungeonLaunch = false;
    }

    private void SetQuestTeleportConfirmation(uint questId, bool confirmed)
    {
        if (confirmed) confirmedQuestTeleports.Add(questId);
        else confirmedQuestTeleports.Remove(questId);
    }

    private async Task TeleportToQuestGiverAsync(DungeonQuest quest)
    {
        if (quest.QuestGiver is null) return;
        var players = quest.PlayerStatuses
            .Where(status => status.CanTeleport)
            .Select(status => status.PlayerName)
            .ToArray();
        await TeleportPlayersToQuestGiverAsync(quest.QuestId, quest.QuestGiver, players);
    }

    private async Task TeleportToPrerequisiteGiverAsync(DungeonQuest quest)
    {
        if (quest.Prerequisite?.QuestGiver is null) return;
        var players = quest.PlayerStatuses
            .Where(status => status.Status == "MissingPrerequisite")
            .Select(status => status.PlayerName)
            .ToArray();
        await TeleportPlayersToQuestGiverAsync(
            quest.Prerequisite.QuestId, quest.Prerequisite.QuestGiver, players);
    }

    private async Task TeleportPlayersToQuestGiverAsync(
        uint questId, DungeonQuestGiver giver, IReadOnlyList<string> players)
    {
        await RunAsync(() => AccountsClient.TeleportToDungeonQuestGiverAsync(new(
            questId, giver.SpawnId, players, confirmedQuestTeleports.Contains(questId))));
        if (operationSucceeded)
        {
            questReturnPlayerNames = players;
            confirmQuestReturn = false;
            confirmedQuestTeleports.Remove(questId);
            await SelectDungeonAsync(selectedDungeonId);
        }
    }

    private async Task ReturnFromQuestGiverAsync()
    {
        await RunAsync(() => AccountsClient.ReturnDungeonQuestPlayersAsync(
            new(questReturnPlayerNames, confirmQuestReturn)));
        if (operationSucceeded)
        {
            questReturnPlayerNames = [];
            confirmQuestReturn = false;
        }
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

    private static string LootQualityClass(int quality) => quality switch
    {
        >= 5 => "text-warning",
        4 => "text-purple",
        3 => "text-primary",
        2 => "text-success",
        _ => ""
    };

    private static string QuestStatusLabel(string status) => status switch
    {
        "Available" => "Available",
        "InProgress" => "In progress",
        "Completed" => "Completed",
        "LevelTooLow" => "Level too low",
        "MissingPrerequisite" => "Prerequisite missing",
        "ReputationRequired" => "Reputation required",
        "ReputationTooHigh" => "Reputation restricted",
        "WrongRace" => "Wrong race",
        "WrongClass" => "Wrong class",
        "StartedByItem" => "Item-started",
        "NoNpcGiver" => "No NPC giver",
        "Offline" => "Offline",
        _ => status
    };

    private static string QuestStatusBadgeClass(string status) => status switch
    {
        "Available" => "text-bg-success",
        "InProgress" => "text-bg-info",
        "Completed" => "text-bg-secondary",
        "Offline" or "NoNpcGiver" or "StartedByItem" => "text-bg-secondary",
        _ => "text-bg-warning"
    };

    private int DungeonRecommendationScore(DungeonDestination dungeon)
    {
        if (party is null || party.Members.Count == 0) return dungeon.MinimumLevel;
        var averageLevel = party.Members.Average(member => member.Level);
        var outsideCount = party.Members.Count(member =>
            member.Level < dungeon.MinimumLevel || member.Level > dungeon.MaximumLevel);
        var midpoint = (dungeon.MinimumLevel + dungeon.MaximumLevel) / 2d;
        return outsideCount * 1000 + (int)Math.Abs(averageLevel - midpoint);
    }

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
    private Task OnItemFiltersChangedAsync() => LoadItemsAsync(1);

    private async Task LoadItemsAsync(int page, CancellationToken cancellationToken = default)
    {
        isLoadingItems = true;
        try
        {
            itemResults = await AccountsClient.GetAdministrationItemsAsync(
                itemSearch, itemCategory, page, itemQuality,
                minimumItemLevel, maximumItemLevel,
                minimumRequiredLevel, maximumRequiredLevel, cancellationToken);
        }
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

    private void SelectItemFromKeyboard(KeyboardEventArgs args, AdministrationItem item)
    {
        if (IsRowSelectionKey(args)) SelectItem(item);
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

    private void SelectLocationFromKeyboard(KeyboardEventArgs args, TeleportLocation location)
    {
        if (IsRowSelectionKey(args)) SelectLocation(location);
    }

    private void SetTeleportMode(string mode)
    {
        teleportMode = mode;
        confirmNpcTeleport = false;
    }

    private async Task OpenNpcTeleportPickerAsync()
    {
        if (SelectedActionPlayers.Count == 0) return;
        showNpcTeleportPicker = true;
        await LoadNpcTeleportsAsync(1);
    }

    private async Task OnNpcTeleportSearchAsync(ChangeEventArgs args)
    {
        npcTeleportSearch = args.Value?.ToString() ?? "";
        npcTeleportSearchCancellation?.Cancel();
        npcTeleportSearchCancellation?.Dispose();
        npcTeleportSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, npcTeleportSearchCancellation.Token);
            await LoadNpcTeleportsAsync(1, npcTeleportSearchCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadNpcTeleportsAsync(int page, CancellationToken cancellationToken = default)
    {
        if (SelectedActionPlayers.Count == 0) return;
        isLoadingNpcTeleports = true;
        try
        {
            npcTeleportResults = await AccountsClient.GetNpcTeleportsAsync(
                SelectedActionPlayers[0].Name, npcTeleportSearch, page, cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException exception)
        {
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally { isLoadingNpcTeleports = false; }
    }

    private void SelectTeleportNpc(NpcTeleportSpawn npc)
    {
        selectedTeleportNpc = npc;
        confirmNpcTeleport = false;
        showNpcTeleportPicker = false;
    }

    private void SelectTeleportNpcFromKeyboard(KeyboardEventArgs args, NpcTeleportSpawn npc)
    {
        if (IsRowSelectionKey(args)) SelectTeleportNpc(npc);
    }

    private static bool IsRowSelectionKey(KeyboardEventArgs args) =>
        args.Key is "Enter" or " ";

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
        if (!PlayerBotSettingsValid)
        {
            operationSucceeded = false;
            resultMessage = PlayerBotValidationMessage;
            return;
        }
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
    private static string FormatMoney(uint copper) =>
        $"{copper / 10000:N0}g {(copper / 100) % 100}s {copper % 100}c";
    private sealed record BatchActionResult(string PlayerName, bool Success, string Message);
}
