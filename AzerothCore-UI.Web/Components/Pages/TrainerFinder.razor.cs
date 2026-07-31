using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class TrainerFinder
{
    [SupplyParameterFromQuery(Name = "character")]
    public string? InitialCharacter { get; set; }

    [SupplyParameterFromQuery(Name = "category")]
    public string? InitialCategory { get; set; }

    [SupplyParameterFromQuery(Name = "search")]
    public string? InitialSearch { get; set; }

    private IReadOnlyList<AdministrationPlayer> players = [];
    private bool isLoading = true;
    private string characterName = "";
    private string? errorMessage;
    private IReadOnlyList<CharacterPickerItem> PickerItems =>
        CharacterPickerItem.FromAdministrationPlayers(players);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            players = await AccountsClient.GetAdministrationPlayersAsync();
            characterName = players.Any(player =>
                player.Name.Equals(InitialCharacter, StringComparison.OrdinalIgnoreCase))
                ? InitialCharacter!
                : "";
        }
        catch (Exception exception)
        {
            errorMessage = $"Characters could not be loaded: {exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void SelectCharacter(string? value) => characterName = value ?? "";
}
