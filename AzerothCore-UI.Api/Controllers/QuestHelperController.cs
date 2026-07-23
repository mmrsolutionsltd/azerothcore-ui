using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/quest-helper")]
public sealed class QuestHelperController(
    AzerothCoreConnectionFactory connectionFactory,
    AzerothCoreSoapClient soapClient,
    ILogger<QuestHelperController> logger) : ControllerBase
{
    [HttpGet("{guid:long}")]
    public async Task<ActionResult<QuestHelperDashboard>> GetDashboard(
        long guid, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (guid is < 0 or > uint.MaxValue) return BadRequest("The character GUID is outside the supported range.");

        await using var connection = connectionFactory.CreateConnection();
        var character = await connection.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition("""
            SELECT c.guid AS Guid, c.name AS Name, c.level AS Level, c.race AS Race,
                   c.class AS Class, c.online AS OnlineValue, c.map AS MapId,
                   c.zone AS ZoneId, c.position_x AS PositionX, c.position_y AS PositionY,
                   NULLIF(area.AreaName_Lang_enUS, '') AS DatabaseLocationName
            FROM acore_characters.characters c
            INNER JOIN acore_auth.account account ON account.id = c.account
            LEFT JOIN acore_world.areatable_dbc area ON area.ID = c.zone
            WHERE c.guid = @Guid AND account.username NOT LIKE 'rndbot%'
              AND account.username <> 'AHBOT';
            """, new { Guid = (uint)guid }, cancellationToken: cancellationToken));
        if (character is null) return NotFound();

        var activeRows = (await connection.QueryAsync<ActiveQuestRow>(new CommandDefinition("""
            SELECT status.quest AS QuestId, quest.LogTitle AS Title, quest.QuestLevel,
                   status.status AS Status,
                   CONCAT_WS('; ',
                     IF(quest.RequiredNpcOrGo1 <> 0, CONCAT(status.mobcount1, '/', quest.RequiredNpcOrGoCount1, ' objective 1'), NULL),
                     IF(quest.RequiredNpcOrGo2 <> 0, CONCAT(status.mobcount2, '/', quest.RequiredNpcOrGoCount2, ' objective 2'), NULL),
                     IF(quest.RequiredNpcOrGo3 <> 0, CONCAT(status.mobcount3, '/', quest.RequiredNpcOrGoCount3, ' objective 3'), NULL),
                     IF(quest.RequiredNpcOrGo4 <> 0, CONCAT(status.mobcount4, '/', quest.RequiredNpcOrGoCount4, ' objective 4'), NULL),
                     IF(quest.RequiredItemId1 <> 0, CONCAT(status.itemcount1, '/', quest.RequiredItemCount1, ' item 1'), NULL),
                     IF(quest.RequiredItemId2 <> 0, CONCAT(status.itemcount2, '/', quest.RequiredItemCount2, ' item 2'), NULL),
                     IF(quest.RequiredItemId3 <> 0, CONCAT(status.itemcount3, '/', quest.RequiredItemCount3, ' item 3'), NULL),
                     IF(quest.RequiredItemId4 <> 0, CONCAT(status.itemcount4, '/', quest.RequiredItemCount4, ' item 4'), NULL)
                   ) AS ObjectiveSummary
            FROM acore_characters.character_queststatus status
            INNER JOIN acore_world.quest_template quest ON quest.ID = status.quest
            WHERE status.guid = @Guid
            ORDER BY status.status = 1 DESC, quest.QuestLevel, quest.LogTitle;
            """, new { Guid = (uint)guid }, cancellationToken: cancellationToken))).ToArray();

        var raceMask = 1u << (character.Race - 1);
        var classMask = 1u << (character.Class - 1);
        var recommendations = (await connection.QueryAsync<RecommendationRow>(new CommandDefinition("""
            SELECT quest.ID AS QuestId, quest.LogTitle AS Title, quest.QuestLevel,
                   quest.MinLevel AS MinimumLevel, addon.PrevQuestID AS PreviousQuestId,
                   previous.LogTitle AS PreviousQuestTitle,
                   COALESCE((
                     SELECT template.name FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     INNER JOIN acore_world.creature_template template ON template.entry = spawn.id
                     WHERE starter.quest = quest.ID
                     ORDER BY spawn.map = @MapId DESC,
                              IF(spawn.map = @MapId,
                                 POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2),
                                 999999999), spawn.guid LIMIT 1
                   ), 'No creature quest giver') AS QuestGiver,
                   (SELECT spawn.guid FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     WHERE starter.quest = quest.ID
                     ORDER BY spawn.map = @MapId DESC,
                              IF(spawn.map = @MapId,
                                 POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2),
                                 999999999), spawn.guid LIMIT 1) AS QuestGiverSpawnId,
                   (SELECT spawn.map FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     WHERE starter.quest = quest.ID
                     ORDER BY spawn.map = @MapId DESC,
                              IF(spawn.map = @MapId,
                                 POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2),
                                 999999999), spawn.guid LIMIT 1) AS MapId,
                   (SELECT spawn.zoneId FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     WHERE starter.quest = quest.ID
                     ORDER BY spawn.map = @MapId DESC,
                              IF(spawn.map = @MapId,
                                 POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2),
                                 999999999), spawn.guid LIMIT 1) AS ZoneId,
                   (SELECT spawn.areaId FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     WHERE starter.quest = quest.ID
                     ORDER BY spawn.map = @MapId DESC,
                              IF(spawn.map = @MapId,
                                 POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2),
                                 999999999), spawn.guid LIMIT 1) AS AreaId,
                   EXISTS(SELECT 1 FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     WHERE starter.quest = quest.ID AND spawn.map = @MapId) AS SameMapValue,
                   (SELECT SQRT(POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2))
                     FROM acore_world.creature_queststarter starter
                     INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                     WHERE starter.quest = quest.ID AND spawn.map = @MapId
                     ORDER BY POW(spawn.position_x - @PositionX, 2) + POW(spawn.position_y - @PositionY, 2)
                     LIMIT 1) AS Distance
            FROM acore_world.quest_template quest
            LEFT JOIN acore_world.quest_template_addon addon ON addon.ID = quest.ID
            LEFT JOIN acore_world.quest_template previous ON previous.ID = ABS(addon.PrevQuestID)
            WHERE quest.MinLevel <= @MaximumMinimumLevel
              AND (quest.QuestLevel = -1 OR quest.QuestLevel >= @MinimumQuestLevel)
              AND (quest.QuestLevel = -1 OR quest.QuestLevel <= @MaximumQuestLevel)
              AND (quest.AllowableRaces = 0 OR (quest.AllowableRaces & @RaceMask) <> 0)
              AND (addon.AllowableClasses IS NULL OR addon.AllowableClasses = 0 OR (addon.AllowableClasses & @ClassMask) <> 0)
              AND NOT EXISTS (SELECT 1 FROM acore_characters.character_queststatus active
                              WHERE active.guid = @Guid AND active.quest = quest.ID)
              AND NOT EXISTS (SELECT 1 FROM acore_characters.character_queststatus_rewarded rewarded
                              WHERE rewarded.guid = @Guid AND rewarded.quest = quest.ID)
              AND (addon.PrevQuestID IS NULL OR addon.PrevQuestID = 0 OR EXISTS (
                    SELECT 1 FROM acore_characters.character_queststatus_rewarded prerequisite
                    WHERE prerequisite.guid = @Guid AND prerequisite.quest = ABS(addon.PrevQuestID)))
              AND EXISTS (SELECT 1 FROM acore_world.creature_queststarter starter WHERE starter.quest = quest.ID)
            ORDER BY SameMapValue DESC, Distance, ABS(quest.QuestLevel - @Level), quest.LogTitle
            LIMIT 100;
            """, new
            {
                Guid = (uint)guid,
                character.MapId,
                character.PositionX,
                character.PositionY,
                RaceMask = raceMask,
                ClassMask = classMask,
                MaximumMinimumLevel = character.Level + 2,
                MinimumQuestLevel = Math.Max(1, character.Level - 5),
                MaximumQuestLevel = character.Level + 5,
                character.Level
            }, cancellationToken: cancellationToken))).ToArray();

        return Ok(new QuestHelperDashboard(
            new(character.Guid, character.Name, character.Level, character.Race, character.Class,
                character.OnlineValue != 0, character.MapId, character.ZoneId,
                AreaNameResolver.Resolve(character.ZoneId, character.DatabaseLocationName)),
            activeRows.Select(row => new QuestHelperActiveQuest(
                row.QuestId, row.Title, row.QuestLevel, row.Status, StatusName(row.Status),
                string.IsNullOrWhiteSpace(row.ObjectiveSummary) ? "No tracked objectives" : row.ObjectiveSummary)).ToArray(),
            recommendations.Select(row => new QuestHelperRecommendation(
                row.QuestId, row.Title, row.QuestLevel, row.MinimumLevel,
                row.PreviousQuestId, row.PreviousQuestTitle, row.QuestGiver,
                row.QuestGiverSpawnId, row.MapId, row.ZoneId, row.AreaId,
                row.SameMapValue != 0, row.Distance)).ToArray()));
    }

    [HttpPost("add")]
    public Task<ActionResult<AdministrationResult>> AddQuest(
        QuestAdminRequest request, CancellationToken cancellationToken) =>
        ChangeQuest(request, "add", cancellationToken);

    [HttpPost("remove")]
    public Task<ActionResult<AdministrationResult>> RemoveQuest(
        QuestAdminRequest request, CancellationToken cancellationToken) =>
        ChangeQuest(request, "remove", cancellationToken);

    [HttpPost("teleport")]
    public async Task<ActionResult<AdministrationResult>> TeleportToQuestGiver(
        QuestGiverTeleportRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the quest-giver teleport first."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        await using (var connection = connectionFactory.CreateConnection())
        {
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(
                    SELECT 1 FROM acore_world.creature_queststarter starter
                    INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                    WHERE starter.quest = @QuestId AND spawn.guid = @SpawnId);
                """, new { request.QuestId, request.SpawnId }, cancellationToken: cancellationToken));
            if (!exists)
                return NotFound(new AdministrationResult(false, "That spawn is not a quest giver for the selected quest."));
        }
        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildTrainerTeleportCommand(player, request.SpawnId), cancellationToken);
        logger.LogInformation("Quest giver teleport: Player={Player}; Quest={QuestId}; Spawn={SpawnId}",
            player, request.QuestId, request.SpawnId);
        return Ok(new AdministrationResult(true, $"{player} was teleported to the quest giver.", output));
    }

    private async Task<ActionResult<AdministrationResult>> ChangeQuest(
        QuestAdminRequest request, string action, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, $"Confirm the quest {action} operation first."));
        if (request.QuestId == 0)
            return BadRequest(new AdministrationResult(false, "A quest ID is required."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildQuestCommand(player, request.QuestId, action == "add"), cancellationToken);
        logger.LogInformation("Quest administration: {Action}; Player={Player}; Quest={QuestId}",
            action, player, request.QuestId);
        return Ok(new AdministrationResult(true,
            $"Quest {request.QuestId} was {("add".Equals(action) ? "added to" : "removed from")} {player}.", output));
    }

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is null
        || System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress);

    private static string StatusName(byte status) => status switch
    {
        1 => "Ready to turn in",
        3 => "In progress",
        5 => "Failed",
        6 => "Rewarded",
        _ => $"Status {status}"
    };

    private sealed class CharacterRow
    {
        public uint Guid { get; init; }
        public string Name { get; init; } = "";
        public byte Level { get; init; }
        public byte Race { get; init; }
        public byte Class { get; init; }
        public byte OnlineValue { get; init; }
        public ushort MapId { get; init; }
        public ushort ZoneId { get; init; }
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public string? DatabaseLocationName { get; init; }
    }

    private sealed class ActiveQuestRow
    {
        public uint QuestId { get; init; }
        public string Title { get; init; } = "";
        public short QuestLevel { get; init; }
        public byte Status { get; init; }
        public string ObjectiveSummary { get; init; } = "";
    }

    private sealed class RecommendationRow
    {
        public uint QuestId { get; init; }
        public string Title { get; init; } = "";
        public short QuestLevel { get; init; }
        public byte MinimumLevel { get; init; }
        public int PreviousQuestId { get; init; }
        public string? PreviousQuestTitle { get; init; }
        public string QuestGiver { get; init; } = "";
        public uint? QuestGiverSpawnId { get; init; }
        public ushort? MapId { get; init; }
        public ushort? ZoneId { get; init; }
        public ushort? AreaId { get; init; }
        public byte SameMapValue { get; init; }
        public double? Distance { get; init; }
    }
}
