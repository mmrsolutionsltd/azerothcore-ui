using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages
{
    public partial class CharacterDetails
    {
        [Parameter] public long CharacterGuid { get; set; }
        [SupplyParameterFromQuery(Name = "tab")]
        public string? InitialTab { get; set; }

        private static readonly string[] tabs = ["Inventory", "Quests", "Professions", "Training", "Reputation"];
        private AzerothCore_UI.Web.Models.CharacterDetails? character;
        private IReadOnlyList<EquippedItem> equippedItems = [];
        private IReadOnlyList<BagItem> bagItems = [];
        private IReadOnlyList<CharacterQuest> quests = [];
        private IReadOnlyList<CompletedCharacterQuest> completedQuests = [];
        private IReadOnlyList<CharacterProfession> professions = [];
        private IReadOnlyList<MissingVendorRecipe> missingVendorRecipes = [];
        private IReadOnlyList<MissingQuestRecipe> missingQuestRecipes = [];
        private IReadOnlyList<MissingLootRecipe> missingLootRecipes = [];
        private IReadOnlyList<MissingUnclassifiedRecipe> unclassifiedRecipes = [];
        private IReadOnlyList<MissingClassSpell> missingClassSpells = [];
        private IReadOnlyList<MissingProfessionSpell> missingProfessionSpells = [];
        private string activeTab = "Inventory";
        private string inventoryView = "Equipped";
        private string questView = "Current";
        private string professionView = "Skills";
        private string vendorRecipeProfession = "All";
        private string vendorRecipeSearch = string.Empty;
        private string vendorRecipeAvailability = "All";
        private string questRecipeProfession = "All";
        private string questRecipeSearch = string.Empty;
        private string trainingView = "Class abilities";
        private bool isLoading = true;
        private bool inventoryIsLoading;
        private bool inventoryHasLoaded;
        private bool bagItemsAreLoading;
        private bool bagItemsHaveLoaded;
        private bool questsAreLoading;
        private bool questsHaveLoaded;
        private bool completedQuestsAreLoading;
        private bool completedQuestsHaveLoaded;
        private bool professionsAreLoading;
        private bool professionsHaveLoaded;
        private bool vendorRecipesAreLoading;
        private bool vendorRecipesHaveLoaded;
        private bool questRecipesAreLoading;
        private bool questRecipesHaveLoaded;
        private bool otherRecipesAreLoading;
        private bool otherRecipesHaveLoaded;
        private bool trainingIsLoading;
        private bool trainingHasLoaded;
        private string? errorMessage;
        private string? inventoryErrorMessage;
        private string? bagItemsErrorMessage;
        private string? questErrorMessage;
        private string? completedQuestErrorMessage;
        private string? professionsErrorMessage;
        private string? vendorRecipesErrorMessage;
        private string? questRecipesErrorMessage;
        private string? otherRecipesErrorMessage;
        private string? trainingErrorMessage;

        protected override async Task OnParametersSetAsync()
        {
            isLoading = true;
            errorMessage = null;
            activeTab = tabs.FirstOrDefault(tab =>
                string.Equals(tab, InitialTab, StringComparison.OrdinalIgnoreCase)) ?? "Inventory";
            equippedItems = [];
            inventoryIsLoading = false;
            inventoryHasLoaded = false;
            inventoryErrorMessage = null;
            inventoryView = "Equipped";
            bagItems = [];
            bagItemsHaveLoaded = false;
            bagItemsErrorMessage = null;
            quests = [];
            questsHaveLoaded = false;
            questErrorMessage = null;
            questView = "Current";
            completedQuests = [];
            completedQuestsHaveLoaded = false;
            completedQuestErrorMessage = null;
            professions = [];
            missingVendorRecipes = [];
            missingQuestRecipes = [];
            missingLootRecipes = [];
            unclassifiedRecipes = [];
            professionsHaveLoaded = false;
            professionsErrorMessage = null;
            professionView = "Skills";
            vendorRecipeProfession = "All";
            vendorRecipeSearch = string.Empty;
            vendorRecipeAvailability = "All";
            vendorRecipesAreLoading = false;
            vendorRecipesHaveLoaded = false;
            vendorRecipesErrorMessage = null;
            questRecipeProfession = "All";
            questRecipeSearch = string.Empty;
            questRecipesAreLoading = false;
            questRecipesHaveLoaded = false;
            questRecipesErrorMessage = null;
            otherRecipesAreLoading = false;
            otherRecipesHaveLoaded = false;
            otherRecipesErrorMessage = null;
            missingClassSpells = [];
            missingProfessionSpells = [];
            trainingHasLoaded = false;
            trainingErrorMessage = null;
            trainingView = "Class abilities";

            if (CharacterGuid is < 0 or > uint.MaxValue)
            {
                errorMessage = "The character GUID is invalid.";
                isLoading = false;
                return;
            }

            try
            {
                character = await AccountsClient.GetCharacterAsync((uint)CharacterGuid);

                if (character is not null)
                {
                    await Task.WhenAll(
                        LoadInventoryAsync(character.Guid),
                        LoadTrainingAsync(character.Guid));
                }
            }
            catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                errorMessage = "The character was not found.";
            }
            catch (HttpRequestException)
            {
                errorMessage = "The character API could not be reached.";
            }
            finally
            {
                isLoading = false;
            }
        }

        private static string FormatPlayedTime(uint seconds) =>
            TimeSpan.FromSeconds(seconds).ToString(@"d\d\ hh\h\ mm\m");

        private async Task SelectTabAsync(string tab)
        {
            activeTab = tab;

            if (character is null)
            {
                return;
            }

            if (tab == "Inventory" && !inventoryHasLoaded)
            {
                await LoadInventoryAsync(character.Guid);
                return;
            }

            if (tab == "Professions" && !professionsHaveLoaded)
            {
                await LoadProfessionsAsync(character.Guid);
                return;
            }

            if (tab == "Training" && !trainingHasLoaded)
            {
                await LoadTrainingAsync(character.Guid);
                return;
            }

            if (tab != "Quests" || questsHaveLoaded)
            {
                return;
            }

            questsAreLoading = true;
            questErrorMessage = null;

            try
            {
                quests = await AccountsClient.GetCharacterQuestsAsync(character.Guid);
                questsHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                questErrorMessage = "The character quests could not be loaded.";
            }
            finally
            {
                questsAreLoading = false;
            }
        }

        private async Task LoadInventoryAsync(uint guid)
        {
            inventoryIsLoading = true;
            inventoryErrorMessage = null;

            try
            {
                equippedItems = await AccountsClient.GetEquippedItemsAsync(guid);
                inventoryHasLoaded = true;
            }
            catch (HttpRequestException)
            {
                inventoryErrorMessage = "The equipped inventory could not be loaded.";
            }
            finally
            {
                inventoryIsLoading = false;
            }
        }

        private static string GetEquipmentSlotName(byte slot) => slot switch
        {
            0 => "Head", 1 => "Neck", 2 => "Shoulders", 3 => "Shirt",
            4 => "Chest", 5 => "Waist", 6 => "Legs", 7 => "Feet",
            8 => "Wrists", 9 => "Hands", 10 => "Finger 1", 11 => "Finger 2",
            12 => "Trinket 1", 13 => "Trinket 2", 14 => "Back",
            15 => "Main hand", 16 => "Off hand", 17 => "Ranged", 18 => "Tabard",
            _ => $"Unknown ({slot})"
        };

        private static string GetItemQualityName(byte quality) => quality switch
        {
            0 => "Poor", 1 => "Common", 2 => "Uncommon", 3 => "Rare",
            4 => "Epic", 5 => "Legendary", 6 => "Artifact", 7 => "Heirloom",
            _ => $"Unknown ({quality})"
        };

        private static string GetItemQualityClass(byte quality) => quality switch
        {
            0 => "text-secondary", 2 => "text-success", 3 => "text-primary",
            4 => "text-info", 5 => "text-warning", 6 => "text-danger",
            7 => "item-quality-heirloom",
            _ => string.Empty
        };

        private static string FormatDurability(EquippedItem item) =>
            item.MaxDurability == 0 ? "—" : $"{item.Durability} / {item.MaxDurability}";

        private static int GetBagSlotNumber(byte slot) => slot + 1;

        private static string? GetDurabilityRowClass(EquippedItem item)
        {
            if (item.MaxDurability == 0)
            {
                return null;
            }

            if (item.Durability == 0)
            {
                return "table-danger";
            }

            return item.Durability * 100 / item.MaxDurability <= 25
                ? "table-warning"
                : null;
        }

        private void ShowEquippedItems() => inventoryView = "Equipped";

        private async Task ShowBagItemsAsync()
        {
            inventoryView = "Bags";

            if (bagItemsHaveLoaded || character is null)
            {
                return;
            }

            bagItemsAreLoading = true;
            bagItemsErrorMessage = null;

            try
            {
                bagItems = await AccountsClient.GetBagItemsAsync(character.Guid);
                bagItemsHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                bagItemsErrorMessage = "The bag contents could not be loaded.";
            }
            finally
            {
                bagItemsAreLoading = false;
            }
        }

        private static string GetQuestStatus(byte status) => status switch
        {
            1 => "Complete",
            3 => "In progress",
            5 => "Failed",
            6 => "Rewarded",
            _ => $"Unknown ({status})"
        };

        private async Task LoadProfessionsAsync(uint guid)
        {
            professionsAreLoading = true;
            professionsErrorMessage = null;

            try
            {
                professions = await AccountsClient.GetCharacterProfessionsAsync(guid);
                professionsHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                professionsErrorMessage = "The character professions could not be loaded.";
            }
            finally
            {
                professionsAreLoading = false;
            }
        }

        private static int GetProfessionProgress(CharacterProfession profession) =>
            profession.Maximum == 0
                ? 0
                : Math.Clamp(profession.Value * 100 / profession.Maximum, 0, 100);

        private void ShowProfessionSkills() => professionView = "Skills";

        private async Task ShowMissingVendorRecipesAsync()
        {
            professionView = "Vendor recipes";

            if (vendorRecipesHaveLoaded || character is null)
            {
                return;
            }

            vendorRecipesAreLoading = true;
            vendorRecipesErrorMessage = null;

            try
            {
                missingVendorRecipes = await AccountsClient.GetMissingVendorRecipesAsync(character.Guid);
                vendorRecipesHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                vendorRecipesErrorMessage = "The missing vendor recipes could not be loaded.";
            }
            finally
            {
                vendorRecipesAreLoading = false;
            }
        }

        private IEnumerable<MissingVendorRecipe> FilteredVendorRecipes => missingVendorRecipes
            .Where(recipe => vendorRecipeProfession == "All"
                || recipe.ProfessionName == vendorRecipeProfession)
            .Where(recipe => vendorRecipeAvailability == "All"
                || (vendorRecipeAvailability == "Available" && recipe.ReputationRequirementMet)
                || (vendorRecipeAvailability == "Locked" && !recipe.ReputationRequirementMet))
            .Where(recipe => string.IsNullOrWhiteSpace(vendorRecipeSearch)
                || recipe.ItemName.Contains(vendorRecipeSearch, StringComparison.OrdinalIgnoreCase)
                || (recipe.RecipeName?.Contains(vendorRecipeSearch, StringComparison.OrdinalIgnoreCase) ?? false)
                || recipe.VendorNames.Contains(vendorRecipeSearch, StringComparison.OrdinalIgnoreCase));

        private static string ReputationRankName(byte rank) => rank switch
        {
            0 => "Hated", 1 => "Hostile", 2 => "Unfriendly", 3 => "Neutral",
            4 => "Friendly", 5 => "Honored", 6 => "Revered", 7 => "Exalted",
            _ => $"Rank {rank}"
        };

        private async Task ShowMissingQuestRecipesAsync()
        {
            professionView = "Quest rewards";

            if (questRecipesHaveLoaded || character is null)
            {
                return;
            }

            questRecipesAreLoading = true;
            questRecipesErrorMessage = null;

            try
            {
                missingQuestRecipes = await AccountsClient.GetMissingQuestRecipesAsync(character.Guid);
                questRecipesHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                questRecipesErrorMessage = "The missing quest reward recipes could not be loaded.";
            }
            finally
            {
                questRecipesAreLoading = false;
            }
        }

        private IEnumerable<MissingQuestRecipe> FilteredQuestRecipes => missingQuestRecipes
            .Where(recipe => questRecipeProfession == "All"
                || recipe.ProfessionName == questRecipeProfession)
            .Where(recipe => string.IsNullOrWhiteSpace(questRecipeSearch)
                || recipe.ItemName.Contains(questRecipeSearch, StringComparison.OrdinalIgnoreCase)
                || (recipe.RecipeName?.Contains(questRecipeSearch, StringComparison.OrdinalIgnoreCase) ?? false)
                || recipe.QuestTitle.Contains(questRecipeSearch, StringComparison.OrdinalIgnoreCase));

        private async Task ShowOtherRecipesAsync()
        {
            professionView = "Drops and other";
            if (otherRecipesHaveLoaded || character is null) return;
            otherRecipesAreLoading = true;
            otherRecipesErrorMessage = null;
            try
            {
                var lootTask = AccountsClient.GetMissingLootRecipesAsync(character.Guid);
                var otherTask = AccountsClient.GetUnclassifiedRecipesAsync(character.Guid);
                await Task.WhenAll(lootTask, otherTask);
                missingLootRecipes = await lootTask;
                unclassifiedRecipes = await otherTask;
                otherRecipesHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                otherRecipesErrorMessage = "The loot and unclassified recipes could not be loaded.";
            }
            finally
            {
                otherRecipesAreLoading = false;
            }
        }

        private async Task LoadTrainingAsync(uint guid)
        {
            trainingIsLoading = true;
            trainingErrorMessage = null;

            try
            {
                var classTraining = AccountsClient.GetMissingClassSpellsAsync(guid);
                var professionTraining = AccountsClient.GetMissingProfessionSpellsAsync(guid);
                await Task.WhenAll(classTraining, professionTraining);
                missingClassSpells = await classTraining;
                missingProfessionSpells = await professionTraining;
                trainingHasLoaded = true;
            }
            catch (HttpRequestException)
            {
                trainingErrorMessage = "The class training information could not be loaded.";
            }
            finally
            {
                trainingIsLoading = false;
            }
        }

        private int AvailableTrainingCount =>
            missingClassSpells.Count + missingProfessionSpells.Count;

        private void ShowClassTraining() => trainingView = "Class abilities";

        private void ShowProfessionTraining() => trainingView = "Profession recipes";

        private void ShowCurrentQuests() => questView = "Current";

        private async Task ShowCompletedQuestsAsync()
        {
            questView = "Completed";

            if (completedQuestsHaveLoaded || character is null)
            {
                return;
            }

            completedQuestsAreLoading = true;
            completedQuestErrorMessage = null;

            try
            {
                completedQuests = await AccountsClient.GetCompletedCharacterQuestsAsync(character.Guid);
                completedQuestsHaveLoaded = true;
            }
            catch (HttpRequestException)
            {
                completedQuestErrorMessage = "The completed quest history could not be loaded.";
            }
            finally
            {
                completedQuestsAreLoading = false;
            }
        }
    }
}
