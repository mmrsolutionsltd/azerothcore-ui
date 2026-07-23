using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class QuestHelper
{
    private IReadOnlyList<CharacterOverviewSummary> characters = [];
    private QuestHelperDashboard? dashboard;
    private PendingQuestOperation? pendingOperation;
    private QuestHelperRecommendation? pendingTeleport;
    private uint selectedGuid;
    private string search = "";
    private bool nearbyOnly = true, isLoading = true, isWorking, confirmed, operationSucceeded;
    private string? message;

    private int ReadyCount => dashboard?.ActiveQuests.Count(quest => quest.Status == 1) ?? 0;
    private IReadOnlyList<QuestHelperRecommendation> FilteredRecommendations =>
        dashboard?.RecommendedQuests.Where(quest =>
                (!nearbyOnly || quest.SameMap)
                && (string.IsNullOrWhiteSpace(search)
                    || quest.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || quest.QuestGiver.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || quest.QuestId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToArray() ?? [];

    protected override async Task OnInitializedAsync()
    {
        try { characters = await AccountsClient.GetCharacterOverviewAsync(); }
        catch (Exception exception) { message = exception.Message; operationSucceeded = false; }
        finally { isLoading = false; }
    }

    private async Task LoadDashboardAsync()
    {
        CancelOperation();
        message = null;
        dashboard = null;
        if (selectedGuid == 0) return;
        isLoading = true;
        try { dashboard = await AccountsClient.GetQuestHelperAsync(selectedGuid); }
        catch (Exception exception) { message = exception.Message; operationSucceeded = false; }
        finally { isLoading = false; }
    }

    private void SelectOperation(uint questId, string title, bool add)
    {
        pendingOperation = new(questId, title, add);
        pendingTeleport = null;
        confirmed = false;
        message = null;
    }

    private void SelectTeleport(QuestHelperRecommendation quest)
    {
        pendingTeleport = quest;
        pendingOperation = null;
        confirmed = false;
        message = null;
    }

    private void CancelOperation()
    {
        pendingOperation = null;
        pendingTeleport = null;
        confirmed = false;
    }

    private async Task ChangeQuestAsync()
    {
        if (dashboard is null || pendingOperation is null || !confirmed) return;
        isWorking = true;
        try
        {
            var request = new QuestAdminRequest(
                dashboard.Character.Name, pendingOperation.QuestId, confirmed);
            var response = pendingOperation.Add
                ? await AccountsClient.AddQuestAsync(request)
                : await AccountsClient.RemoveQuestAsync(request);
            operationSucceeded = response?.Success == true;
            message = response?.Message;
            if (operationSucceeded) await LoadDashboardAsync();
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }

    private async Task TeleportAsync()
    {
        if (dashboard is null || pendingTeleport?.QuestGiverSpawnId is not { } spawnId || !confirmed) return;
        isWorking = true;
        try
        {
            var response = await AccountsClient.TeleportToQuestGiverAsync(new(
                dashboard.Character.Name, pendingTeleport.QuestId, spawnId, confirmed));
            operationSucceeded = response?.Success == true;
            message = response?.Message;
            if (operationSucceeded) CancelOperation();
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }

    private static string FormatQuestLevel(short level) => level < 0 ? "scales" : level.ToString();
    private static string LocationText(QuestHelperRecommendation quest) =>
        quest.MapId is null ? "Unknown" : $"{MapName(quest.MapId.Value)}, zone {quest.ZoneId}, area {quest.AreaId}";
    private static string MapName(ushort mapId) => mapId switch
    {
        0 => "Eastern Kingdoms", 1 => "Kalimdor", 530 => "Outland",
        571 => "Northrend", 609 => "Acherus", _ => $"Map {mapId}"
    };

    private sealed record PendingQuestOperation(uint QuestId, string Title, bool Add);
}
