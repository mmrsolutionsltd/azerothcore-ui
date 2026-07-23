using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public static class StarterPresetPlanner
{
    public const uint BagItemId = 4245;
    public const uint HearthstoneItemId = 6948;
    private static readonly StarterItem Food = new(117, "Tough Jerky");
    private static readonly StarterItem Drink = new(159, "Refreshing Spring Water");

    private static readonly IReadOnlyDictionary<byte, StarterItem[]> Heirlooms =
        new Dictionary<byte, StarterItem[]>
        {
            [1] = [new(42943, "Bloodied Arcanite Reaper"), new(42949, "Polished Spaulders of Valor"), new(48685, "Polished Breastplate of Valor")],
            [2] = [new(44092, "Reforged Truesilver Champion"), new(44100, "Pristine Lightforge Spaulders"), new(48685, "Polished Breastplate of Valor")],
            [3] = [new(42946, "Charmed Ancient Bone Bow"), new(44101, "Prized Beastmaster's Mantle"), new(48677, "Champion's Deathdealer Breastplate")],
            [4] = [new(42944, "Balanced Heartseeker"), new(42952, "Stained Shadowcraft Spaulders"), new(48689, "Stained Shadowcraft Tunic")],
            [5] = [new(42947, "Dignified Headmaster's Charge"), new(42985, "Tattered Dreadmist Mantle"), new(48691, "Tattered Dreadmist Robe")],
            [6] = [new(42943, "Bloodied Arcanite Reaper"), new(42949, "Polished Spaulders of Valor"), new(48685, "Polished Breastplate of Valor")],
            [7] = [new(42948, "Devout Aurastone Hammer"), new(42951, "Mystical Pauldrons of Elements"), new(48683, "Mystical Vest of Elements")],
            [8] = [new(42947, "Dignified Headmaster's Charge"), new(42985, "Tattered Dreadmist Mantle"), new(48691, "Tattered Dreadmist Robe")],
            [9] = [new(42947, "Dignified Headmaster's Charge"), new(42985, "Tattered Dreadmist Mantle"), new(48691, "Tattered Dreadmist Robe")],
            [11] = [new(42948, "Devout Aurastone Hammer"), new(42984, "Preened Ironfeather Shoulders"), new(48687, "Preened Ironfeather Breastplate")]
        };

    public static IReadOnlyList<StarterPresetAction> Plan(
        string preset, byte characterClass, byte characterLevel, bool online,
        IReadOnlyDictionary<uint, int> owned,
        int bagCount, bool includeHeirlooms, bool includeHearthstone,
        bool includeFoodAndDrink, bool includeClassSupplies, int moneyGold)
    {
        var delivery = online ? "Direct" : "Mail";
        var actions = new List<StarterPresetAction>();
        if (includeHeirlooms && Heirlooms.TryGetValue(characterClass, out var heirlooms))
        {
            foreach (var item in heirlooms)
                actions.Add(ItemAction("Heirloom", item, 1, delivery, owned.ContainsKey(item.Id),
                    "Already owned"));
        }

        var ownedBags = owned.GetValueOrDefault(BagItemId);
        var missingBags = Math.Max(0, bagCount - ownedBags);
        actions.Add(ItemAction("Bag", new(BagItemId, "Small Silk Pack"), missingBags, delivery,
            missingBags == 0, $"Already owns {ownedBags} suitable bag(s)"));

        if (includeHearthstone)
            actions.Add(ItemAction("Utility", new(HearthstoneItemId, "Hearthstone"), 1, delivery,
                owned.ContainsKey(HearthstoneItemId), "Already owned"));
        if (includeFoodAndDrink)
        {
            var quantity = preset == "new" ? 20 : 40;
            actions.Add(ItemAction("Supply", Food, quantity, delivery, false, null));
            actions.Add(ItemAction("Supply", Drink, quantity, delivery, false, null));
        }
        if (includeClassSupplies && characterClass == 3)
            actions.Add(ItemAction("Class supply",
                characterLevel >= 10 ? new(2515, "Sharp Arrow") : new(2512, "Rough Arrow"),
                1000, delivery, false, null));
        if (includeClassSupplies && characterClass == 9)
            actions.Add(ItemAction("Class supply", new(6265, "Soul Shard"), 10, delivery, false, null));
        if (moneyGold > 0)
            actions.Add(new("Money", null, $"{moneyGold} gold", moneyGold * 10_000,
                "Mail", false, null));
        return actions;
    }

    public static void Validate(StarterPresetRequest request)
    {
        if (request.PlayerNames.Count is < 1 or > 10)
            throw new ArgumentException("Select between one and ten characters.");
        if (request.Preset is not ("new" or "level10" or "returning"))
            throw new ArgumentException("Unknown starter preset.");
        if (request.BagCount is < 0 or > 4)
            throw new ArgumentException("Bag count must be between zero and four.");
        if (request.MoneyGold is < 0 or > 1000)
            throw new ArgumentException("Money must be between zero and 1,000 gold.");
    }

    private static StarterPresetAction ItemAction(
        string kind, StarterItem item, int quantity, string delivery, bool skipped, string? reason) =>
        new(kind, item.Id, item.Name, quantity, delivery, skipped || quantity == 0,
            skipped || quantity == 0 ? reason : null);

    private sealed record StarterItem(uint Id, string Name);
}
