using System.Net.Http.Json;
using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Clients;

public sealed class AccountsApiClient(HttpClient httpClient)
{
    public async Task<DiagnosticsDashboard?> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<DiagnosticsDashboard>("api/diagnostics", cancellationToken);

    public async Task<string> GetDiagnosticsReportAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetStringAsync("api/diagnostics/report", cancellationToken);

    public async Task<StarterPresetPreview?> PreviewStarterPresetAsync(StarterPresetRequest request)
    {
        using var response = await httpClient.PostAsJsonAsync("api/starter-presets/preview", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>();
            throw new HttpRequestException(error?.Message ?? "Could not preview the starter preset.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<StarterPresetPreview>();
    }

    public async Task<StarterPresetApplyResult?> ApplyStarterPresetAsync(StarterPresetRequest request)
    {
        using var response = await httpClient.PostAsJsonAsync("api/starter-presets/apply", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>();
            throw new HttpRequestException(error?.Message ?? "Could not apply the starter preset.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<StarterPresetApplyResult>();
    }

    public async Task<AuctionHouseDashboard> GetAuctionHouseDashboardAsync(
        string? search, int houseId, int category, int quality, string sort,
        bool descending, int page, CancellationToken cancellationToken = default)
    {
        var uri = $"api/auction-house?search={Uri.EscapeDataString(search ?? "")}" +
                  $"&houseId={houseId}&category={category}&quality={quality}" +
                  $"&sort={Uri.EscapeDataString(sort)}&descending={descending.ToString().ToLowerInvariant()}" +
                  $"&page={page}&pageSize=30";
        return await httpClient.GetFromJsonAsync<AuctionHouseDashboard>(uri, cancellationToken)
            ?? new AuctionHouseDashboard(
                new(0, 0, 0, 0, [], [], []), [], page, 30, 0, 0);
    }

    public async Task<AdministrationResult?> EnableAuctionHouseRestockingAsync(bool confirmed)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/auction-house/restock", new AuctionHouseRestockRequest(confirmed));
        var result = await response.Content.ReadFromJsonAsync<AdministrationResult>();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(result?.Message ?? "Could not enable AHBot restocking.", null, response.StatusCode);
        return result;
    }

    public async Task<QuestHelperDashboard?> GetQuestHelperAsync(
        uint guid, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<QuestHelperDashboard>(
            $"api/quest-helper/{guid}", cancellationToken);

    public Task<AdministrationResult?> AddQuestAsync(QuestAdminRequest request) =>
        PostQuestHelperAsync("add", request);

    public Task<AdministrationResult?> RemoveQuestAsync(QuestAdminRequest request) =>
        PostQuestHelperAsync("remove", request);

    public Task<AdministrationResult?> TeleportToQuestGiverAsync(QuestGiverTeleportRequest request) =>
        PostQuestHelperAsync("teleport", request);

    private async Task<AdministrationResult?> PostQuestHelperAsync<T>(string action, T request)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/quest-helper/{action}", request);
        var result = await response.Content.ReadFromJsonAsync<AdministrationResult>();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(result?.Message ?? "The quest operation failed.", null, response.StatusCode);
        return result;
    }

    public async Task<ServerStatus?> GetServerStatusAsync() =>
        await httpClient.GetFromJsonAsync<ServerStatus>("api/server-administration/status");
    public async Task<IReadOnlyList<AdministrationPlayer>> GetAdministrationPlayersAsync() =>
        await httpClient.GetFromJsonAsync<AdministrationPlayer[]>("api/server-administration/players") ?? [];
    public async Task<AdministrationItemSearchResult> GetAdministrationItemsAsync(
        string? search, string category, int page, CancellationToken cancellationToken = default)
    {
        var uri = $"api/server-administration/items?search={Uri.EscapeDataString(search ?? "")}&category={Uri.EscapeDataString(category)}&page={page}&pageSize=30";
        return await httpClient.GetFromJsonAsync<AdministrationItemSearchResult>(uri, cancellationToken)
            ?? new AdministrationItemSearchResult([], page, 30, 0, 0);
    }
    public async Task<TeleportLocationSearchResult> GetTeleportLocationsAsync(
        string? search, int page, CancellationToken cancellationToken = default)
    {
        var uri = $"api/server-administration/teleport-locations?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize=30";
        return await httpClient.GetFromJsonAsync<TeleportLocationSearchResult>(uri, cancellationToken)
            ?? new TeleportLocationSearchResult([], page, 30, 0, 0);
    }
    public async Task<AdministrationCreatureSearchResult> GetAdministrationCreaturesAsync(
        string? search, string filter, uint family, int? minimumLevel, int? maximumLevel,
        string sort, bool descending, int page, CancellationToken cancellationToken = default)
    {
        var uri = $"api/server-administration/creatures?search={Uri.EscapeDataString(search ?? "")}" +
                  $"&filter={Uri.EscapeDataString(filter)}&family={family}&minimumLevel={minimumLevel}" +
                  $"&maximumLevel={maximumLevel}&sort={Uri.EscapeDataString(sort)}&descending={descending.ToString().ToLowerInvariant()}&page={page}&pageSize=30";
        return await httpClient.GetFromJsonAsync<AdministrationCreatureSearchResult>(uri, cancellationToken)
            ?? new AdministrationCreatureSearchResult([], page, 30, 0, 0);
    }
    public async Task<TrainerSearchResult> GetTrainersAsync(
        string characterName, string? search, string category, int page,
        CancellationToken cancellationToken = default)
    {
        var uri = $"api/server-administration/trainers?characterName={Uri.EscapeDataString(characterName)}" +
                  $"&search={Uri.EscapeDataString(search ?? "")}&category={Uri.EscapeDataString(category)}" +
                  $"&page={page}&pageSize=30";
        return await GetAdministrationAsync<TrainerSearchResult>(uri, cancellationToken)
            ?? new TrainerSearchResult([], page, 30, 0, 0);
    }
    public async Task<CollectibleSearchResult> GetCollectiblesAsync(string? search, string type, int page)
    {
        var uri = $"api/server-administration/collectibles?search={Uri.EscapeDataString(search ?? "")}&type={Uri.EscapeDataString(type)}&page={page}&pageSize=30";
        return await httpClient.GetFromJsonAsync<CollectibleSearchResult>(uri) ?? new CollectibleSearchResult([], page, 30, 0, 0);
    }
    public async Task<CharacterCollectibleSearchResult> GetCharacterCollectiblesAsync(
        string characterName, string? search, string type, bool missingOnly, int page)
    {
        var uri = $"api/server-administration/collectibles/collection?characterName={Uri.EscapeDataString(characterName)}" +
                  $"&search={Uri.EscapeDataString(search ?? "")}&type={Uri.EscapeDataString(type)}" +
                  $"&missingOnly={missingOnly.ToString().ToLowerInvariant()}&page={page}&pageSize=30";
        return await GetAdministrationAsync<CharacterCollectibleSearchResult>(uri)
            ?? new CharacterCollectibleSearchResult([], page, 30, 0, 0, 0, 0);
    }
    public async Task<PlayerBotSettings?> GetPlayerBotSettingsAsync() =>
        await httpClient.GetFromJsonAsync<PlayerBotSettings>("api/server-administration/settings/playerbots");
    public async Task<PlayerBotSettings?> UpdatePlayerBotSettingsAsync(PlayerBotSettings settings)
    {
        using var response = await httpClient.PutAsJsonAsync("api/server-administration/settings/playerbots", settings);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>();
            throw new HttpRequestException(error?.Message ?? "Could not save PlayerBots settings.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<PlayerBotSettings>();
    }
    public async Task<GameplayRateSettings?> GetGameplayRateSettingsAsync() =>
        await httpClient.GetFromJsonAsync<GameplayRateSettings>("api/server-administration/settings/rates");
    public async Task<GameplayRateSettings?> UpdateGameplayRateSettingsAsync(GameplayRateSettings settings)
    {
        using var response = await httpClient.PutAsJsonAsync("api/server-administration/settings/rates", settings);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>();
            throw new HttpRequestException(error?.Message ?? "Could not save gameplay rates.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<GameplayRateSettings>();
    }

    public Task<AuctionHouseBotSettings?> GetAuctionHouseBotSettingsAsync() => GetAdministrationAsync<AuctionHouseBotSettings>("api/server-administration/settings/auction-house-bot");
    public Task<AutoBalanceSettings?> GetAutoBalanceSettingsAsync() => GetAdministrationAsync<AutoBalanceSettings>("api/server-administration/settings/autobalance");
    public Task<TransmogSettings?> GetTransmogSettingsAsync() => GetAdministrationAsync<TransmogSettings>("api/server-administration/settings/transmog");
    public Task<AoeLootSettings?> GetAoeLootSettingsAsync() => GetAdministrationAsync<AoeLootSettings>("api/server-administration/settings/aoe-loot");
    public Task<AuctionHouseBotSettings?> UpdateAuctionHouseBotSettingsAsync(AuctionHouseBotSettings settings) => PutSettingsAsync("auction-house-bot", settings);
    public Task<AutoBalanceSettings?> UpdateAutoBalanceSettingsAsync(AutoBalanceSettings settings) => PutSettingsAsync("autobalance", settings);
    public Task<TransmogSettings?> UpdateTransmogSettingsAsync(TransmogSettings settings) => PutSettingsAsync("transmog", settings);
    public Task<AoeLootSettings?> UpdateAoeLootSettingsAsync(AoeLootSettings settings) => PutSettingsAsync("aoe-loot", settings);

    private async Task<T?> PutSettingsAsync<T>(string module, T settings)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/server-administration/settings/{module}", settings);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>();
            throw new HttpRequestException(error?.Message ?? "Could not save module settings.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public Task<AdministrationResult?> StartServersAsync() => PostAsync("api/server-administration/start", new { });
    public Task<AdministrationResult?> StopServersAsync(bool force) => PostAsync($"api/server-administration/stop?force={force.ToString().ToLowerInvariant()}", new { });
    public Task<AdministrationResult?> RestartServersAsync(bool force) => PostAsync($"api/server-administration/restart?force={force.ToString().ToLowerInvariant()}", new { });
    public Task<AdministrationResult?> GiveItemAsync(GiveItemRequest request) => PostAsync("api/server-administration/items/give", request);
    public Task<AdministrationResult?> MailItemAsync(MailItemRequest request) => PostAsync("api/server-administration/items/mail", request);
    public Task<AdministrationResult?> GiveMoneyAsync(GiveMoneyRequest request) => PostAsync("api/server-administration/money/give", request);
    public Task<AdministrationResult?> TeleportAsync(TeleportPlayerRequest request) => PostAsync("api/server-administration/players/teleport", request);
    public Task<AdministrationResult?> TeleportToPlayerAsync(PlayerRelativeTeleportRequest request) => PostAsync("api/server-administration/players/teleport-to-player", request);
    public Task<PartySnapshot?> GetPartyAsync(string leaderName) =>
        GetAdministrationAsync<PartySnapshot>($"api/server-administration/parties/{Uri.EscapeDataString(leaderName)}");
    public Task<AdministrationResult?> AddPartyBotAsync(PartyBotRequest request) => PostAsync("api/server-administration/parties/bots/add", request);
    public Task<AdministrationResult?> RemovePartyBotAsync(PartyBotRequest request) => PostAsync("api/server-administration/parties/bots/remove", request);
    public Task<AdministrationResult?> ClearPartyBotsAsync(PartyLeaderRequest request) => PostAsync("api/server-administration/parties/bots/clear", request);
    public Task<AdministrationResult?> FillPartyWithBotsAsync(PartyLeaderRequest request) => PostAsync("api/server-administration/parties/bots/fill", request);
    public async Task<IReadOnlyList<DungeonDestination>> GetDungeonsAsync() =>
        await GetAdministrationAsync<DungeonDestination[]>("api/server-administration/dungeons") ?? [];
    public Task<DungeonReadiness?> GetDungeonReadinessAsync(string leaderName, uint dungeonId) =>
        GetAdministrationAsync<DungeonReadiness>(
            $"api/server-administration/parties/{Uri.EscapeDataString(leaderName)}/dungeons/{dungeonId}/readiness");
    public Task<AdministrationResult?> LaunchPartyAsync(LaunchDungeonRequest request) =>
        PostAsync("api/server-administration/parties/launch", request);
    public Task<AdministrationResult?> SpawnCreatureAsync(SpawnCreatureRequest request) =>
        PostAsync("api/server-administration/creatures/spawn", request);
    public Task<AdministrationResult?> SetAccountGmAsync(SetAccountGmRequest request) =>
        PostAsync("api/server-administration/accounts/gm", request);
    public Task<AdministrationResult?> SetPlayerSpeedAsync(SetPlayerSpeedRequest request) =>
        PostAsync("api/server-administration/players/speed", request);
    public Task<AdministrationResult?> ApplyCharacterServiceAsync(CharacterServiceRequest request) =>
        PostAsync("api/server-administration/characters/service", request);
    public Task<AdministrationResult?> TeleportToTrainerAsync(TeleportToTrainerRequest request) =>
        PostAsync("api/server-administration/trainers/teleport", request);
    public Task<WeaponTrainingStatus[]?> GetWeaponTrainingAsync(string playerName) =>
        GetAdministrationAsync<WeaponTrainingStatus[]>($"api/server-administration/players/{Uri.EscapeDataString(playerName)}/weapon-training");
    public Task<AdministrationResult?> GrantWeaponTrainingAsync(GrantWeaponTrainingRequest request) =>
        PostAsync("api/server-administration/players/weapon-training", request);

    private async Task<AdministrationResult?> PostAsync<T>(string uri, T request)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request);
        var result = await response.Content.ReadFromJsonAsync<AdministrationResult>();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(result?.Message ?? "Administration command failed.", null, response.StatusCode);
        return result;
    }

    private async Task<T?> GetAdministrationAsync<T>(string uri, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>(cancellationToken);
            throw new HttpRequestException(error?.Message ?? "Administration request failed.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public async Task<PagedAccounts> GetAccountsAsync(
        string? search,
        string type,
        string sort,
        bool descending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["search"] = search,
            ["type"] = type,
            ["sort"] = sort,
            ["descending"] = descending.ToString().ToLowerInvariant(),
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        var queryString = string.Join("&", query
            .Where(item => !string.IsNullOrEmpty(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));

        return await httpClient.GetFromJsonAsync<PagedAccounts>(
            $"api/accounts?{queryString}",
            cancellationToken)
            ?? new PagedAccounts([], page, pageSize, 0, 0);
    }

    public async Task<AccountWithCharacters?> GetAccountAsync(
        uint accountId,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<AccountWithCharacters>(
            $"api/accounts/{accountId}",
            cancellationToken);
    }

    public async Task<CharacterDetails?> GetCharacterAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CharacterDetails>(
            $"api/characters/{guid}",
            cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterOverviewSummary>> GetCharacterOverviewAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<CharacterOverviewSummary[]>(
            "api/characters", cancellationToken) ?? [];

    public async Task<IReadOnlyList<CharacterQuest>> GetCharacterQuestsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CharacterQuest[]>(
            $"api/characters/{guid}/quests",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<EquippedItem>> GetEquippedItemsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<EquippedItem[]>(
            $"api/characters/{guid}/inventory/equipped",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<BagItem>> GetBagItemsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<BagItem[]>(
            $"api/characters/{guid}/inventory/bags",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<CompletedCharacterQuest>> GetCompletedCharacterQuestsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CompletedCharacterQuest[]>(
            $"api/characters/{guid}/quests/completed",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<CharacterProfession>> GetCharacterProfessionsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CharacterProfession[]>(
            $"api/characters/{guid}/professions",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<MissingVendorRecipe>> GetMissingVendorRecipesAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MissingVendorRecipe[]>(
            $"api/characters/{guid}/professions/recipes/vendors",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<MissingQuestRecipe>> GetMissingQuestRecipesAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MissingQuestRecipe[]>(
            $"api/characters/{guid}/professions/recipes/quests",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<MissingLootRecipe>> GetMissingLootRecipesAsync(uint guid) =>
        await httpClient.GetFromJsonAsync<MissingLootRecipe[]>(
            $"api/characters/{guid}/professions/recipes/loot") ?? [];

    public async Task<IReadOnlyList<MissingUnclassifiedRecipe>> GetUnclassifiedRecipesAsync(uint guid) =>
        await httpClient.GetFromJsonAsync<MissingUnclassifiedRecipe[]>(
            $"api/characters/{guid}/professions/recipes/unclassified") ?? [];

    public async Task<IReadOnlyList<MissingClassSpell>> GetMissingClassSpellsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MissingClassSpell[]>(
            $"api/characters/{guid}/training/class",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<MissingProfessionSpell>> GetMissingProfessionSpellsAsync(
        uint guid,
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MissingProfessionSpell[]>(
            $"api/characters/{guid}/training/professions",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<CharacterTrainingSummary>> GetAvailableTrainingAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CharacterTrainingSummary[]>(
            "api/training",
            cancellationToken) ?? [];
    }

    public Task<AdministrationResult?> GrantProfessionTrainingAsync(
        GrantProfessionTrainingRequest request) =>
        PostAsync("api/training/professions/grant", request);

    public async Task<IReadOnlyList<ProfessionStarterCharacter>> GetProfessionStartersAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ProfessionStarterCharacter[]>(
            "api/training/professions/starters", cancellationToken) ?? [];

    public Task<AdministrationResult?> LearnProfessionAsync(
        LearnProfessionRequest request) =>
        PostAsync("api/training/professions/learn", request);

    public async Task<IReadOnlyList<ProfessionManagementCharacter>> GetProfessionManagementAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ProfessionManagementCharacter[]>(
            "api/training/professions/manage", cancellationToken) ?? [];

    public Task<AdministrationResult?> UnlearnProfessionAsync(
        UnlearnProfessionRequest request) =>
        PostAsync("api/training/professions/unlearn", request);

    public async Task<IReadOnlyList<DatabaseBackupSummary>> GetDatabaseBackupsAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<DatabaseBackupSummary[]>(
            "api/database-backups", cancellationToken) ?? [];

    public async Task<DatabaseBackupSummary?> CreateDatabaseBackupAsync(bool confirmed)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/database-backups", new CreateDatabaseBackupRequest(confirmed));
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<AdministrationResult>();
            throw new HttpRequestException(
                error?.Message ?? "Database backup failed.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<DatabaseBackupSummary>();
    }

    public Task<AdministrationResult?> RestoreDatabaseBackupAsync(
        RestoreDatabaseBackupRequest request) =>
        PostAsync("api/database-backups/restore", request);
}
