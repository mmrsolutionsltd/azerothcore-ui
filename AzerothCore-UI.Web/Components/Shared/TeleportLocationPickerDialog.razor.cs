using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class TeleportLocationPickerDialog : IDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string? InitialSearchText { get; set; }
    [Parameter] public EventCallback<TeleportLocation> LocationSelected { get; set; }
    [Parameter] public EventCallback Closed { get; set; }

    private TeleportLocationSearchResult results = new([], 1, 30, 0, 0);
    private CancellationTokenSource? searchCancellation;
    private bool wasOpen;
    private bool isLoading;
    private string search = "";
    private string? errorMessage;
    private ElementReference searchInput;
    private bool focusSearch;
    private IReadOnlyList<TeleportLocation> recentLocations = [];

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !wasOpen)
        {
            search = InitialSearchText?.Trim() ?? "";
            var storedLocations = await RecentSelections.GetAsync<TeleportLocation>(
                RecentPickerKeys.Locations);
            if (storedLocations.Count > 0 || recentLocations.Count == 0)
                recentLocations = storedLocations;
            focusSearch = true;
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
        isLoading = true;
        errorMessage = null;
        try
        {
            results = await AccountsClient.GetTeleportLocationsAsync(
                search, page, cancellationToken);
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

    private async Task SelectAsync(TeleportLocation location)
    {
        recentLocations = await RecentSelections.RememberAsync(
            RecentPickerKeys.Locations, location, value => value.Id.ToString());
        await LocationSelected.InvokeAsync(location);
    }

    private Task SelectFromKeyboardAsync(KeyboardEventArgs args, TeleportLocation location) =>
        args.Key is "Enter" or " " ? SelectAsync(location) : Task.CompletedTask;

    private Task CloseAsync() => Closed.InvokeAsync();

    private static string LocationDisplayText(TeleportLocation location) =>
        location.Name;

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
