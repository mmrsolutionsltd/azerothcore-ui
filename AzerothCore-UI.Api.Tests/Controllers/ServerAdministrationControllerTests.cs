using AzerothCore_UI.Api.Controllers;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Controllers;

public sealed class ServerAdministrationControllerTests
{
    [Fact]
    public void QuestingCompanionLevelOrderingUsesSignedArithmetic()
    {
        var query = ServerAdministrationController.QuestingCompanionCandidateSql;

        Assert.Contains(
            "ABS(CAST(c.level AS SIGNED) - CAST(@LeaderLevel AS SIGNED))",
            query,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "ABS(c.level - @LeaderLevel)",
            query,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompanionInspectionParsesLootBagsAndComparableQuestProgress()
    {
        const string output = """
            WEBADMIN_COMPANION_PROTOCOL	3
            WEBADMIN_COMPANION_QUEST	Leader	101	0	A Test Quest
            WEBADMIN_COMPANION_OBJECTIVE	Leader	101	item	2001	2	6	Test Claw
            WEBADMIN_COMPANION	Helper	12	3	1	1	14	36
            WEBADMIN_COMPANION_GATHER	Helper	Moving to Test Herb (8m).
            WEBADMIN_COMPANION_ITEM	Helper	equipment	255	15	42947	1	7	1	60	65	1	Dignified Headmaster's Charge
            WEBADMIN_COMPANION_ITEM	Helper	bag	255	23	2001	4	1	8	0	0	0	Test Claw
            WEBADMIN_COMPANION_MAINTENANCE	Helper	1	1
            WEBADMIN_COMPANION_BEHAVIOR	Helper	dungeon-healer	healer	follow	defend	8.0	1	0	1	1
            WEBADMIN_COMPANION_EQUIPMENT_CHANGE	Helper	1785776400	Main hand: Withered Staff -> Dignified Headmaster's Charge
            WEBADMIN_COMPANION_INVENTORY_CHANGE	Helper	1785776401	Added Test Claw x2
            WEBADMIN_COMPANION_QUEST	Helper	101	0	A Test Quest
            WEBADMIN_COMPANION_OBJECTIVE	Helper	101	item	2001	4	6	Test Claw
            WEBADMIN_COMPANION_OBJECTIVE	Helper	101	creature	3001	3	8	Test Beast
            WEBADMIN_COMPANION_SUMMARY	Leader	1
            """;

        var inspection =
            ServerAdministrationController.ParseQuestingCompanionInspection(
                output, "Leader");

        var companion = Assert.Single(inspection.ActiveCompanions);
        Assert.Equal("Helper", companion.Name);
        Assert.True(companion.InLeaderParty);
        Assert.True(companion.LootEnabled);
        Assert.Equal(14, companion.FreeBagSlots);
        Assert.Equal(36, companion.TotalBagSlots);
        Assert.Equal("Moving to Test Herb (8m).", companion.QuestObjectStatus);
        Assert.True(companion.AutoSellTrash);
        Assert.True(companion.AutoRepair);
        Assert.Equal("dungeon-healer", companion.Behavior.Preset);
        Assert.Equal("healer", companion.Behavior.Role);
        Assert.Equal("follow", companion.Behavior.Movement);
        Assert.Equal("defend", companion.Behavior.CombatFocus);
        Assert.Equal(8, companion.Behavior.FollowDistance);
        Assert.True(companion.Behavior.LootEnabled);
        Assert.False(companion.Behavior.GatherEnabled);
        var equipment = Assert.Single(companion.Equipment);
        Assert.Equal((uint)42947, equipment.ItemId);
        Assert.True(equipment.Protected);
        Assert.Equal(60, equipment.Durability);
        var inventory = Assert.Single(companion.Inventory);
        Assert.Equal((uint)2001, inventory.ItemId);
        Assert.Equal(4, inventory.Count);
        var equipmentChange = Assert.Single(companion.RecentEquipmentChanges);
        Assert.Contains("Withered Staff", equipmentChange.Description);
        var inventoryChange = Assert.Single(companion.RecentInventoryChanges);
        Assert.Equal("Added Test Claw x2", inventoryChange.Description);
        var companionQuest = Assert.Single(companion.Quests);
        Assert.Equal((uint)101, companionQuest.QuestId);
        Assert.Equal(2, companionQuest.Objectives.Count);
        Assert.Equal(4, companionQuest.Objectives[0].Current);

        var leaderQuest = Assert.Single(inspection.LeaderQuests);
        Assert.Equal(2, Assert.Single(leaderQuest.Objectives).Current);
    }

    [Fact]
    public void CompanionInspectionAcceptsOlderModuleOutput()
    {
        const string output = "WEBADMIN_COMPANION\tHelper\t12\t3\t1";

        var inspection =
            ServerAdministrationController.ParseQuestingCompanionInspection(
                output, "Leader");

        var companion = Assert.Single(inspection.ActiveCompanions);
        Assert.False(companion.LootEnabled);
        Assert.Equal(0, companion.TotalBagSlots);
        Assert.False(companion.AutoSellTrash);
        Assert.False(companion.AutoRepair);
        Assert.Empty(companion.Equipment);
        Assert.Empty(companion.Inventory);
        Assert.Empty(companion.RecentEquipmentChanges);
        Assert.Empty(companion.RecentInventoryChanges);
        Assert.Equal("legacy", companion.Behavior.Preset);
        Assert.Equal("auto", companion.Behavior.Role);
        Assert.Equal("follow", companion.Behavior.Movement);
        Assert.True(companion.Behavior.GatherEnabled);
        Assert.Empty(companion.Quests);
        Assert.Empty(companion.QuestObjectStatus);
        Assert.Empty(inspection.LeaderQuests);
        Assert.Equal(0, inspection.ProtocolVersion);
        Assert.Null(inspection.Error);
    }

    [Fact]
    public void CompanionInspectionParsesVersionMultipleBotsFullBagsAndMissingObjectives()
    {
        const string output = """
            WEBADMIN_COMPANION_PROTOCOL	1
            WEBADMIN_COMPANION_QUEST	Leader	200	0	Doom Weed
            WEBADMIN_COMPANION_OBJECTIVE	Leader	200	item	900	9	10	Doom Weed
            WEBADMIN_COMPANION	Fullbags	15	3	1	1	0	20
            WEBADMIN_COMPANION_QUEST	Fullbags	200	1	Doom Weed
            WEBADMIN_COMPANION_OBJECTIVE	Fullbags	200	item	900	10	10	Doom Weed
            WEBADMIN_COMPANION	Missing	15	5	1	1	8	36
            WEBADMIN_COMPANION_QUEST	Missing	200	0	Doom Weed
            WEBADMIN_COMPANION_SUMMARY	Leader	2
            """;

        var inspection =
            ServerAdministrationController.ParseQuestingCompanionInspection(
                output, "Leader");

        Assert.Equal(1, inspection.ProtocolVersion);
        Assert.Null(inspection.Error);
        Assert.Equal(2, inspection.ActiveCompanions.Count);
        var fullBags = inspection.ActiveCompanions.Single(value =>
            value.Name == "Fullbags");
        Assert.Equal(0, fullBags.FreeBagSlots);
        Assert.Equal(20, fullBags.TotalBagSlots);
        Assert.True(Assert.Single(fullBags.Quests).Complete);
        var missing = inspection.ActiveCompanions.Single(value =>
            value.Name == "Missing");
        Assert.Empty(Assert.Single(missing.Quests).Objectives);
    }

    [Fact]
    public void CompanionInspectionPreservesServerErrorsForDiagnostics()
    {
        const string output = """
            WEBADMIN_COMPANION_PROTOCOL	1
            Players may inspect only their own questing companions.
            """;

        var inspection =
            ServerAdministrationController.ParseQuestingCompanionInspection(
                output, "Leader");

        Assert.Equal(1, inspection.ProtocolVersion);
        Assert.Equal(
            "Players may inspect only their own questing companions.",
            inspection.Error);
    }
}
