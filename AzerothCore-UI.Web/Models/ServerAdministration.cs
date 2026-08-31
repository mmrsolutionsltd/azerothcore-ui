namespace AzerothCore_UI.Web.Models;

public sealed record ManagedProcessStatus(string Name, bool IsRunning, int? ProcessId,
    DateTime? StartedAt, long? WorkingSetBytes);
public sealed record LiveCharacterStatus(
    string Name, bool Alive, uint Health, uint MaxHealth,
    ushort MapId, ushort ZoneId, ushort AreaId, float X, float Y, float Z);
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
    public int HerbAbundancePercent { get; set; } = 100;
    public int MiningAbundancePercent { get; set; } = 100;
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
    public int SuitableTargetCount { get; set; }
    public int TargetCount { get; set; }
    public int LikelyUpgradeTargetCount { get; set; }
    public IReadOnlyList<string> SuitableTargetNames { get; set; } = [];
    public IReadOnlyList<string> IncompatibleTargetNames { get; set; } = [];
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
    public uint AccountId { get; init; }
    public int Level { get; init; }
    public int CharacterClass { get; init; }
    public int Race { get; init; }
    public bool Online { get; init; }
    public bool SameFaction { get; init; }
    public bool SameAccount { get; init; }
    public bool SameGuild { get; set; }
    public bool AccountsLinked { get; set; }
    public bool ControlAllowed => SameAccount || SameGuild || AccountsLinked;
    public string ControlReason => SameAccount ? "Same game account"
        : SameGuild ? "Same guild"
        : AccountsLinked ? "Trusted accounts"
        : "Accounts are not linked and the characters do not share a guild";
}
public sealed record QuestingCompanionObjective(
    string Kind, uint Entry, string Name, int Current, int Required);
public sealed record QuestingCompanionQuest(
    uint QuestId, string Title, bool Complete,
    IReadOnlyList<QuestingCompanionObjective> Objectives);
public sealed record QuestingCompanionItem(
    string Location, int Bag, int Slot, uint ItemId, int Count,
    int Quality, int ItemLevel, int Durability, int MaximumDurability,
    bool Protected, string Name)
{
    public ulong ItemGuid { get; init; }
    public int RequiredLevel { get; init; }
    public bool Tradeable { get; init; }
    public bool TemporaryBopTradeable { get; init; }
    public string TradeRestriction { get; init; } = "";
}
public sealed record QuestingCompanionEquipmentChange(
    long ChangedAtUnix, string Description);
public sealed record QuestingCompanionBehavior(
    string Preset, string Role, string Movement, string CombatFocus,
    double FollowDistance, bool LootEnabled, bool GatherEnabled,
    bool AutoSellTrash, bool AutoRepair);
public sealed record QuestingCompanionDiagnostics(
    string Activity, string Target, string Destination, string Blocker,
    double DistanceFromLeader, bool Alive, bool LeaderAlive,
    bool InCombat, bool Moving, long LastSuccessAtUnix,
    string LastSuccess, long LastFailureAtUnix, string LastFailure)
{
    public static QuestingCompanionDiagnostics Unknown { get; } = new(
        "Unknown", "None", "Unknown", "Install bridge protocol 10.",
        -1, true, true, false, false, 0,
        "No successful action reported.", 0, "No failed action reported.");
}
public sealed record ActiveQuestingCompanion(
    string Name, int Level, int CharacterClass, bool InLeaderParty,
    bool LootEnabled, int FreeBagSlots, int TotalBagSlots,
    IReadOnlyList<QuestingCompanionQuest> Quests, string QuestObjectStatus,
    bool AutoSellTrash, bool AutoRepair,
    IReadOnlyList<QuestingCompanionItem> Equipment,
    IReadOnlyList<QuestingCompanionItem> Inventory,
    IReadOnlyList<QuestingCompanionEquipmentChange> RecentEquipmentChanges,
    IReadOnlyList<QuestingCompanionEquipmentChange> RecentInventoryChanges,
    QuestingCompanionBehavior Behavior,
    QuestingCompanionLogisticsStatus Logistics)
{
    public QuestingCompanionDiagnostics Diagnostics { get; init; } =
        QuestingCompanionDiagnostics.Unknown;
}
public sealed record QuestingCompanionStatus(
    string LeaderName, IReadOnlyList<ActiveQuestingCompanion> ActiveCompanions,
    IReadOnlyList<QuestingCompanionCandidate> Candidates,
    IReadOnlyList<QuestingCompanionQuest> LeaderQuests,
    int ProtocolVersion);
public sealed record CompanionPartySession(
    uint LeaderGuid, string LeaderName, uint LeaderAccountId,
    string LeaderUsername, bool LeaderOnline, ulong? StartedByUserId,
    string StartedByUsername, DateTime StartedAtUtc,
    DateTime LastLeaderOnlineAtUtc, int OfflineTimeoutMinutes,
    IReadOnlyList<RealmRosterCharacter> Companions);
public sealed record RealmRosterCharacter(
    uint Guid, string Name, string Username, int Level, int CharacterClass,
    int Race, bool Online, bool IsLeader, bool IsCompanion,
    uint CurrentHealth = 0, uint? MaximumHealth = null,
    bool IsPlayerBot = false);
public sealed record RealmRosterParty(
    string Key, uint? GroupGuid, string LeaderName, bool Live,
    bool RememberedCompanionParty, int OfflineTimeoutMinutes,
    DateTime? LastLeaderOnlineAtUtc,
    IReadOnlyList<RealmRosterCharacter> Members);
public sealed record RealmRosterSnapshot(
    DateTime GeneratedAtUtc, IReadOnlyList<RealmRosterParty> Parties,
    IReadOnlyList<RealmRosterCharacter> SoloPlayers,
    IReadOnlyList<CompanionPartySession> CompanionSessions,
    IReadOnlyList<RealmRosterCharacter>? AvailableHeroes = null);
public sealed record CompanionPartyTimeoutRequest(
    string LeaderName, int OfflineTimeoutMinutes);
public sealed record QuestingCompanionRequest(string LeaderName, string CompanionName);
public sealed record QuestingCompanionResetRequest(
    string LeaderName, string CompanionName);
public sealed record QuestingCompanionCommandRequest(
    string LeaderName, string CompanionName, string Command);
public sealed record QuestingCompanionTradeRequest(
    string LeaderName, string CompanionName, string RecipientName,
    int Bag, int Slot, ulong ItemGuid, uint ItemId, int Quantity);
public sealed record QuestingCompanionEquipmentProtectionRequest(
    string LeaderName, string CompanionName, int Slot, bool Protected);
public sealed record QuestingCompanionBehaviorRequest(
    string LeaderName, string CompanionName, string Preset, string Role,
    string Movement, string CombatFocus, double FollowDistance,
    bool LootEnabled, bool GatherEnabled, bool AutoSellTrash, bool AutoRepair);
public sealed record QuestingCompanionPresetRequest(
    string LeaderName, string CompanionName, string Preset);
public sealed record QuestingCompanionAccountLinkRequest(
    string LeaderName, string CompanionName, bool Linked, bool Confirmed);
public sealed record CompanionLogisticsSettings(
    int TriggerFreeSlots, int TargetFreeSlots, bool AutomaticEnabled);
public sealed record CompanionLogisticsRoute(
    string CategoryKey, string CategoryName, uint? RecipientGuid,
    string? RecipientName, int KeepQuantity, bool Enabled);
public sealed record CompanionLogisticsRecipient(
    uint CharacterGuid, string Name, string Username,
    IReadOnlyList<string> Professions);
public sealed record CompanionLogisticsCategory(
    string Key, string Name, string Description,
    int SuggestedKeepQuantity, IReadOnlyList<uint> SuggestedRecipientGuids);
public sealed record CompanionLogisticsConfiguration(
    string CompanionName, CompanionLogisticsSettings Settings,
    IReadOnlyList<CompanionLogisticsRoute> Routes,
    IReadOnlyList<CompanionLogisticsCategory> Categories,
    IReadOnlyList<CompanionLogisticsRecipient> Recipients);
public sealed record SaveCompanionLogisticsRequest(
    string LeaderName, string CompanionName,
    CompanionLogisticsSettings Settings,
    IReadOnlyList<CompanionLogisticsRoute> Routes);
public sealed record RunCompanionLogisticsRequest(
    string LeaderName, string CompanionName);
public sealed record CompanionLogisticsPreviewItem(
    uint ItemId, int Count, int Quality, int Bag, int Slot, string Name,
    string Action, string Destination, string Reason);
public sealed record CompanionLogisticsPreview(
    string CompanionName, int CurrentFreeSlots, int TotalBagSlots,
    int PotentialFreeSlots, int PostageCopper, bool MailboxNearby,
    bool VendorNearby, IReadOnlyList<CompanionLogisticsPreviewItem> Items);
public sealed record QuestingCompanionLogisticsStatus(
    int TriggerFreeSlots, int TargetFreeSlots, bool AutomaticEnabled,
    int RouteCount, string Status);
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
    public IReadOnlyList<DungeonLootRecommendation> Recommendations { get; init; } = [];
}
public sealed record DungeonItemStat(string Name, int Value, bool Rating = false);
public sealed record DungeonLootRecommendation(
    uint CharacterGuid, string CharacterName, bool Usable, bool LikelyUpgrade,
    int? EquippedItemLevel, string Reason);
public sealed record DungeonLibraryCharacter(
    uint Guid, string Name, string Username, int Level, int CharacterClass,
    bool Online);
public sealed record DungeonLibraryGuideRequest(
    uint DungeonId, IReadOnlyList<uint> CharacterGuids);
public sealed record DungeonWishlistPlanRequest(
    IReadOnlyList<uint> ItemIds, IReadOnlyList<uint> CharacterGuids);
public sealed record DungeonWishlistPlan(
    IReadOnlyList<DungeonWishlistPlanItem> Items,
    IReadOnlyList<DungeonWishlistRun> RecommendedRuns);
public sealed record DungeonWishlistPlanItem(
    uint ItemId, string Name, int Quality, int ItemLevel,
    IReadOnlyList<DungeonWishlistSource> Sources,
    IReadOnlyList<DungeonWishlistCharacter> Characters);
public sealed record DungeonWishlistSource(
    uint BossCreatureId, string BossName, uint MapId,
    double DropChance, double? EstimatedRuns);
public sealed record DungeonWishlistCharacter(
    uint CharacterGuid, string CharacterName, bool Usable, bool Owned, bool Equipped);
public sealed record DungeonWishlistRun(
    uint MapId, IReadOnlyList<string> DungeonNames,
    int WantedItemCount, IReadOnlyList<string> ItemNames);
public sealed record LaunchDungeonRequest(string LeaderName, uint DungeonId, bool Confirmed);
public sealed record TeleportToDungeonQuestGiverRequest(
    uint QuestId, uint SpawnId, IReadOnlyList<string> PlayerNames, bool Confirmed);
public sealed record ReturnDungeonQuestPlayersRequest(
    IReadOnlyList<string> PlayerNames, bool Confirmed);
public sealed record SpawnCreatureRequest(
    string AnchorPlayerName,
    uint CreatureId,
    int Level,
    int DespawnMinutes,
    int Count,
    int SquareSideLength,
    bool Confirmed);
public sealed record UtilityNpc(uint CreatureId, string Name, string Service, string Description, int Level);
public sealed record SummonUtilityNpcRequest(
    string PlayerName, uint CreatureId, int DespawnMinutes, bool Confirmed);
public sealed record CreateGameAccountRequest(string Username, string Password);
public sealed record SetAccountGmRequest(string Username, bool Enabled, bool Confirmed);
public sealed record SetPlayerSpeedRequest(string PlayerName, decimal Speed);
public sealed record CharacterServiceRequest(string PlayerName, string Service, int? Level, bool Confirmed);
public sealed record CharacterAccountTransferRequest(
    string PlayerName, uint DestinationAccountId, bool Confirmed);
public sealed record CharacterTransferAccount(
    uint AccountId, string Username, string Classification, int CharacterCount);
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
