using AzerothCore_UI.Api.Controllers;
using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Controllers;

public sealed class ServerAdministrationControllerTests
{
    [Fact]
    public void CompanionCommandTargetsOneValidatedPairAndPreservesItemText()
    {
        var command = ServerAdministrationController.BuildCompanionCommand(
            "Kiesh", "Elfruid", "  give Kiesh Grizzled Bear Heart 3  ");

        Assert.Equal(
            "webadmin companion command Kiesh Elfruid give Kiesh Grizzled Bear Heart 3",
            command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("follow\nstay")]
    public void CompanionCommandRejectsEmptyOrMultipleControlLines(string command)
    {
        Assert.Throws<ArgumentException>(() =>
            ServerAdministrationController.BuildCompanionCommand(
                "Kiesh", "Elfruid", command));
    }

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
    public void SameAccountQuestingCompanionsAreTrustedAutomatically()
    {
        var candidate = new QuestingCompanionCandidate
        {
            SameAccount = true,
            SameGuild = false,
            AccountsLinked = false
        };

        Assert.True(candidate.ControlAllowed);
        Assert.Equal("Same game account", candidate.ControlReason);
    }

    [Fact]
    public void CompanionInspectionParsesLootBagsAndComparableQuestProgress()
    {
        const string output = """
            WEBADMIN_COMPANION_PROTOCOL	4
            WEBADMIN_COMPANION_QUEST	Leader	101	0	A Test Quest
            WEBADMIN_COMPANION_OBJECTIVE	Leader	101	item	2001	2	6	Test Claw
            WEBADMIN_COMPANION	Helper	12	3	1	1	14	36
            WEBADMIN_COMPANION_GATHER	Helper	Moving to Test Herb (8m).
            WEBADMIN_COMPANION_ITEM	Helper	equipment	255	15	42947	1	7	1	60	65	1	Dignified Headmaster's Charge
            WEBADMIN_COMPANION_ITEM	Helper	bag	255	23	2001	4	1	8	0	0	0	Test Claw
            WEBADMIN_COMPANION_ITEM	Helper	bag	19	2	3001	2	3	24	0	0	0	98765	18	1	1	-	Gold-flecked Gloves
            WEBADMIN_COMPANION_MAINTENANCE	Helper	1	1
            WEBADMIN_COMPANION_BEHAVIOR	Helper	dungeon-healer	healer	follow	defend	8.0	1	0	1	1
            WEBADMIN_COMPANION_LOGISTICS	Helper	4	8	1	2	Mailed 2 material stacks.
            WEBADMIN_COMPANION_DIAGNOSTIC	Helper	Following	None	Leader Leader	None	3.5	1	1	0	1	1785776500	Follow behaviour configured.	1785776400	A quest object was not usable.
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
        var tradeItem = Assert.Single(companion.Inventory, item =>
            item.ItemId == 3001);
        Assert.Equal(98765UL, tradeItem.ItemGuid);
        Assert.Equal(18, tradeItem.RequiredLevel);
        Assert.True(tradeItem.Tradeable);
        Assert.True(tradeItem.TemporaryBopTradeable);
        Assert.Empty(tradeItem.TradeRestriction);
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
        Assert.True(companion.Logistics.AutomaticEnabled);
        Assert.Equal(4, companion.Logistics.TriggerFreeSlots);
        Assert.Equal(8, companion.Logistics.TargetFreeSlots);
        Assert.Equal(2, companion.Logistics.RouteCount);
        Assert.Equal("Mailed 2 material stacks.", companion.Logistics.Status);
        Assert.Equal("Following", companion.Diagnostics.Activity);
        Assert.Equal("Leader Leader", companion.Diagnostics.Destination);
        Assert.Equal(3.5, companion.Diagnostics.DistanceFromLeader);
        Assert.True(companion.Diagnostics.Alive);
        Assert.True(companion.Diagnostics.Moving);
        Assert.Equal(1785776500, companion.Diagnostics.LastSuccessAtUnix);
        Assert.Equal("Follow behaviour configured.", companion.Diagnostics.LastSuccess);
        Assert.Equal("A quest object was not usable.", companion.Diagnostics.LastFailure);
        var equipment = Assert.Single(companion.Equipment);
        Assert.Equal((uint)42947, equipment.ItemId);
        Assert.True(equipment.Protected);
        Assert.Equal(60, equipment.Durability);
        var inventory = Assert.Single(companion.Inventory, item =>
            item.ItemId == 2001);
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
        Assert.False(companion.Logistics.AutomaticEnabled);
        Assert.Equal(0, companion.Logistics.RouteCount);
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

    [Fact]
    public void CompanionLogisticsCommandIncludesOnlyPersistedValidRoutes()
    {
        var command = ServerAdministrationController.BuildCompanionLogisticsCommand(
            "Leader", "Helper", new CompanionLogisticsSettings(4, 9, true),
            [
                new StoredCompanionLogisticsRoute("cloth", 12, 40, true),
                new StoredCompanionLogisticsRoute("metal", 13, 0, true),
                new StoredCompanionLogisticsRoute("catchall", 14, 0, true),
                new StoredCompanionLogisticsRoute("disenchant", 15, 0, true),
                new StoredCompanionLogisticsRoute("herbs", 99, 20, true)
            ],
            new Dictionary<uint, string>
            {
                [12] = "Tailor", [13] = "Smith", [14] = "Banker",
                [15] = "Enchanter"
            });

        Assert.Equal(
            "webadmin companion logistics Leader Helper 4 9 1 "
            + "cloth Tailor 40 disenchant Enchanter 0 metal Smith 0 catchall Banker 0",
            command);
    }

    [Fact]
    public void CompanionLogisticsPreviewCommandCarriesPolicyWithoutChangingIt()
    {
        var command = ServerAdministrationController
            .BuildCompanionLogisticsPreviewCommand(
                "Leader", "Helper",
                [
                    new StoredCompanionLogisticsRoute("catchall", 14, 0, true),
                    new StoredCompanionLogisticsRoute("cloth", 12, 40, true),
                    new StoredCompanionLogisticsRoute("herbs", 99, 20, true)
                ],
                new Dictionary<uint, string>
                {
                    [12] = "Tailor", [14] = "Banker"
                });

        Assert.Equal(
            "webadmin companion logistics-preview Leader Helper "
            + "cloth Tailor 40 catchall Banker 0",
            command);
    }

    [Fact]
    public void CompanionLogisticsPreviewParsesSummaryAndEveryDecision()
    {
        const string output = """
            WEBADMIN_LOGISTICS_PREVIEW	Helper	2	36	6	60	0	1
            WEBADMIN_LOGISTICS_PREVIEW_ITEM	4306	20	1	19	3	Mail	Tailor	Matches the cloth route above its configured reserve.	Silk Cloth
            WEBADMIN_LOGISTICS_PREVIEW_ITEM	7073	1	0	255	24	Sell	Nearby vendor	Grey-quality vendor item.	Broken Fang
            WEBADMIN_LOGISTICS_PREVIEW_ITEM	6948	1	1	255	23	Protected	Companion bags	The item cannot be traded.	Hearthstone
            WEBADMIN_LOGISTICS_PREVIEW_ITEM	1179	5	1	20	1	Keep	Companion bags	PlayerBots does not consider this item routing surplus.	Ice Cold Milk
            """;

        var preview = ServerAdministrationController
            .ParseCompanionLogisticsPreview(output, "Requested");

        Assert.Equal("Helper", preview.CompanionName);
        Assert.Equal(2, preview.CurrentFreeSlots);
        Assert.Equal(36, preview.TotalBagSlots);
        Assert.Equal(6, preview.PotentialFreeSlots);
        Assert.Equal(60, preview.PostageCopper);
        Assert.False(preview.MailboxNearby);
        Assert.True(preview.VendorNearby);
        Assert.Collection(preview.Items,
            item =>
            {
                Assert.Equal((uint)4306, item.ItemId);
                Assert.Equal(19, item.Bag);
                Assert.Equal(3, item.Slot);
                Assert.Equal("Mail", item.Action);
                Assert.Equal("Tailor", item.Destination);
                Assert.Equal("Silk Cloth", item.Name);
            },
            item => Assert.Equal("Sell", item.Action),
            item => Assert.Equal("Protected", item.Action),
            item => Assert.Equal("Keep", item.Action));
    }

    [Fact]
    public void CompanionLogisticsPreviewPreservesBridgeDiagnostic()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ServerAdministrationController.ParseCompanionLogisticsPreview(
                "Unknown command: logistics-preview", "Helper"));

        Assert.Equal("Unknown command: logistics-preview", exception.Message);
    }
}
