using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class ItemPickerDialog : IDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public IReadOnlyCollection<string> TargetNames { get; set; } = [];
    [Parameter] public EventCallback<AdministrationItem> ItemSelected { get; set; }
    [Parameter] public EventCallback Closed { get; set; }

    private AdministrationItemSearchResult results = new([], 1, 30, 0, 0);
    private CancellationTokenSource? searchCancellation;
    private bool wasOpen;
    private bool isLoading;
    private string search = "";
    private string category = "all";
    private int? quality;
    private int? minimumItemLevel;
    private int? maximumItemLevel;
    private int? minimumRequiredLevel;
    private int? maximumRequiredLevel;
    private string suitability = "all";
    private string? errorMessage;

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !wasOpen)
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

    private Task FiltersChangedAsync() => LoadAsync(1);

    private async Task LoadAsync(int page, CancellationToken cancellationToken = default)
    {
        isLoading = true;
        errorMessage = null;
        try
        {
            results = await AccountsClient.GetAdministrationItemsAsync(
                search, category, page, quality,
                minimumItemLevel, maximumItemLevel,
                minimumRequiredLevel, maximumRequiredLevel,
                TargetNames, suitability, cancellationToken);
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

    private Task SelectAsync(AdministrationItem item) => ItemSelected.InvokeAsync(item);

    private Task SelectFromKeyboardAsync(KeyboardEventArgs args, AdministrationItem item) =>
        args.Key is "Enter" or " " ? SelectAsync(item) : Task.CompletedTask;

    private Task CloseAsync() => Closed.InvokeAsync();

    private static string QualityName(byte quality) => quality switch
    {
        0 => "Poor", 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Epic",
        5 => "Legendary", 6 => "Artifact", 7 => "Heirloom", _ => "Unknown"
    };

    private static string QualityClass(byte quality) => quality switch
    {
        0 => "text-secondary", 2 => "text-success", 3 => "text-primary", 4 => "text-purple",
        5 => "text-warning", 6 => "text-danger", 7 => "text-warning", _ => ""
    };

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
    }
}
