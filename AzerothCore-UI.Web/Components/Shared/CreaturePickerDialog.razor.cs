using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class CreaturePickerDialog : IDisposable
{
    private static readonly uint[] CreatureFamilies =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 20, 21, 24, 25, 26, 27, 30,
        31, 32, 33, 34, 35, 37, 38, 39, 41, 42, 43, 44, 45, 46
    ];

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<AdministrationCreature> CreatureSelected { get; set; }
    [Parameter] public EventCallback Closed { get; set; }

    private AdministrationCreatureSearchResult results = new([], 1, 30, 0, 0);
    private CancellationTokenSource? searchCancellation;
    private bool wasOpen;
    private bool isLoading;
    private string? errorMessage;
    private string search = "";
    private string filter = "all";
    private string sort = "name";
    private uint family;
    private int? minimumLevel;
    private int? maximumLevel;
    private long queryGeneration;

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
        var generation = ++queryGeneration;
        try
        {
            await Task.Delay(250, searchCancellation.Token);
            await LoadAsync(1, generation, searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task ReloadAsync() => LoadAsync(1);

    private Task LoadAsync(int page) =>
        LoadAsync(page, ++queryGeneration, CancellationToken.None);

    private async Task LoadAsync(
        int page,
        long generation,
        CancellationToken cancellationToken)
    {
        isLoading = true;
        errorMessage = null;
        try
        {
            var levelSort = sort.StartsWith("level", StringComparison.Ordinal);
            var response = await AccountsClient.GetAdministrationCreaturesAsync(
                search,
                filter,
                family,
                minimumLevel,
                maximumLevel,
                levelSort ? "level" : "name",
                sort.EndsWith("desc", StringComparison.Ordinal),
                page,
                cancellationToken);
            if (generation == queryGeneration)
                results = response;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (generation == queryGeneration)
                errorMessage = exception.Message;
        }
        finally
        {
            if (generation == queryGeneration)
                isLoading = false;
        }
    }

    private Task SelectAsync(AdministrationCreature creature) =>
        CreatureSelected.InvokeAsync(creature);

    private Task SelectFromKeyboardAsync(
        KeyboardEventArgs args,
        AdministrationCreature creature) =>
        args.Key is "Enter" or " " ? SelectAsync(creature) : Task.CompletedTask;

    private Task CloseAsync() => Closed.InvokeAsync();

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
    }

    private static string CreatureTypeName(byte type) => type switch
    {
        1 => "Beast", 2 => "Dragonkin", 3 => "Demon", 4 => "Elemental",
        5 => "Giant", 6 => "Undead", 7 => "Humanoid", 8 => "Critter",
        9 => "Mechanical", 10 => "Not specified", 11 => "Totem",
        12 => "Non-combat pet", 13 => "Gas cloud", _ => "Unknown"
    };

    private static string CreatureFamilyName(uint value) => value switch
    {
        0 => "None", 1 => "Wolf", 2 => "Cat", 3 => "Spider", 4 => "Bear",
        5 => "Boar", 6 => "Crocolisk", 7 => "Carrion Bird", 8 => "Crab",
        9 => "Gorilla", 11 => "Raptor", 12 => "Tallstrider", 20 => "Scorpid",
        21 => "Turtle", 24 => "Bat", 25 => "Hyena", 26 => "Bird of Prey",
        27 => "Wind Serpent", 30 => "Dragonhawk", 31 => "Ravager",
        32 => "Warp Stalker", 33 => "Sporebat", 34 => "Nether Ray",
        35 => "Serpent", 37 => "Moth", 38 => "Chimaera", 39 => "Devilsaur",
        41 => "Silithid", 42 => "Worm", 43 => "Rhino", 44 => "Wasp",
        45 => "Core Hound", 46 => "Spirit Beast", _ => $"Family {value}"
    };
}
