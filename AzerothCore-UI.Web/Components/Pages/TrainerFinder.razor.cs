using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class TrainerFinder : IDisposable
{
    private IReadOnlyList<AdministrationPlayer> players = [];
    private IEnumerable<AdministrationPlayer> OrderedPlayers =>
        players.Where(player => !player.IsPlayerBot)
            .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private AdministrationPlayer? SelectedCharacter => players.FirstOrDefault(
        player => player.Name.Equals(characterName.Trim(), StringComparison.OrdinalIgnoreCase));
    private TrainerSearchResult results = new([], 1, 30, 0, 0);
    private TrainerSpawn? selectedTrainer;
    private ServerStatus? status;
    private CancellationTokenSource? searchCancellation;
    private string characterName = "", search = "", category = "all";
    private string? message;
    private bool isLoadingPage = true, isLoadingTrainers, isWorking, operationSucceeded, confirmed;
    private long queryGeneration;
    private bool CanTeleport => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;
    private bool CanTeleportSelected => CanTeleport && confirmed && selectedTrainer is not null && SelectedCharacter is not null;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            status = await AccountsClient.GetServerStatusAsync();
            players = await AccountsClient.GetAdministrationPlayersAsync();
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally { isLoadingPage = false; }
    }

    private async Task CharacterChangedAsync()
    {
        selectedTrainer = null;
        confirmed = false;
        if (SelectedCharacter is null)
        {
            results = new([], 1, 30, 0, 0);
            return;
        }
        await LoadAsync(1);
    }

    private async Task SearchChangedAsync(ChangeEventArgs args)
    {
        search = args.Value?.ToString() ?? "";
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        var generation = ++queryGeneration;
        try
        {
            await Task.Delay(250, searchCancellation.Token);
            await LoadAsync(1, generation, searchCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private Task ReloadAsync()
    {
        selectedTrainer = null;
        confirmed = false;
        return SelectedCharacter is null ? Task.CompletedTask : LoadAsync(1);
    }

    private Task LoadAsync(int page) => LoadAsync(page, ++queryGeneration, CancellationToken.None);

    private async Task LoadAsync(int page, long generation, CancellationToken cancellationToken)
    {
        if (SelectedCharacter is null) return;
        isLoadingTrainers = true;
        try
        {
            var response = await AccountsClient.GetTrainersAsync(
                SelectedCharacter.Name, search, category, page, cancellationToken);
            if (generation == queryGeneration) results = response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (generation == queryGeneration)
            {
                operationSucceeded = false;
                message = exception.Message;
            }
        }
        finally
        {
            if (generation == queryGeneration) isLoadingTrainers = false;
        }
    }

    private void SelectTrainer(TrainerSpawn trainer)
    {
        selectedTrainer = trainer;
        confirmed = false;
        message = null;
    }

    private async Task TeleportAsync()
    {
        if (!CanTeleportSelected || selectedTrainer is null || SelectedCharacter is null) return;
        isWorking = true;
        try
        {
            var response = await AccountsClient.TeleportToTrainerAsync(
                new(SelectedCharacter.Name, selectedTrainer.SpawnId, confirmed));
            operationSucceeded = response?.Success == true;
            message = response?.Message;
            if (operationSucceeded)
            {
                confirmed = false;
                await LoadAsync(results.Page);
            }
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally { isWorking = false; }
    }

    private static string CategoryName(string value) => value switch
    {
        "class" => "Class trainer",
        "profession" => "Profession trainer",
        "weapon" => "Weapon master",
        "riding" => "Riding trainer",
        "stable" => "Stable master",
        _ => value
    };

    private static string DistanceText(TrainerSpawn trainer) =>
        trainer.SameMap && trainer.Distance is { } distance
            ? $"{Math.Round(distance):N0} yards away"
            : "Different continent/map";

    private static string? LocationDetail(TrainerSpawn trainer) => (trainer.ZoneId, trainer.AreaId) switch
    {
        (0, 0) => null,
        (_, 0) => $"Zone {trainer.ZoneId}",
        (0, _) => $"Area {trainer.AreaId}",
        _ => $"Zone {trainer.ZoneId}, area {trainer.AreaId}"
    };

    private static string MapName(ushort mapId) => mapId switch
    {
        0 => "Eastern Kingdoms",
        1 => "Kalimdor",
        530 => "Outland",
        571 => "Northrend",
        609 => "Acherus",
        _ => $"Map {mapId}"
    };

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
    }
}
