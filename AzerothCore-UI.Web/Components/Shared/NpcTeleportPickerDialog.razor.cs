using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class NpcTeleportPickerDialog : IDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string AnchorPlayerName { get; set; } = "";
    [Parameter] public EventCallback<NpcTeleportSpawn> NpcSelected { get; set; }
    [Parameter] public EventCallback Closed { get; set; }

    private NpcTeleportSearchResult results = new([], 1, 30, 0, 0);
    private CancellationTokenSource? searchCancellation;
    private bool wasOpen;
    private bool isLoading;
    private string search = "";
    private string? errorMessage;

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !wasOpen && !string.IsNullOrWhiteSpace(AnchorPlayerName))
            await LoadAsync(1);
        wasOpen = IsOpen;
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

    private Task SelectAsync(NpcTeleportSpawn npc) => NpcSelected.InvokeAsync(npc);

    private Task SelectFromKeyboardAsync(KeyboardEventArgs args, NpcTeleportSpawn npc) =>
        args.Key is "Enter" or " " ? SelectAsync(npc) : Task.CompletedTask;

    private Task CloseAsync() => Closed.InvokeAsync();

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
