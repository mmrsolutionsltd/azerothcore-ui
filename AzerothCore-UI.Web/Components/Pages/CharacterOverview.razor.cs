using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class CharacterOverview
{
    private static readonly byte[] ClassOptions = [1, 2, 3, 4, 5, 6, 7, 8, 9, 11];

    private IReadOnlyList<CharacterOverviewSummary> characters = [];
    private string search = string.Empty;
    private byte classFilter;
    private string statusFilter = "all";
    private bool isLoading = true;
    private string? errorMessage;

    private IReadOnlyList<CharacterOverviewSummary> FilteredCharacters =>
        characters.Where(character =>
                (string.IsNullOrWhiteSpace(search)
                 || character.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                 || character.Username.Contains(search, StringComparison.OrdinalIgnoreCase))
                && (classFilter == 0 || character.Class == classFilter)
                && (statusFilter == "all"
                    || statusFilter == "online" && character.Online
                    || statusFilter == "offline" && !character.Online))
            .ToArray();

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        isLoading = true;
        errorMessage = null;
        try
        {
            characters = await AccountsClient.GetCharacterOverviewAsync();
        }
        catch (HttpRequestException)
        {
            errorMessage = "The character overview API could not be reached.";
        }
        catch (System.Text.Json.JsonException)
        {
            errorMessage = "The character overview API returned an invalid response.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private static string ClassGlyph(byte characterClass) => characterClass switch
    {
        1 => "⚔", 2 => "✦", 3 => "➶", 4 => "◆", 5 => "✧",
        6 => "☠", 7 => "ϟ", 8 => "❄", 9 => "♜", 11 => "☘", _ => "◇"
    };
}
