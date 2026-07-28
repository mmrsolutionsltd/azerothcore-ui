namespace AzerothCore_UI.Web.Models;

public sealed record ManagedProcessStatus(string Name, bool IsRunning, int? ProcessId,
    DateTime? StartedAt, long? WorkingSetBytes);
public sealed record ServerStatus(ManagedProcessStatus WorldServer, ManagedProcessStatus AuthServer,
    bool SoapConfigured, bool SoapReachable, string? WorldStatus, IReadOnlyList<ServerLogEntry> RecentLogs,
    ServerPopulation Population, int PlayerLimit);
public sealed record ToolAvailability(
    bool WorldServerRunning, bool SoapConfigured, bool SoapReachable);
public sealed record ServerPopulation(int HumanPlayers, int PlayerBots, int Total);
public sealed class PlayerBotSettings
{
    public string Version { get; set; } = "";
    public bool Enabled { get; set; }
    public bool RandomBotAutologin { get; set; }
    public int MinRandomBots { get; set; }
    public int MaxRandomBots { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public bool JoinLfg { get; set; }
    public bool JoinBattlegrounds { get; set; }
    public bool EnableTrading { get; set; }
}
public sealed class GameplayRateSettings
{
    public string Version { get; set; } = "";
    public decimal KillXp { get; set; }
    public decimal QuestXp { get; set; }
    public decimal ExplorationXp { get; set; }
    public decimal Reputation { get; set; }
    public decimal MoneyDrops { get; set; }
    public decimal QuestMoney { get; set; }
    public decimal Honor { get; set; }
    public decimal RepairCost { get; set; }
}
public sealed class AuctionHouseBotSettings
{
    public string Version { get; set; } = ""; public bool EnableSeller { get; set; } public bool EnableBuyer { get; set; }
    public bool UseMarketPrice { get; set; } public int ItemsPerCycle { get; set; } public int DuplicatesCount { get; set; }
    public bool DivisibleStacks { get; set; } public bool IncludeVendorItems { get; set; }
    public bool IncludeLootItems { get; set; } public bool IncludeProfessionItems { get; set; }
}
public sealed class AutoBalanceSettings
{
    public string Version { get; set; } = ""; public bool Enabled { get; set; } public int MinimumPlayers { get; set; }
    public int MinimumHeroicPlayers { get; set; } public int MinimumRaidPlayers { get; set; }
    public decimal HealthMultiplier { get; set; } public decimal DamageMultiplier { get; set; }
    public bool LevelScaling { get; set; } public bool ScaleXp { get; set; } public bool ScaleMoney { get; set; }
    public bool Announce { get; set; }
}
public sealed class TransmogSettings
{
    public string Version { get; set; } = ""; public bool Enabled { get; set; } public bool CollectionSystem { get; set; }
    public bool Portable { get; set; } public decimal CostMultiplier { get; set; } public int CopperCost { get; set; }
    public bool AllowPoor { get; set; } public bool AllowCommon { get; set; } public bool AllowLegendary { get; set; }
    public bool AllowHeirloom { get; set; } public bool MixedArmorTypes { get; set; } public int MixedWeaponTypes { get; set; }
    public bool IgnoreClass { get; set; } public bool IgnoreLevel { get; set; } public bool EnableSets { get; set; }
    public int MaximumSets { get; set; }
}
public sealed class AoeLootSettings
{
    public string Version { get; set; } = ""; public bool Enabled { get; set; } public bool ShowMessage { get; set; }
    public decimal Range { get; set; } public bool AllowInGroups { get; set; }
}
public sealed class AdministrationPlayer
{
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public bool Online { get; set; }
    public string Classification { get; set; } = "";
    public bool IsPlayerBot => Classification.Equals("PlayerBot", StringComparison.OrdinalIgnoreCase);
    public int PickerOrder => (Online ? 0 : 2) + (IsPlayerBot ? 1 : 0);
    public string PickerLabel => $"[{(Online ? "ONLINE" : "OFFLINE")}] [{(IsPlayerBot ? "BOT" : "PLAYER")}] Account: {Username}";
}
public sealed class AdministrationItem
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public byte ItemClass { get; set; }
    public byte ItemSubclass { get; set; }
    public byte Quality { get; set; }
    public ushort ItemLevel { get; set; }
    public byte RequiredLevel { get; set; }
}
public sealed record AdministrationItemSearchResult(
    IReadOnlyList<AdministrationItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed class AdministrationCreature
{
    public uint CreatureId { get; set; }
    public string Name { get; set; } = "";
    public byte MinimumLevel { get; set; }
    public byte MaximumLevel { get; set; }
    public byte CreatureType { get; set; }
    public uint Family { get; set; }
    public bool Tameable { get; set; }
    public bool Exotic { get; set; }
}
public sealed record AdministrationCreatureSearchResult(
    IReadOnlyList<AdministrationCreature> Creatures, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed class TeleportLocation
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public ushort MapId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
}
public sealed record TeleportLocationSearchResult(
    IReadOnlyList<TeleportLocation> Locations, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed class NpcTeleportSpawn
{
    public uint SpawnId { get; init; }
    public uint CreatureId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subname { get; init; } = string.Empty;
    public ushort MapId { get; init; }
    public ushort ZoneId { get; init; }
    public ushort AreaId { get; init; }
    public bool SameMap { get; init; }
    public double? Distance { get; init; }
    public bool PotentiallyHostile { get; init; }
}
public sealed record NpcTeleportSearchResult(
    IReadOnlyList<NpcTeleportSpawn> Npcs, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed class TrainerSpawn
{
    public uint SpawnId { get; set; }
    public uint CreatureId { get; set; }
    public string Name { get; set; } = "";
    public string Subname { get; set; } = "";
    public string Category { get; set; } = "";
    public ushort MapId { get; set; }
    public ushort ZoneId { get; set; }
    public ushort AreaId { get; set; }
    public bool SameMap { get; set; }
    public double? Distance { get; set; }
}
public sealed record TrainerSearchResult(
    IReadOnlyList<TrainerSpawn> Trainers, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record ServerLogEntry(string Source, string Message);
public sealed record GiveItemRequest(string PlayerName, uint ItemId, int Quantity);
public sealed record MailItemRequest(string PlayerName, uint ItemId, int Quantity, string Subject, string Message);
public sealed record GiveMoneyRequest(string PlayerName, int Gold, int Silver, int Copper);
public sealed record TeleportPlayerRequest(string PlayerName, string Location);
public sealed record TeleportPlayerToNpcRequest(
    string PlayerName, uint SpawnId, bool Confirmed);
public sealed record PlayerRelativeTeleportRequest(string PlayerName, string AnchorPlayerName);
public sealed record PartyBotRequest(string LeaderName, string BotName);
public sealed record PartyLeaderRequest(string LeaderName);
public sealed record PartyMember(string Name, int Level, string Role, bool IsPlayerBot);
public sealed record PartyBotCandidate(string Name, int Level, string Role, int CharacterClass);
public sealed record PartySnapshot(string LeaderName, int MemberCount,
    IReadOnlyList<PartyMember> Members, IReadOnlyList<PartyBotCandidate> Candidates);
public sealed class QuestingCompanionCandidate
{
    public string Name { get; init; } = "";
    public string Username { get; init; } = "";
    public int Level { get; init; }
    public int CharacterClass { get; init; }
    public int Race { get; init; }
    public bool Online { get; init; }
    public bool SameFaction { get; init; }
    public bool SameAccount { get; init; }
}
public sealed record ActiveQuestingCompanion(
    string Name, int Level, int CharacterClass, bool InLeaderParty);
public sealed record QuestingCompanionStatus(
    string LeaderName, IReadOnlyList<ActiveQuestingCompanion> ActiveCompanions,
    IReadOnlyList<QuestingCompanionCandidate> Candidates);
public sealed record QuestingCompanionRequest(string LeaderName, string CompanionName);
public sealed record DungeonDestination(uint DungeonId, string Name, int MinimumLevel,
    int MaximumLevel, uint MapId, string Difficulty);
public sealed record DungeonLockout(string PlayerName, uint MapId, int Difficulty, DateTime ResetAtUtc);
public sealed record DungeonQuestGiver(
    uint SpawnId, uint CreatureId, string Name, ushort MapId, ushort ZoneId);
public sealed record DungeonQuestPlayerStatus(
    string PlayerName, string Status, string Detail, bool CanTeleport);
public sealed record DungeonQuestPrerequisite(
    uint QuestId, string Title, DungeonQuestGiver? QuestGiver);
public sealed record DungeonQuest(uint QuestId, string Title, int MinimumLevel,
    IReadOnlyList<string> InProgressBy, IReadOnlyList<string> CompletedBy,
    DungeonQuestGiver? QuestGiver, IReadOnlyList<DungeonQuestPlayerStatus> PlayerStatuses,
    DungeonQuestPrerequisite? Prerequisite);
public sealed record DungeonReadiness(bool HasTank, bool HasHealer, int DamageCount, bool PartyFull,
    bool LevelsSuitable, IReadOnlyList<DungeonLockout> Lockouts, IReadOnlyList<DungeonQuest> RelevantQuests);
public sealed record DungeonGuide(
    uint DungeonId, string Name, string Overview, string Route,
    IReadOnlyList<string> ImportantNotes, IReadOnlyList<DungeonBossGuide> Bosses);
public sealed record DungeonBossGuide(
    int Order, uint CreatureId, string Name, string Tactics,
    IReadOnlyList<DungeonLootItem> Loot);
public sealed class DungeonLootItem
{
    public uint ItemId { get; init; }
    public string Name { get; init; } = "";
    public int Quality { get; init; }
    public int ItemLevel { get; init; }
    public int RequiredLevel { get; init; }
    public int ItemClass { get; init; }
    public int ItemSubclass { get; init; }
    public int InventoryType { get; init; }
    public long AllowableClass { get; init; }
    public double DropChance { get; init; }
    public bool QuestRequired { get; init; }
    public bool SuggestedForParty { get; init; }
    public int Armor { get; init; }
    public double MinimumDamage { get; init; }
    public double MaximumDamage { get; init; }
    public int DelayMilliseconds { get; init; }
    public IReadOnlyList<DungeonItemStat> Stats { get; init; } = [];
}
public sealed record DungeonItemStat(string Name, int Value, bool Rating = false);
public sealed record DungeonLibraryCharacter(
    uint Guid, string Name, string Username, int Level, int CharacterClass,
    bool Online);
public sealed record DungeonLibraryGuideRequest(
    uint DungeonId, IReadOnlyList<uint> CharacterGuids);
public sealed record LaunchDungeonRequest(string LeaderName, uint DungeonId, bool Confirmed);
public sealed record TeleportToDungeonQuestGiverRequest(
    uint QuestId, uint SpawnId, IReadOnlyList<string> PlayerNames, bool Confirmed);
public sealed record ReturnDungeonQuestPlayersRequest(
    IReadOnlyList<string> PlayerNames, bool Confirmed);
public sealed record SpawnCreatureRequest(string AnchorPlayerName, uint CreatureId, int Level, int DespawnMinutes, bool Confirmed);
public sealed record UtilityNpc(uint CreatureId, string Name, string Service, string Description, int Level);
public sealed record SummonUtilityNpcRequest(
    string PlayerName, uint CreatureId, int DespawnMinutes, bool Confirmed);
public sealed record SetAccountGmRequest(string Username, bool Enabled, bool Confirmed);
public sealed record SetPlayerSpeedRequest(string PlayerName, decimal Speed);
public sealed record CharacterServiceRequest(string PlayerName, string Service, int? Level, bool Confirmed);
public sealed record TeleportToTrainerRequest(string PlayerName, uint SpawnId, bool Confirmed);
public sealed record CollectibleItem(uint ItemId, string Name, string Type, int LearnSpellId, byte RequiredLevel, byte Quality);
public sealed record CollectibleSearchResult(IReadOnlyList<CollectibleItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record CharacterCollectibleItem(uint ItemId, string Name, string Type, int LearnSpellId,
    byte RequiredLevel, byte Quality, bool Known, bool MeetsLevelRequirement);
public sealed record CharacterCollectibleSearchResult(IReadOnlyList<CharacterCollectibleItem> Items, int Page,
    int PageSize, int TotalCount, int TotalPages, int KnownCount, int MissingCount);
public sealed record WeaponTrainingStatus(string Key, string Name, bool Learned, int CurrentSkill, int MaximumSkill);
public sealed record GrantWeaponTrainingRequest(string PlayerName, string WeaponKey, bool Confirmed);
public sealed record GuildBankStatus(
    uint GuildId, string GuildName, string PlayerName, bool IsGuildMaster,
    int PurchasedTabs, int MaximumTabs, uint NextTabCostCopper);
public sealed record UnlockGuildBankTabRequest(string PlayerName, bool Confirmed);
public sealed record AdministrationResult(bool Success, string Message, string? Output = null);
