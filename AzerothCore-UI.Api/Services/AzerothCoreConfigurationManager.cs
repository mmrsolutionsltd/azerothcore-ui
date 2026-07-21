using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public sealed partial class AzerothCoreConfigurationManager(IConfiguration configuration)
{
    private const int MaximumRandomBotCount = 5000;
    private readonly string rootPath = Path.GetFullPath(configuration["AzerothCore:Server:RootPath"]
        ?? @"C:\AzerothServer-PlayerBots");
    private readonly SemaphoreSlim writeLock = new(1, 1);

    private string PlayerBotsPath => SafePath("configs", "modules", "playerbots.conf");
    private string WorldServerPath => SafePath("configs", "worldserver.conf");
    private string AuctionHouseBotPath => SafePath("configs", "modules", "mod_ahbot.conf");
    private string AutoBalancePath => SafePath("configs", "modules", "AutoBalance.conf");
    private string TransmogPath => SafePath("configs", "modules", "transmog.conf");
    private string AoeLootPath => SafePath("configs", "modules", "mod_aoe_loot.conf");

    public int GetPlayerLimit() => ReadIntFile(WorldServerPath, "PlayerLimit");

    public GameplayRateSettings GetGameplayRateSettings()
    {
        var text = File.ReadAllText(WorldServerPath);
        return new GameplayRateSettings(
            Hash(text),
            ReadDecimal(text, "Rate.XP.Kill"),
            ReadDecimal(text, "Rate.XP.Quest"),
            ReadDecimal(text, "Rate.XP.Explore"),
            ReadDecimal(text, "Rate.Reputation.Gain"),
            ReadDecimal(text, "Rate.Drop.Money"),
            ReadDecimal(text, "Rate.RewardQuestMoney"),
            ReadDecimal(text, "Rate.Honor"),
            ReadDecimal(text, "Rate.RepairCost"));
    }

    public async Task<GameplayRateSettings> UpdateGameplayRateSettingsAsync(
        UpdateGameplayRateSettingsRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var values = new Dictionary<string, string>
        {
            ["Rate.XP.Kill"] = Format(request.KillXp),
            ["Rate.XP.Quest"] = Format(request.QuestXp),
            ["Rate.XP.Explore"] = Format(request.ExplorationXp),
            ["Rate.Reputation.Gain"] = Format(request.Reputation),
            ["Rate.Drop.Money"] = Format(request.MoneyDrops),
            ["Rate.RewardQuestMoney"] = Format(request.QuestMoney),
            ["Rate.Honor"] = Format(request.Honor),
            ["Rate.RepairCost"] = Format(request.RepairCost)
        };
        await UpdateFileAsync(WorldServerPath, request.Version, values, cancellationToken);
        return GetGameplayRateSettings();
    }

    public PlayerBotSettings GetPlayerBotSettings()
    {
        var text = File.ReadAllText(PlayerBotsPath);
        return new PlayerBotSettings(
            Hash(text),
            ReadBool(text, "AiPlayerbot.Enabled"),
            ReadBool(text, "AiPlayerbot.RandomBotAutologin"),
            ReadInt(text, "AiPlayerbot.MinRandomBots"),
            ReadInt(text, "AiPlayerbot.MaxRandomBots"),
            ReadInt(text, "AiPlayerbot.RandomBotMinLevel"),
            ReadInt(text, "AiPlayerbot.RandomBotMaxLevel"),
            ReadBool(text, "AiPlayerbot.RandomBotJoinLfg"),
            ReadBool(text, "AiPlayerbot.RandomBotJoinBG"),
            ReadBool(text, "AiPlayerbot.EnableRandomBotTrading"));
    }

    public AuctionHouseBotSettings GetAuctionHouseBotSettings() => ReadModule<AuctionHouseBotSettings>(AuctionHouseBotPath, text => new(
        Hash(text), ReadBool(text, "AuctionHouseBot.EnableSeller"), ReadBool(text, "AuctionHouseBot.EnableBuyer"),
        ReadBool(text, "AuctionHouseBot.UseMarketPriceForSeller"), ReadInt(text, "AuctionHouseBot.ItemsPerCycle"),
        ReadInt(text, "AuctionHouseBot.DuplicatesCount"), ReadBool(text, "AuctionHouseBot.DivisibleStacks"),
        ReadBool(text, "AuctionHouseBot.VendorItems"), ReadBool(text, "AuctionHouseBot.LootItems"),
        ReadBool(text, "AuctionHouseBot.ProfessionItems")));

    public AutoBalanceSettings GetAutoBalanceSettings() => ReadModule<AutoBalanceSettings>(AutoBalancePath, text => new(
        Hash(text), ReadBool(text, "AutoBalance.Enable.Global"), ReadInt(text, "AutoBalance.MinPlayers"),
        ReadInt(text, "AutoBalance.MinPlayers.Heroic"), ReadInt(text, "AutoBalance.MinPlayers.Raid"),
        ReadDecimal(text, "AutoBalance.StatModifier.Health"), ReadDecimal(text, "AutoBalance.StatModifier.Damage"),
        ReadBool(text, "AutoBalance.LevelScaling"), ReadBool(text, "AutoBalance.RewardScaling.XP"),
        ReadBool(text, "AutoBalance.RewardScaling.Money"), ReadBool(text, "AutoBalanceAnnounce.enable")));

    public TransmogSettings GetTransmogSettings() => ReadModule<TransmogSettings>(TransmogPath, text => new(
        Hash(text), ReadBool(text, "Transmogrification.Enable"), ReadBool(text, "Transmogrification.UseCollectionSystem"),
        ReadBool(text, "Transmogrification.EnablePortable"), ReadDecimal(text, "Transmogrification.ScaledCostModifier"),
        ReadInt(text, "Transmogrification.CopperCost"), ReadBool(text, "Transmogrification.AllowPoor"),
        ReadBool(text, "Transmogrification.AllowCommon"), ReadBool(text, "Transmogrification.AllowLegendary"),
        ReadBool(text, "Transmogrification.AllowHeirloom"), ReadBool(text, "Transmogrification.AllowMixedArmorTypes"),
        ReadInt(text, "Transmogrification.AllowMixedWeaponTypes"), ReadBool(text, "Transmogrification.IgnoreReqClass"),
        ReadBool(text, "Transmogrification.IgnoreReqLevel"), ReadBool(text, "Transmogrification.EnableSets"),
        ReadInt(text, "Transmogrification.MaxSets")));

    public AoeLootSettings GetAoeLootSettings() => ReadModule<AoeLootSettings>(AoeLootPath, text => new(
        Hash(text), ReadBool(text, "AOELoot.Enable"), ReadBool(text, "AOELoot.Message"),
        ReadDecimal(text, "AOELoot.Range"), ReadBool(text, "AOELoot.Group")));

    public async Task<AuctionHouseBotSettings> UpdateAuctionHouseBotSettingsAsync(AuctionHouseBotSettings value, CancellationToken token)
    {
        if (value.ItemsPerCycle is < 1 or > 10000 || value.DuplicatesCount is < 0 or > 1000) throw new ArgumentException("Auction quantities are outside the supported range.");
        await UpdateModuleAsync(AuctionHouseBotPath, value.Version, new Dictionary<string, string>
        {
            ["AuctionHouseBot.EnableSeller"] = Bit(value.EnableSeller), ["AuctionHouseBot.EnableBuyer"] = Bit(value.EnableBuyer),
            ["AuctionHouseBot.UseMarketPriceForSeller"] = Bit(value.UseMarketPrice), ["AuctionHouseBot.ItemsPerCycle"] = value.ItemsPerCycle.ToString(),
            ["AuctionHouseBot.DuplicatesCount"] = value.DuplicatesCount.ToString(), ["AuctionHouseBot.DivisibleStacks"] = Bit(value.DivisibleStacks),
            ["AuctionHouseBot.VendorItems"] = Bit(value.IncludeVendorItems), ["AuctionHouseBot.LootItems"] = Bit(value.IncludeLootItems),
            ["AuctionHouseBot.ProfessionItems"] = Bit(value.IncludeProfessionItems)
        }, token); return GetAuctionHouseBotSettings();
    }

    public async Task<AutoBalanceSettings> UpdateAutoBalanceSettingsAsync(AutoBalanceSettings value, CancellationToken token)
    {
        if (value.MinimumPlayers is < 1 or > 40 || value.MinimumHeroicPlayers is < 1 or > 40 || value.MinimumRaidPlayers is < 1 or > 40) throw new ArgumentException("Minimum players must be between 1 and 40.");
        if (value.HealthMultiplier is < 0.01m or > 10 || value.DamageMultiplier is < 0.01m or > 10) throw new ArgumentException("AutoBalance multipliers must be between 0.01 and 10.");
        await UpdateModuleAsync(AutoBalancePath, value.Version, new Dictionary<string, string>
        {
            ["AutoBalance.Enable.Global"] = Bit(value.Enabled), ["AutoBalance.MinPlayers"] = value.MinimumPlayers.ToString(),
            ["AutoBalance.MinPlayers.Heroic"] = value.MinimumHeroicPlayers.ToString(), ["AutoBalance.MinPlayers.Raid"] = value.MinimumRaidPlayers.ToString(),
            ["AutoBalance.StatModifier.Health"] = Format(value.HealthMultiplier), ["AutoBalance.StatModifier.Damage"] = Format(value.DamageMultiplier),
            ["AutoBalance.LevelScaling"] = Bit(value.LevelScaling), ["AutoBalance.RewardScaling.XP"] = Bit(value.ScaleXp),
            ["AutoBalance.RewardScaling.Money"] = Bit(value.ScaleMoney), ["AutoBalanceAnnounce.enable"] = Bit(value.Announce)
        }, token); return GetAutoBalanceSettings();
    }

    public async Task<TransmogSettings> UpdateTransmogSettingsAsync(TransmogSettings value, CancellationToken token)
    {
        if (value.CostMultiplier is < 0 or > 100 || value.CopperCost is < 0 or > 100000000 || value.MixedWeaponTypes is < 0 or > 2 || value.MaximumSets is < 0 or > 25) throw new ArgumentException("One or more transmog settings are outside the supported range.");
        await UpdateModuleAsync(TransmogPath, value.Version, new Dictionary<string, string>
        {
            ["Transmogrification.Enable"] = Bit(value.Enabled), ["Transmogrification.UseCollectionSystem"] = Bit(value.CollectionSystem),
            ["Transmogrification.EnablePortable"] = Bit(value.Portable), ["Transmogrification.ScaledCostModifier"] = Format(value.CostMultiplier),
            ["Transmogrification.CopperCost"] = value.CopperCost.ToString(), ["Transmogrification.AllowPoor"] = Bit(value.AllowPoor),
            ["Transmogrification.AllowCommon"] = Bit(value.AllowCommon), ["Transmogrification.AllowLegendary"] = Bit(value.AllowLegendary),
            ["Transmogrification.AllowHeirloom"] = Bit(value.AllowHeirloom), ["Transmogrification.AllowMixedArmorTypes"] = Bit(value.MixedArmorTypes),
            ["Transmogrification.AllowMixedWeaponTypes"] = value.MixedWeaponTypes.ToString(), ["Transmogrification.IgnoreReqClass"] = Bit(value.IgnoreClass),
            ["Transmogrification.IgnoreReqLevel"] = Bit(value.IgnoreLevel), ["Transmogrification.EnableSets"] = Bit(value.EnableSets),
            ["Transmogrification.MaxSets"] = value.MaximumSets.ToString()
        }, token); return GetTransmogSettings();
    }

    public async Task<AoeLootSettings> UpdateAoeLootSettingsAsync(AoeLootSettings value, CancellationToken token)
    {
        if (value.Range is < 5 or > 100) throw new ArgumentException("AoE loot range must be between 5 and 100 yards.");
        await UpdateModuleAsync(AoeLootPath, value.Version, new Dictionary<string, string>
        {
            ["AOELoot.Enable"] = Bit(value.Enabled), ["AOELoot.Message"] = Bit(value.ShowMessage),
            ["AOELoot.Range"] = Format(value.Range), ["AOELoot.Group"] = Bit(value.AllowInGroups)
        }, token); return GetAoeLootSettings();
    }

    private static T ReadModule<T>(string path, Func<string, T> read) => read(File.ReadAllText(path));
    private Task UpdateModuleAsync(string path, string version, IReadOnlyDictionary<string, string> values, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Configuration version is required.");
        return UpdateFileAsync(path, version, values, token);
    }

    public async Task<PlayerBotSettings> UpdatePlayerBotSettingsAsync(
        UpdatePlayerBotSettingsRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var path = PlayerBotsPath;
            var original = await File.ReadAllTextAsync(path, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(original)), Encoding.UTF8.GetBytes(request.Version)))
                throw new InvalidOperationException("The PlayerBots configuration changed after it was loaded. Refresh and try again.");

            var values = new Dictionary<string, string>
            {
                ["AiPlayerbot.Enabled"] = Bit(request.Enabled),
                ["AiPlayerbot.RandomBotAutologin"] = Bit(request.RandomBotAutologin),
                ["AiPlayerbot.MinRandomBots"] = request.MinRandomBots.ToString(),
                ["AiPlayerbot.MaxRandomBots"] = request.MaxRandomBots.ToString(),
                ["AiPlayerbot.RandomBotMinLevel"] = request.MinLevel.ToString(),
                ["AiPlayerbot.RandomBotMaxLevel"] = request.MaxLevel.ToString(),
                ["AiPlayerbot.RandomBotJoinLfg"] = Bit(request.JoinLfg),
                ["AiPlayerbot.RandomBotJoinBG"] = Bit(request.JoinBattlegrounds),
                ["AiPlayerbot.EnableRandomBotTrading"] = Bit(request.EnableTrading)
            };
            var updated = values.Aggregate(original, (text, item) => Replace(text, item.Key, item.Value));
            if (updated == original) return GetPlayerBotSettings();

            var backup = $"{path}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, false);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, updated, new UTF8Encoding(false), cancellationToken);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return GetPlayerBotSettings();
        }
        finally { writeLock.Release(); }
    }

    private string SafePath(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine([rootPath, .. parts]));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid configuration path.");
        return path;
    }

    private static void Validate(UpdatePlayerBotSettingsRequest value)
    {
        if (value.MinRandomBots is < 0 or > MaximumRandomBotCount || value.MaxRandomBots is < 0 or > MaximumRandomBotCount || value.MinRandomBots > value.MaxRandomBots)
            throw new ArgumentException($"Bot counts must be between 0 and {MaximumRandomBotCount:N0}, with minimum no greater than maximum.");
        if (value.MinLevel is < 1 or > 80 || value.MaxLevel is < 1 or > 80 || value.MinLevel > value.MaxLevel)
            throw new ArgumentException("Bot levels must be between 1 and 80, with minimum no greater than maximum.");
        if (string.IsNullOrWhiteSpace(value.Version)) throw new ArgumentException("Configuration version is required.");
    }

    private static void Validate(UpdateGameplayRateSettingsRequest value)
    {
        if (string.IsNullOrWhiteSpace(value.Version)) throw new ArgumentException("Configuration version is required.");
        var rates = new[] { value.KillXp, value.QuestXp, value.ExplorationXp, value.Reputation,
            value.MoneyDrops, value.QuestMoney, value.Honor, value.RepairCost };
        if (rates.Any(rate => rate is < 0 or > 100))
            throw new ArgumentException("Gameplay rates must be between 0 and 100.");
    }

    private async Task UpdateFileAsync(string path, string version, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var original = await File.ReadAllTextAsync(path, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(original)), Encoding.UTF8.GetBytes(version)))
                throw new InvalidOperationException("The configuration changed after it was loaded. Refresh and try again.");
            var updated = values.Aggregate(original, (text, item) => Replace(text, item.Key, item.Value));
            if (updated == original) return;
            var backup = $"{path}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, false);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, updated, new UTF8Encoding(false), cancellationToken);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { writeLock.Release(); }
    }

    private static string Replace(string text, string key, string value)
    {
        var regex = new Regex($@"(?m)^(\s*{Regex.Escape(key)}\s*=\s*)[^\r\n]*(\r?$)");
        if (!regex.IsMatch(text)) throw new InvalidOperationException($"Required setting '{key}' was not found.");
        return regex.Replace(text, $"${{1}}{value}${{2}}", 1);
    }

    private static bool ReadBool(string text, string key) => ReadInt(text, key) != 0;
    private static int ReadIntFile(string path, string key) => ReadInt(File.ReadAllText(path), key);
    private static int ReadInt(string text, string key)
    {
        var pairs = SettingRegex().Matches(text).Cast<Match>();
        var value = pairs.FirstOrDefault(x => x.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
        return int.TryParse(value, out var result) ? result : throw new InvalidOperationException($"Setting '{key}' is missing or invalid.");
    }
    private static decimal ReadDecimal(string text, string key)
    {
        var value = SettingRegex().Matches(text).Cast<Match>()
            .FirstOrDefault(x => x.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result : throw new InvalidOperationException($"Setting '{key}' is missing or invalid.");
    }
    private static string Bit(bool value) => value ? "1" : "0";
    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(@"(?m)^\s*([A-Za-z][A-Za-z0-9_.-]*)\s*=\s*([^#\s\r\n]+)")]
    private static partial Regex SettingRegex();
}
