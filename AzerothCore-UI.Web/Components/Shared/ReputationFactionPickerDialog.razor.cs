using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class ReputationFactionPickerDialog : IDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string? InitialSearchText { get; set; }
    [Parameter] public EventCallback<ReputationFaction> FactionSelected { get; set; }
    [Parameter] public EventCallback Closed { get; set; }

    private ReputationFactionSearchResult results = new([], 1, 30, 0, 0);
    private CancellationTokenSource? searchCancellation;
    private bool wasOpen;
    private bool isLoading;
    private string search = "";
    private string? errorMessage;
    private ElementReference searchInput;
    private bool focusSearch;
    private IReadOnlyList<ReputationFaction> recentFactions = [];

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !wasOpen)
        {
            search = InitialSearchText?.Trim() ?? "";
            var storedFactions = await RecentSelections.GetAsync<ReputationFaction>(
                RecentPickerKeys.Factions);
            if (storedFactions.Count > 0 || recentFactions.Count == 0)
                recentFactions = storedFactions;
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
            results = await AccountsClient.GetReputationFactionsAsync(search, page, cancellationToken);
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

    private async Task SelectAsync(ReputationFaction faction)
    {
        recentFactions = await RecentSelections.RememberAsync(
            RecentPickerKeys.Factions, faction, value => value.FactionId.ToString());
        await FactionSelected.InvokeAsync(faction);
    }

    private Task SelectFromKeyboardAsync(KeyboardEventArgs args, ReputationFaction faction) =>
        args.Key is "Enter" or " " ? SelectAsync(faction) : Task.CompletedTask;

    private Task CloseAsync() => Closed.InvokeAsync();

    private static string FactionDisplayText(ReputationFaction faction) =>
        $"{faction.Name} ({faction.FactionId})";

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
    }
}
