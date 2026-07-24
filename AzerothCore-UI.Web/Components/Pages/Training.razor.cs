using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class Training
{
    private IReadOnlyList<CharacterTrainingSummary> characters = [];
    private IReadOnlyList<ProfessionStarterCharacter> professionStarters = [];
    private IReadOnlyList<ProfessionManagementCharacter> professionManagement = [];
    private readonly HashSet<uint> collapsedCharacters = [];
    private readonly HashSet<string> collapsedDisciplines = [];
    private bool isLoading = true;
    private string? errorMessage;
    private string search = string.Empty;
    private string trainingType = "All";
    private string discipline = "All";
    private string sort = "Character";
    private string? activeTraining;
    private string? actionMessage;
    private bool actionSucceeded;
    private uint selectedCharacterGuid;
    private ushort selectedProfessionSkillId;
    private uint selectedManagementCharacterGuid;
    private ManagedProfession? pendingUnlearn;
    private const string NewProfessionActionKey = "new-profession";
    private const string UnlearnActionKey = "unlearn-profession";
    private string? SelectedProfessionGuidValue => selectedCharacterGuid == 0
        ? null : selectedCharacterGuid.ToString();
    private string? SelectedManagementGuidValue => selectedManagementCharacterGuid == 0
        ? null : selectedManagementCharacterGuid.ToString();
    private IReadOnlyList<CharacterPickerItem> ProfessionStarterPickerItems => professionStarters
        .Select(character => new CharacterPickerItem(
            character.CharacterGuid.ToString(), character.CharacterName,
            $"Level {character.CharacterLevel} · {character.PrimaryProfessionCount}/2 primary",
            character.Online))
        .ToArray();
    private IReadOnlyList<CharacterPickerItem> ProfessionManagementPickerItems => professionManagement
        .Select(character => new CharacterPickerItem(
            character.CharacterGuid.ToString(), character.CharacterName,
            $"{character.Professions.Count} known professions", character.Online))
        .ToArray();

    private ProfessionStarterCharacter? SelectedProfessionCharacter =>
        professionStarters.FirstOrDefault(character =>
            character.CharacterGuid == selectedCharacterGuid);

    private AvailableProfession? SelectedNewProfession =>
        SelectedProfessionCharacter?.AvailableProfessions.FirstOrDefault(profession =>
            profession.SkillId == selectedProfessionSkillId);

    private ProfessionManagementCharacter? SelectedManagementCharacter =>
        professionManagement.FirstOrDefault(character =>
            character.CharacterGuid == selectedManagementCharacterGuid);

    private void SelectProfessionCharacter(string? value)
    {
        selectedCharacterGuid = uint.TryParse(value, out var guid) ? guid : 0;
        SelectDefaultProfession();
    }

    private void SelectManagementCharacter(string? value)
    {
        selectedManagementCharacterGuid = uint.TryParse(value, out var guid) ? guid : 0;
        CancelUnlearn();
    }

    private IReadOnlyList<string> Disciplines => characters
        .SelectMany(character => character.Requirements)
        .Select(requirement => requirement.Discipline)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name)
        .ToArray();

    private IReadOnlyList<CharacterTrainingSummary> FilteredCharacters
    {
        get
        {
            var filtered = characters
                .Where(character => string.IsNullOrWhiteSpace(search)
                    || character.CharacterName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || character.Username.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(character => character with
                {
                    Requirements = character.Requirements
                        .Where(requirement => trainingType == "All"
                            || requirement.Category == trainingType)
                        .Where(requirement => discipline == "All"
                            || requirement.Discipline == discipline)
                        .ToArray()
                })
                .Where(character => character.Requirements.Count > 0);

            return sort switch
            {
                "Account" => filtered.OrderBy(character => character.Username)
                    .ThenBy(character => character.CharacterName).ToArray(),
                "Level" => filtered.OrderByDescending(character => character.CharacterLevel)
                    .ThenBy(character => character.CharacterName).ToArray(),
                "Count" => filtered.OrderByDescending(character => character.Requirements.Count)
                    .ThenBy(character => character.CharacterName).ToArray(),
                "Cost" => filtered.OrderByDescending(TotalCost)
                    .ThenBy(character => character.CharacterName).ToArray(),
                "Requirement" => filtered.OrderBy(NextRequirement)
                    .ThenBy(character => character.CharacterName).ToArray(),
                _ => filtered.OrderBy(character => character.CharacterName).ToArray()
            };
        }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            characters = await AccountsClient.GetAvailableTrainingAsync();
            professionStarters = await AccountsClient.GetProfessionStartersAsync();
            professionManagement = await AccountsClient.GetProfessionManagementAsync();
            selectedCharacterGuid = professionStarters
                .OrderByDescending(character => character.Online)
                .Select(character => character.CharacterGuid)
                .FirstOrDefault();
            SelectDefaultProfession();
            selectedManagementCharacterGuid = professionManagement
                .OrderByDescending(character => character.Online)
                .Select(character => character.CharacterGuid)
                .FirstOrDefault();
        }
        catch (HttpRequestException)
        {
            errorMessage = "The available training information could not be loaded.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private static string FormatRequirement(TrainingRequirement requirement) =>
        requirement.RequiredSkillRank.HasValue
            ? $"{requirement.Discipline} {requirement.RequiredSkillRank.Value}"
            : $"Level {requirement.RequiredLevel}";

    private static uint TotalCost(CharacterTrainingSummary character) =>
        character.Requirements.Aggregate(0u, (total, requirement) => total + requirement.TrainingCost);

    private static int NextRequirement(CharacterTrainingSummary character) =>
        character.Requirements.Min(requirement =>
            requirement.RequiredSkillRank ?? requirement.RequiredLevel);

    private void ToggleCharacter(uint characterGuid)
    {
        if (!collapsedCharacters.Add(characterGuid))
        {
            collapsedCharacters.Remove(characterGuid);
        }
    }

    private void ExpandAll() => collapsedCharacters.Clear();

    private static string DisciplineKey(uint characterGuid, IGrouping<string, TrainingRequirement> group) =>
        $"{characterGuid}:{group.Key}";

    private void ToggleDiscipline(string key)
    {
        if (!collapsedDisciplines.Add(key))
        {
            collapsedDisciplines.Remove(key);
        }
    }

    private void CollapseAll()
    {
        foreach (var character in FilteredCharacters)
        {
            collapsedCharacters.Add(character.CharacterGuid);
        }
    }

    private static string TrainingKey(uint characterGuid, uint spellId) =>
        $"{characterGuid}:{spellId}";

    private static string TrainerLink(string characterName, string professionName) =>
        $"/trainers?character={Uri.EscapeDataString(characterName)}" +
        $"&category=profession&search={Uri.EscapeDataString(professionName)}";

    private async Task GrantProfessionTrainingAsync(
        CharacterTrainingSummary character,
        TrainingRequirement requirement)
    {
        activeTraining = TrainingKey(character.CharacterGuid, requirement.SpellId);
        actionMessage = null;

        try
        {
            var result = await AccountsClient.GrantProfessionTrainingAsync(
                new GrantProfessionTrainingRequest(
                    character.CharacterGuid,
                    requirement.SpellId,
                    true));

            actionSucceeded = result?.Success == true;
            actionMessage = result?.Message ?? "Profession training was granted.";

            if (actionSucceeded)
            {
                characters = characters
                    .Select(item => item.CharacterGuid == character.CharacterGuid
                        ? item with
                        {
                            Requirements = item.Requirements
                                .Where(candidate => candidate.SpellId != requirement.SpellId)
                                .ToArray()
                        }
                        : item)
                    .Where(item => item.Requirements.Count > 0)
                    .ToArray();
            }
        }
        catch (HttpRequestException exception)
        {
            actionSucceeded = false;
            actionMessage = exception.Message;
        }
        finally
        {
            activeTraining = null;
        }
    }

    private void SelectDefaultProfession()
    {
        selectedProfessionSkillId = SelectedProfessionCharacter?
            .AvailableProfessions
            .Select(profession => profession.SkillId)
            .FirstOrDefault() ?? 0;
    }

    private async Task LearnProfessionAsync()
    {
        var character = SelectedProfessionCharacter;
        var profession = character?.AvailableProfessions.FirstOrDefault(item =>
            item.SkillId == selectedProfessionSkillId);
        if (character is null || profession is null)
            return;

        activeTraining = NewProfessionActionKey;
        actionMessage = null;

        try
        {
            var result = await AccountsClient.LearnProfessionAsync(
                new LearnProfessionRequest(
                    character.CharacterGuid,
                    profession.SkillId,
                    true));
            actionSucceeded = result?.Success == true;
            actionMessage = result?.Message ?? "The profession was taught.";

            if (actionSucceeded)
            {
                await RefreshProfessionDataAsync();
                if (professionStarters.All(item => item.CharacterGuid != selectedCharacterGuid))
                {
                    selectedCharacterGuid = professionStarters
                        .OrderByDescending(item => item.Online)
                        .Select(item => item.CharacterGuid)
                        .FirstOrDefault();
                }
                SelectDefaultProfession();
            }
        }
        catch (HttpRequestException exception)
        {
            actionSucceeded = false;
            actionMessage = exception.Message;
        }
        finally
        {
            activeTraining = null;
        }
    }

    private void RequestUnlearn(ManagedProfession profession) =>
        pendingUnlearn = profession;

    private void CancelUnlearn() =>
        pendingUnlearn = null;

    private async Task ConfirmUnlearnAsync()
    {
        var character = SelectedManagementCharacter;
        var profession = pendingUnlearn;
        if (character is null || profession is null)
            return;

        activeTraining = UnlearnActionKey;
        actionMessage = null;

        try
        {
            var result = await AccountsClient.UnlearnProfessionAsync(
                new UnlearnProfessionRequest(
                    character.CharacterGuid,
                    profession.SkillId,
                    true));
            actionSucceeded = result?.Success == true;
            actionMessage = result?.Message ?? "The profession was unlearned.";

            if (actionSucceeded)
            {
                pendingUnlearn = null;
                await RefreshProfessionDataAsync();
                if (professionManagement.All(item =>
                    item.CharacterGuid != selectedManagementCharacterGuid))
                {
                    selectedManagementCharacterGuid = professionManagement
                        .OrderByDescending(item => item.Online)
                        .Select(item => item.CharacterGuid)
                        .FirstOrDefault();
                }
                SelectDefaultProfession();
            }
        }
        catch (HttpRequestException exception)
        {
            actionSucceeded = false;
            actionMessage = exception.Message;
        }
        finally
        {
            activeTraining = null;
        }
    }

    private async Task RefreshProfessionDataAsync()
    {
        professionStarters = await AccountsClient.GetProfessionStartersAsync();
        professionManagement = await AccountsClient.GetProfessionManagementAsync();
        characters = await AccountsClient.GetAvailableTrainingAsync();
    }
}
