using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class CreatureSpawner
{
    private static readonly uint[] CreatureFamilies = [1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 20, 21, 24, 25, 26, 27, 30, 31, 32, 33, 34, 35, 37, 38, 39, 41, 42, 43, 44, 45, 46];
    private AdministrationCreatureSearchResult creatureResults = new([], 1, 30, 0, 0);
    private IReadOnlyList<AdministrationPlayer> administrationPlayers = [];
    private IEnumerable<AdministrationPlayer> OrderedOnlinePlayers => administrationPlayers
        .Where(player => player.Online && !player.IsPlayerBot)
        .OrderBy(player => player.PickerOrder).ThenBy(player => player.Name);
    private CancellationTokenSource? searchCancellation;
    private ServerStatus? status;
    private AdministrationCreature? selectedCreature;
    private bool isLoadingPage = true, isLoadingCreatures, isWorking, operationSucceeded, confirmCreatureSpawn;
    private string? message;
    private string creatureSearch = "", creatureFilter = "tameable", creatureSort = "name", creatureAnchor = "";
    private uint creatureFamily;
    private int? creatureMinimumLevel, creatureMaximumLevel;
    private int creatureLevel = 1, creatureDespawnMinutes = 10;
    private long creatureQueryGeneration;
    private bool CanSpawn => status is { WorldServer.IsRunning: true, SoapConfigured: true } && !isWorking;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            status = await AccountsClient.GetServerStatusAsync();
            administrationPlayers = await AccountsClient.GetAdministrationPlayersAsync();
            await LoadCreaturesAsync(1);
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally { isLoadingPage = false; }
    }

    private async Task OnCreatureSearchAsync(ChangeEventArgs args)
    {
        creatureSearch = args.Value?.ToString() ?? "";
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        var generation = ++creatureQueryGeneration;
        try
        {
            await Task.Delay(250, searchCancellation.Token);
            await LoadCreaturesAsync(1, generation);
        }
        catch (OperationCanceledException) { }
    }

    private Task ReloadAsync() => LoadCreaturesAsync(1);

    private Task LoadCreaturesAsync(int page) => LoadCreaturesAsync(page, ++creatureQueryGeneration);

    private async Task LoadCreaturesAsync(int page, long generation)
    {
        isLoadingCreatures = true;
        try
        {
            var levelSort = creatureSort.StartsWith("level", StringComparison.Ordinal);
            var results = await AccountsClient.GetAdministrationCreaturesAsync(creatureSearch, creatureFilter,
                creatureFamily, creatureMinimumLevel, creatureMaximumLevel, levelSort ? "level" : "name",
                creatureSort.EndsWith("desc", StringComparison.Ordinal), page);
            if (generation == creatureQueryGeneration) creatureResults = results;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally
        {
            if (generation == creatureQueryGeneration) isLoadingCreatures = false;
        }
    }

    private void SelectCreature(AdministrationCreature creature)
    {
        selectedCreature = creature;
        creatureLevel = creature.MinimumLevel;
        confirmCreatureSpawn = false;
    }

    private async Task SpawnCreatureAsync()
    {
        if (selectedCreature is null || isWorking) return;
        isWorking = true;
        try
        {
            var result = await AccountsClient.SpawnCreatureAsync(new(creatureAnchor, selectedCreature.CreatureId,
                creatureLevel, creatureDespawnMinutes, confirmCreatureSpawn));
            operationSucceeded = result?.Success == true;
            message = result?.Message;
            if (operationSucceeded) confirmCreatureSpawn = false;
        }
        catch (Exception exception) { operationSucceeded = false; message = exception.Message; }
        finally
        {
            isWorking = false;
            try { status = await AccountsClient.GetServerStatusAsync(); }
            catch (Exception exception) { operationSucceeded = false; message = $"Status refresh failed: {exception.Message}"; }
        }
    }

    private static string CreatureTypeName(byte type) => type switch
    {
        1 => "Beast", 2 => "Dragonkin", 3 => "Demon", 4 => "Elemental", 5 => "Giant", 6 => "Undead",
        7 => "Humanoid", 8 => "Critter", 9 => "Mechanical", 10 => "Not specified", 11 => "Totem",
        12 => "Non-combat pet", 13 => "Gas cloud", _ => "Unknown"
    };

    private static string CreatureFamilyName(uint family) => family switch
    {
        0 => "None", 1 => "Wolf", 2 => "Cat", 3 => "Spider", 4 => "Bear", 5 => "Boar", 6 => "Crocolisk",
        7 => "Carrion Bird", 8 => "Crab", 9 => "Gorilla", 11 => "Raptor", 12 => "Tallstrider", 20 => "Scorpid",
        21 => "Turtle", 24 => "Bat", 25 => "Hyena", 26 => "Bird of Prey", 27 => "Wind Serpent",
        30 => "Dragonhawk", 31 => "Ravager", 32 => "Warp Stalker", 33 => "Sporebat", 34 => "Nether Ray",
        35 => "Serpent", 37 => "Moth", 38 => "Chimaera", 39 => "Devilsaur", 41 => "Silithid", 42 => "Worm",
        43 => "Rhino", 44 => "Wasp", 45 => "Core Hound", 46 => "Spirit Beast", _ => $"Family {family}"
    };
}
