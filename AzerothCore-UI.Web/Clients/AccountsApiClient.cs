using System.Net.Http.Json;
using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Clients;

public sealed class AccountsApiClient(HttpClient httpClient)
{
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
}
