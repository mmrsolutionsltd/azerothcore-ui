using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class StarterPresets
{
    private IReadOnlyList<CharacterOverviewSummary> characters = [];
    private readonly HashSet<string> selectedNames = new(StringComparer.OrdinalIgnoreCase);
    private StarterPresetPreview? preview;
    private StarterPresetApplyResult? applyResult;
    private ServerStatus? status;
    private string preset = "new";
    private int bagCount = 4, moneyGold = 10;
    private bool includeHeirlooms = true, includeHearthstone = true;
    private bool includeFoodAndDrink = true, includeClassSupplies = true;
    private bool confirmed, isLoading = true, isWorking, operationSucceeded;
    private string? message;
    private bool CanApply => preview is not null && confirmed && !isWorking
        && status is { WorldServer.IsRunning: true, SoapConfigured: true };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var charactersTask = AccountsClient.GetCharacterOverviewAsync();
            var statusTask = AccountsClient.GetServerStatusAsync();
            await Task.WhenAll(charactersTask, statusTask);
            characters = await charactersTask;
            status = await statusTask;
        }
        catch (Exception exception) { message = exception.Message; operationSucceeded = false; }
        finally { isLoading = false; }
    }

    private void ToggleCharacter(string name, bool selected)
    {
        if (selected && selectedNames.Count < 10) selectedNames.Add(name);
        else if (!selected) selectedNames.Remove(name);
        InvalidatePreview();
    }

    private void SelectAll(bool selected)
    {
        selectedNames.Clear();
        if (selected)
            foreach (var character in characters.Take(10)) selectedNames.Add(character.Name);
        InvalidatePreview();
    }

    private void PresetChanged()
    {
        (bagCount, moneyGold) = preset switch
        {
            "level10" => (4, 25),
            "returning" => (4, 100),
            _ => (4, 10)
        };
        includeHeirlooms = includeHearthstone = includeFoodAndDrink = includeClassSupplies = true;
        InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        preview = null;
        applyResult = null;
        confirmed = false;
        message = null;
    }

    private StarterPresetRequest BuildRequest(bool isConfirmed) => new(
        selectedNames.OrderBy(name => name).ToArray(), preset, bagCount, includeHeirlooms,
        includeHearthstone, includeFoodAndDrink, includeClassSupplies, moneyGold, isConfirmed);

    private async Task PreviewAsync()
    {
        isWorking = true;
        applyResult = null;
        confirmed = false;
        message = null;
        try
        {
            preview = await AccountsClient.PreviewStarterPresetAsync(BuildRequest(false));
            operationSucceeded = true;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }

    private async Task ApplyAsync()
    {
        if (!CanApply) return;
        isWorking = true;
        try
        {
            applyResult = await AccountsClient.ApplyStarterPresetAsync(BuildRequest(true));
            operationSucceeded = applyResult?.Success == true;
            message = applyResult?.Message;
            preview = null;
            confirmed = false;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isWorking = false; }
    }

    private static string FormatQuantity(StarterPresetAction action) =>
        action.Kind == "Money" ? $"{action.Quantity / 10_000} gold" : action.Quantity.ToString("N0");
}
