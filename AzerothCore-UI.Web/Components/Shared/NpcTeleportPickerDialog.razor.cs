using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class NpcTeleportPickerDialog : IDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string? InitialSearchText { get; set; }
    [Parameter] public string AnchorPlayerName { get; set; } = "";
    [Parameter] public EventCallback<NpcTeleportSpawn> NpcSelected { get; set; }
    [Parameter] public EventCallback Closed { get; set; }

    private NpcTeleportSearchResult results = new([], 1, 30, 0, 0);
    private CancellationTokenSource? searchCancellation;
    private bool wasOpen;
    private bool isLoading;
    private string search = "";
    private string? errorMessage;
    private ElementReference searchInput;
    private bool focusSearch;
    private IReadOnlyList<NpcTeleportSpawn> recentNpcs = [];

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !wasOpen)
        {
            search = InitialSearchText?.Trim() ?? "";
            var storedNpcs = await RecentSelections.GetAsync<NpcTeleportSpawn>(
                RecentPickerKeys.Npcs);
            if (storedNpcs.Count > 0 || recentNpcs.Count == 0)
                recentNpcs = storedNpcs;
            focusSearch = true;
            if (!string.IsNullOrWhiteSpace(AnchorPlayerName))
                await LoadAsync(1);
        }
        wasOpen = IsOpen;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!IsOpen || !focusSearch)
            return;

        focusSearch = false;
        try
        {
            await Javascript.InvokeVoidAsync(
                "azerothCoreUi.focusAndSelect", searchInput);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
        }
    }

    private async Task SearchAsync(ChangeEventArgs args)
    {
        search = args.Value?.ToString() ?? "";
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, searchCancellation.Token);
            await LoadAsync(1, searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadAsync(int page, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(AnchorPlayerName))
            return;

        isLoading = true;
        errorMessage = null;
        try
        {
            results = await AccountsClient.GetNpcTeleportsAsync(
                AnchorPlayerName, search, page, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException exception)
        {
            errorMessage = exception.Message;
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task SelectAsync(NpcTeleportSpawn npc)
    {
        recentNpcs = await RecentSelections.RememberAsync(
            RecentPickerKeys.Npcs, npc, value => value.SpawnId.ToString());
        await NpcSelected.InvokeAsync(npc);
    }

    private Task SelectFromKeyboardAsync(KeyboardEventArgs args, NpcTeleportSpawn npc) =>
        args.Key is "Enter" or " " ? SelectAsync(npc) : Task.CompletedTask;

    private Task CloseAsync() => Closed.InvokeAsync();

    private static string NpcDisplayText(NpcTeleportSpawn npc) =>
        $"{npc.Name} ({MapName(npc.MapId)})";

    private static string MapName(ushort mapId) => mapId switch
    {
        0 => "Eastern Kingdoms",
        1 => "Kalimdor",
        530 => "Outland",
        571 => "Northrend",
        _ => $"Map {mapId}"
    };

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
    }
}
