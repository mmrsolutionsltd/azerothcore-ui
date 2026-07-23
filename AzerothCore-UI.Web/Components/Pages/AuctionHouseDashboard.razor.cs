using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class AuctionHouseDashboard
{
    private AzerothCore_UI.Web.Models.AuctionHouseDashboard result = new(
        new(0, 0, 0, 0, [], [], []), [], 1, 30, 0, 0);
    private AuctionHouseBotSettings? settings;
    private ServerStatus? status;
    private string search = "", sort = "expiry";
    private int houseId, category = -1, quality = -1, page = 1;
    private bool descending, isLoadingPage = true, isLoading, isWorking;
    private bool restockConfirmed, operationSucceeded;
    private string? message;

    private bool CanEnableRestocking =>
        restockConfirmed && !isWorking && status is { WorldServer.IsRunning: true, SoapConfigured: true };
    private string ItemSources => settings is null ? "Unknown" : string.Join(", ",
        new[] {
            settings.IncludeVendorItems ? "vendor" : null,
            settings.IncludeLootItems ? "loot" : null,
            settings.IncludeProfessionItems ? "profession" : null
        }.Where(value => value is not null));
    private IReadOnlyList<string> LowStockCategories =>
        result.Summary.Categories.Where(item =>
                (item.Id is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 9 or 11)
                && item.Count < 10)
            .Select(item => $"{item.Name} ({item.Count})").ToArray();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var settingsTask = AccountsClient.GetAuctionHouseBotSettingsAsync();
            var statusTask = AccountsClient.GetServerStatusAsync();
            var dashboardTask = AccountsClient.GetAuctionHouseDashboardAsync(
                search, houseId, category, quality, sort, descending, page);
            await Task.WhenAll(settingsTask, statusTask, dashboardTask);
            settings = await settingsTask;
            status = await statusTask;
            result = await dashboardTask;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isLoadingPage = false; }
    }

    private async Task ApplyFiltersAsync() => await LoadAsync(1);

    private async Task LoadAsync(int requestedPage)
    {
        isLoading = true;
        message = null;
        try
        {
            page = Math.Max(1, requestedPage);
            result = await AccountsClient.GetAuctionHouseDashboardAsync(
                search, houseId, category, quality, sort, descending, page);
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isLoading = false; }
    }

    private async Task EnableRestockingAsync()
    {
        if (!CanEnableRestocking) return;
        isWorking = true;
        try
        {
            var response = await AccountsClient.EnableAuctionHouseRestockingAsync(restockConfirmed);
            operationSucceeded = response?.Success == true;
            message = response?.Message;
            restockConfirmed = false;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }

    private int Percentage(int count) =>
        result.Summary.TotalAuctions == 0 ? 0 : (int)Math.Round(count * 100d / result.Summary.TotalAuctions);
    private static string QualityBadge(int quality) => quality switch
    {
        0 => "text-bg-secondary", 1 => "text-bg-light", 2 => "text-bg-success",
        3 => "text-bg-primary", 4 => "text-bg-dark", 5 => "text-bg-warning",
        6 => "text-bg-danger", 7 => "text-bg-info", _ => "text-bg-secondary"
    };
    private static string FormatExpiry(DateTime expiresUtc)
    {
        var remaining = expiresUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) return "Expired/pending cleanup";
        if (remaining.TotalHours < 1) return $"{Math.Max(1, (int)remaining.TotalMinutes)}m";
        if (remaining.TotalDays < 1) return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
    }
}
