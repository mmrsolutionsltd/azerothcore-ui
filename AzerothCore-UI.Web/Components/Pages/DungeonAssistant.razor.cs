using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class DungeonAssistant
{
    private ServerStatus? status;
    private IReadOnlyList<AdministrationPlayer> administrationPlayers = [];
    private IReadOnlyList<CharacterPickerItem> AdministrationPickerItems =>
        CharacterPickerItem.FromAdministrationPlayers(administrationPlayers);
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
    private bool isLoading = true;
    private bool isWorking;
    private bool operationSucceeded;
    private string? errorMessage;
    private string? resultMessage;

    private DungeonDestination? SelectedDungeon =>
        dungeons.FirstOrDefault(dungeon => dungeon.DungeonId == selectedDungeonId);
    private IEnumerable<DungeonDestination> FilteredDungeons => dungeons
        .Where(dungeon => string.IsNullOrWhiteSpace(dungeonSearch)
            || dungeon.Name.Contains(dungeonSearch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(DungeonRecommendationScore)
        .ThenBy(dungeon => dungeon.MinimumLevel)
        .Take(12);
    private IReadOnlySet<uint> RecommendedDungeonIds => party is null
        ? new HashSet<uint>()
        : dungeons.OrderBy(DungeonRecommendationScore).Take(3)
            .Select(dungeon => dungeon.DungeonId).ToHashSet();
    private bool PartyHasLevelWarning => party is not null && SelectedDungeon is not null
        && party.Members.Any(member =>
            member.Level < SelectedDungeon.MinimumLevel || member.Level > SelectedDungeon.MaximumLevel);
    private bool CanUseSoap =>
        status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;

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
            errorMessage = null;
        }
        catch (Exception exception)
        {
            errorMessage = $"Server status refresh failed: {exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
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

    private void SelectPartyLeader(string? value)
    {
        partyLeader = value ?? "";
        party = null;
        selectedDungeonId = 0;
        dungeonReadiness = null;
        confirmedQuestTeleports.Clear();
    }

    private async Task LoadPartyAsync()
    {
        if (string.IsNullOrWhiteSpace(partyLeader))
            return;

        isLoadingParty = true;
        try
        {
            party = await AccountsClient.GetPartyAsync(partyLeader);
            if (dungeons.Count == 0)
                dungeons = await AccountsClient.GetDungeonsAsync();
            dungeonReadiness = null;
            operationSucceeded = true;
            resultMessage = null;
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally
        {
            isLoadingParty = false;
        }
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
            var libraryCharacters = await AccountsClient.GetDungeonLibraryCharactersAsync();
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
        finally
        {
            isLoadingReadiness = false;
        }
    }

    private async Task LaunchPartyAsync()
    {
        await RunPartyOperationAsync(
            () => AccountsClient.LaunchPartyAsync(new(
                partyLeader, selectedDungeonId, confirmDungeonLaunch)));
        if (operationSucceeded)
            confirmDungeonLaunch = false;
    }

    private void SetQuestTeleportConfirmation(uint questId, bool confirmed)
    {
        if (confirmed)
            confirmedQuestTeleports.Add(questId);
        else
            confirmedQuestTeleports.Remove(questId);
    }

    private async Task TeleportToQuestGiverAsync(DungeonQuest quest)
    {
        if (quest.QuestGiver is null)
            return;

        var players = quest.PlayerStatuses
            .Where(playerStatus => playerStatus.CanTeleport)
            .Select(playerStatus => playerStatus.PlayerName)
            .ToArray();
        await TeleportPlayersToQuestGiverAsync(quest.QuestId, quest.QuestGiver, players);
    }

    private async Task TeleportToPrerequisiteGiverAsync(DungeonQuest quest)
    {
        if (quest.Prerequisite?.QuestGiver is null)
            return;

        var players = quest.PlayerStatuses
            .Where(playerStatus => playerStatus.Status == "MissingPrerequisite")
            .Select(playerStatus => playerStatus.PlayerName)
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
        if (operationSucceeded)
            await LoadPartyAsync();
    }

    private async Task RunAsync(Func<Task<AdministrationResult?>> operation)
    {
        if (isWorking)
            return;

        isWorking = true;
        resultMessage = null;
        try
        {
            var result = await operation();
            operationSucceeded = result?.Success == true;
            resultMessage = result?.Message;
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally
        {
            isWorking = false;
            await RefreshAsync();
        }
    }

    private static string CharacterClassName(int characterClass) => characterClass switch
    {
        1 => "Warrior",
        2 => "Paladin",
        3 => "Hunter",
        4 => "Rogue",
        5 => "Priest",
        6 => "Death Knight",
        7 => "Shaman",
        8 => "Mage",
        9 => "Warlock",
        11 => "Druid",
        _ => "Unknown"
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
        if (party is null || party.Members.Count == 0)
            return dungeon.MinimumLevel;

        var averageLevel = party.Members.Average(member => member.Level);
        var outsideCount = party.Members.Count(member =>
            member.Level < dungeon.MinimumLevel || member.Level > dungeon.MaximumLevel);
        var midpoint = (dungeon.MinimumLevel + dungeon.MaximumLevel) / 2d;
        return outsideCount * 1000 + (int)Math.Abs(averageLevel - midpoint);
    }
}
