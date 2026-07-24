using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;
public partial class Collectibles
{
    private CharacterCollectibleSearchResult results = new([], 1, 30, 0, 0, 0, 0);
    private IReadOnlyList<AdministrationPlayer> players = [];
    private IEnumerable<AdministrationPlayer> OrderedPlayers => players
        .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private IReadOnlyList<CharacterPickerItem> PickerItems => OrderedPlayers
        .Select(player => new CharacterPickerItem(
            player.Name, player.Name, $"Account {player.Username}", player.Online, player.IsPlayerBot))
        .ToArray();
    private readonly HashSet<uint> bulkSelection = [];
    private readonly Dictionary<uint, CharacterCollectibleItem> selectedItems = [];
    private CancellationTokenSource? debounce;
    private CharacterCollectibleItem? selected;
    private string search = "", type = "all", playerName = "";
    private string? message;
    private bool isLoadingPage = true, collectionLoaded, missingOnly, isSearching, isWorking, confirmSingle, confirmBulk, succeeded;
    protected override async Task OnInitializedAsync()
    {
        try { players = await AccountsClient.GetAdministrationPlayersAsync(); }
        catch (Exception exception) { message = exception.Message; }
        finally { isLoadingPage = false; }
    }
    private async Task LoadCollectionAsync() { collectionLoaded = true; selected = null; bulkSelection.Clear(); selectedItems.Clear(); await LoadAsync(1); }
    private void SelectPlayer(string? value)
    {
        playerName = value ?? "";
        collectionLoaded = false;
    }
    private async Task SearchChangedAsync(ChangeEventArgs args)
    {
        search = args.Value?.ToString() ?? ""; debounce?.Cancel(); debounce?.Dispose(); debounce = new();
        try { await Task.Delay(250, debounce.Token); await LoadAsync(1); } catch (OperationCanceledException) { }
    }
    private Task ReloadAsync() => LoadAsync(1);
    private async Task LoadAsync(int page)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return; isSearching = true;
        try { results = await AccountsClient.GetCharacterCollectiblesAsync(playerName, search, type, missingOnly, page); }
        catch (Exception exception) { succeeded = false; message = exception.Message; }
        finally { isSearching = false; }
    }
    private void SelectItem(CharacterCollectibleItem item) { selected = item; confirmSingle = false; }
    private void ToggleBulk(CharacterCollectibleItem item, ChangeEventArgs args)
    {
        var add = args.Value is bool value && value;
        if (add && bulkSelection.Count < 10) { bulkSelection.Add(item.ItemId); selectedItems[item.ItemId] = item; }
        else if (!add) { bulkSelection.Remove(item.ItemId); selectedItems.Remove(item.ItemId); }
        confirmBulk = false;
    }
    private async Task DeliverSingleAsync()
    {
        if (selected is null || isWorking) return;
        await DeliverItemsAsync([selected]);
        if (succeeded) { confirmSingle = false; selected = null; await LoadAsync(results.Page); }
    }
    private async Task DeliverBulkAsync()
    {
        if (bulkSelection.Count is 0 or > 10 || isWorking) return;
        await DeliverItemsAsync(bulkSelection.Select(id => selectedItems[id]).ToArray());
        if (succeeded) { confirmBulk = false; bulkSelection.Clear(); selectedItems.Clear(); await LoadAsync(results.Page); }
    }
    private async Task DeliverItemsAsync(IReadOnlyList<CharacterCollectibleItem> items)
    {
        isWorking = true; succeeded = false;
        try
        {
            foreach (var item in items)
                await AccountsClient.MailItemAsync(new(playerName, item.ItemId, 1, "Collectible delivery", $"{item.Name} from the server administrator."));
            succeeded = true; message = items.Count == 1 ? $"{items[0].Name} was mailed to {playerName}." : $"{items.Count} collectibles were mailed to {playerName}.";
        }
        catch (Exception exception) { message = exception.Message; }
        finally { isWorking = false; }
    }
}
