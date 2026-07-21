using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class AzerothCoreConfigurationManagerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"azeroth-ui-tests-{Guid.NewGuid():N}");
    private readonly AzerothCoreConfigurationManager manager;

    public AzerothCoreConfigurationManagerTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "configs", "modules"));
        Write("mod_ahbot.conf", """
            AuctionHouseBot.EnableSeller = 1
            AuctionHouseBot.EnableBuyer = 0
            AuctionHouseBot.UseMarketPriceForSeller = 1
            AuctionHouseBot.ItemsPerCycle = 200
            AuctionHouseBot.DuplicatesCount = 3
            AuctionHouseBot.DivisibleStacks = 1
            AuctionHouseBot.VendorItems = 0
            AuctionHouseBot.LootItems = 1
            AuctionHouseBot.ProfessionItems = 1
            Unrelated.Setting = 42
            """);
        Write("AutoBalance.conf", """
            AutoBalance.Enable.Global=1
            AutoBalance.MinPlayers=1
            AutoBalance.MinPlayers.Heroic=2
            AutoBalance.MinPlayers.Raid=3
            AutoBalance.StatModifier.Health=0.75
            AutoBalance.StatModifier.Damage=0.5
            AutoBalance.LevelScaling=1
            AutoBalance.RewardScaling.XP=1
            AutoBalance.RewardScaling.Money=0
            AutoBalanceAnnounce.enable=1
            """);
        Write("transmog.conf", """
            Transmogrification.Enable=1
            Transmogrification.UseCollectionSystem=1
            Transmogrification.EnablePortable=1
            Transmogrification.ScaledCostModifier=1.5
            Transmogrification.CopperCost=100
            Transmogrification.AllowPoor=0
            Transmogrification.AllowCommon=1
            Transmogrification.AllowLegendary=0
            Transmogrification.AllowHeirloom=1
            Transmogrification.AllowMixedArmorTypes=0
            Transmogrification.AllowMixedWeaponTypes=1
            Transmogrification.IgnoreReqClass=0
            Transmogrification.IgnoreReqLevel=0
            Transmogrification.EnableSets=1
            Transmogrification.MaxSets=10
            """);
        Write("mod_aoe_loot.conf", """
            AOELoot.Enable = 1
            AOELoot.Message = 0
            AOELoot.Range = 55.0
            AOELoot.Group = 1
            """);
        File.WriteAllText(Path.Combine(root, "configs", "worldserver.conf"), "PlayerLimit = 10");
        Write("playerbots.conf", """
            AiPlayerbot.Enabled = 1
            AiPlayerbot.RandomBotAutologin = 1
            AiPlayerbot.MinRandomBots = 100
            AiPlayerbot.MaxRandomBots = 250
            AiPlayerbot.RandomBotMinLevel = 1
            AiPlayerbot.RandomBotMaxLevel = 80
            AiPlayerbot.RandomBotJoinLfg = 1
            AiPlayerbot.RandomBotJoinBG = 1
            AiPlayerbot.EnableRandomBotTrading = 0
            """);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AzerothCore:Server:RootPath"] = root }).Build();
        manager = new(configuration);
    }

    [Fact]
    public void ReadsInstalledModuleSettings()
    {
        var auction = manager.GetAuctionHouseBotSettings();
        var balance = manager.GetAutoBalanceSettings();
        var transmog = manager.GetTransmogSettings();
        var loot = manager.GetAoeLootSettings();

        Assert.True(auction.EnableSeller); Assert.False(auction.EnableBuyer); Assert.Equal(200, auction.ItemsPerCycle);
        Assert.True(balance.Enabled); Assert.Equal(0.75m, balance.HealthMultiplier); Assert.Equal(3, balance.MinimumRaidPlayers);
        Assert.True(transmog.CollectionSystem); Assert.Equal(1.5m, transmog.CostMultiplier); Assert.Equal(10, transmog.MaximumSets);
        Assert.True(loot.Enabled); Assert.False(loot.ShowMessage); Assert.Equal(55m, loot.Range);
    }

    [Fact]
    public async Task UpdateAuctionHouseBot_PreservesUnmanagedSettings()
    {
        var current = manager.GetAuctionHouseBotSettings();
        var updated = await manager.UpdateAuctionHouseBotSettingsAsync(
            current with { EnableBuyer = true, ItemsPerCycle = 350 }, CancellationToken.None);

        var text = File.ReadAllText(ModulePath("mod_ahbot.conf"));
        Assert.True(updated.EnableBuyer); Assert.Equal(350, updated.ItemsPerCycle);
        Assert.Contains("Unrelated.Setting = 42", text);
        Assert.Contains("AuctionHouseBot.EnableBuyer = 1", text);
    }

    [Fact]
    public async Task UpdateAoeLoot_RejectsUnsafeRange()
    {
        var current = manager.GetAoeLootSettings();
        await Assert.ThrowsAsync<ArgumentException>(() => manager.UpdateAoeLootSettingsAsync(
            current with { Range = 101 }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateModule_RejectsStaleVersion()
    {
        var current = manager.GetTransmogSettings();
        File.AppendAllText(ModulePath("transmog.conf"), Environment.NewLine + "External.Change=1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateTransmogSettingsAsync(
            current with { Portable = false }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdatePlayerBotSettings_AcceptsCountsAbovePreviousLimit()
    {
        var current = manager.GetPlayerBotSettings();
        var request = new UpdatePlayerBotSettingsRequest(current.Version, current.Enabled, current.RandomBotAutologin,
            1500, 2000, current.MinLevel, current.MaxLevel, current.JoinLfg, current.JoinBattlegrounds, current.EnableTrading);

        var updated = await manager.UpdatePlayerBotSettingsAsync(request, CancellationToken.None);

        var text = File.ReadAllText(ModulePath("playerbots.conf"));
        Assert.Equal(1500, updated.MinRandomBots);
        Assert.Equal(2000, updated.MaxRandomBots);
        Assert.Contains("AiPlayerbot.MinRandomBots = 1500", text);
        Assert.Contains("AiPlayerbot.MaxRandomBots = 2000", text);
    }

    [Fact]
    public async Task UpdatePlayerBotSettings_RejectsCountsAboveSafetyLimit()
    {
        var current = manager.GetPlayerBotSettings();
        var request = new UpdatePlayerBotSettingsRequest(current.Version, current.Enabled, current.RandomBotAutologin,
            5001, 5001, current.MinLevel, current.MaxLevel, current.JoinLfg, current.JoinBattlegrounds, current.EnableTrading);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.UpdatePlayerBotSettingsAsync(request, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private string ModulePath(string name) => Path.Combine(root, "configs", "modules", name);
    private void Write(string name, string contents) => File.WriteAllText(ModulePath(name), contents);
}
