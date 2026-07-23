namespace AzerothCore_UI.Api.Models;

public sealed record QuestHelperCharacter(
    uint Guid, string Name, byte Level, byte Race, byte Class, bool Online,
    ushort MapId, ushort ZoneId, string LocationName);

public sealed record QuestHelperActiveQuest(
    uint QuestId, string Title, short QuestLevel, byte Status, string StatusName,
    string ObjectiveSummary);

public sealed record QuestHelperRecommendation(
    uint QuestId, string Title, short QuestLevel, byte MinimumLevel,
    int PreviousQuestId, string? PreviousQuestTitle, string QuestGiver,
    uint? QuestGiverSpawnId, ushort? MapId, ushort? ZoneId, ushort? AreaId,
    bool SameMap, double? Distance);

public sealed record QuestHelperDashboard(
    QuestHelperCharacter Character,
    IReadOnlyList<QuestHelperActiveQuest> ActiveQuests,
    IReadOnlyList<QuestHelperRecommendation> RecommendedQuests);

public sealed record QuestAdminRequest(string PlayerName, uint QuestId, bool Confirmed);
public sealed record QuestGiverTeleportRequest(string PlayerName, uint QuestId, uint SpawnId, bool Confirmed);
