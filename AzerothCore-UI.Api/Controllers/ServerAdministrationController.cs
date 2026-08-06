using System.Net;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using AzerothCore_UI.Api.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using AzerothCore_UI.Api.Security;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/server-administration")]
public sealed class ServerAdministrationController(
    AzerothCoreServerManager serverManager,
    AzerothCoreSoapClient soapClient,
    AzerothCoreConfigurationManager configurationManager,
    AzerothCoreConnectionFactory connectionFactory,
    SpellMetadataProvider spellMetadataProvider,
    DungeonGuideService dungeonGuideService,
    DatabaseBackupService databaseBackupService,
    ILogger<ServerAdministrationController> logger) : ControllerBase
{
    internal const string QuestingCompanionCandidateSql = """
        SELECT c.name Name, a.username Username, c.account AccountId, c.level Level,
               c.class CharacterClass, c.race Race, c.online<>0 Online,
               CASE
                 WHEN c.race IN (1,3,4,7,11)
                   THEN @LeaderRace IN (1,3,4,7,11)
                 ELSE @LeaderRace IN (2,5,6,8,10)
               END SameFaction,
               c.account=@LeaderAccount SameAccount,
               (@LeaderGuild<>0 AND gm.guildid=@LeaderGuild) SameGuild
        FROM acore_characters.characters c
        JOIN acore_auth.account a ON a.id=c.account
        LEFT JOIN acore_characters.guild_member gm ON gm.guid=c.guid
        WHERE c.name<>@Leader
          AND a.username NOT LIKE CONCAT(@BotPrefix, '%')
          AND UPPER(a.username)<>'AHBOT'
          AND (@AllAccounts OR c.account IN @AllowedAccounts)
        ORDER BY c.online, SameFaction DESC, SameAccount,
                 ABS(CAST(c.level AS SIGNED) - CAST(@LeaderLevel AS SIGNED)),
                 c.level, c.name
        """;

    private static readonly UtilityNpc[] UtilityNpcs =
    [
        new(12959, "Nergal", "General goods", "Basic supplies and somewhere to sell unwanted items.", 52),
        new(12958, "Gigget Zipcoil", "Trade supplies", "Common trade and profession supplies.", 52),
        new(4085, "Nizzik", "Armour and repair", "Armour merchant who buys items and repairs equipment.", 24),
        new(3534, "Wallace the Blind", "Weapons and repair", "Weaponsmith who buys items and repairs equipment.", 19),
        new(5411, "Krinkle Goodsteel", "Blacksmithing supplies", "Blacksmithing supplies, sales and repairs.", 40),
        new(22479, "Sab'aoth", "Reagents and poisons", "Spell reagents and poison supplies.", 68),
        new(19572, "Gant", "Food and drink", "Food, drink and somewhere to sell unwanted items.", 65),
        new(14337, "Field Repair Bot 74A", "Repair bot", "Portable sales and repair service.", 50)
    ];

    [HttpGet("status")]
    public async Task<ActionResult<ServerStatus>> GetStatus(CancellationToken cancellationToken) =>
        IsLocalRequest() ? Ok(await serverManager.GetStatusAsync(cancellationToken)) : NotFound();

    [HttpGet("availability")]
    public async Task<ActionResult<ToolAvailability>> GetToolAvailability(
        CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var status = await serverManager.GetStatusAsync(cancellationToken);
        return Ok(new ToolAvailability(
            status.WorldServer.IsRunning, status.SoapConfigured, status.SoapReachable));
    }

    [HttpGet("players")]
    public async Task<ActionResult<IReadOnlyList<AdministrationPlayer>>> GetPlayers(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        const string sql = """
            SELECT
                characterData.name AS Name,
                account.username AS Username,
                characterData.online <> 0 AS Online,
                CASE WHEN account.username LIKE CONCAT(@BotPrefix, '%') THEN 'PlayerBot' ELSE 'Human' END AS Classification
            FROM acore_characters.characters characterData
            INNER JOIN acore_auth.account account ON account.id = characterData.account
            WHERE characterData.name <> ''
              AND (@AllAccounts OR characterData.account IN @AllowedAccounts)
            ORDER BY characterData.online DESC,
                     CASE WHEN account.username LIKE CONCAT(@BotPrefix, '%') THEN 1 ELSE 0 END,
                     characterData.name
            LIMIT 5000;
            """;
        await using var connection = connectionFactory.CreateConnection();
        var identity = HttpContext.AdministrationIdentity();
        var players = await connection.QueryAsync<AdministrationPlayer>(new CommandDefinition(
            sql, new {
                BotPrefix = "rndbot",
                AllAccounts = identity?.AccountScope == "All",
                AllowedAccounts = identity?.GameAccountIds ?? []
            }, cancellationToken: cancellationToken));
        return Ok(players.AsList());
    }

    [HttpGet("items")]
    public async Task<ActionResult<AdministrationItemSearchResult>> GetItems(
        [FromQuery] string? search, [FromQuery] string category = "all", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30, [FromQuery] int? quality = null,
        [FromQuery] int? minimumItemLevel = null, [FromQuery] int? maximumItemLevel = null,
        [FromQuery] int? minimumRequiredLevel = null, [FromQuery] int? maximumRequiredLevel = null,
        [FromQuery] string? targetNames = null, [FromQuery] string suitability = "off",
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var categoryFilter = GetItemCategoryFilter(category);
        if (categoryFilter is null) return BadRequest("Unknown item category.");
        if (quality is < 0 or > 7) return BadRequest("Unknown item quality.");
        if (minimumItemLevel is < 0 || maximumItemLevel is < 0
            || minimumRequiredLevel is < 0 || maximumRequiredLevel is < 0)
            return BadRequest("Level filters cannot be negative.");
        if (minimumItemLevel > maximumItemLevel || minimumRequiredLevel > maximumRequiredLevel)
            return BadRequest("A minimum level cannot be greater than its maximum.");
        if (suitability is not ("off" or "any" or "all"))
            return BadRequest("Unknown suitability filter.");

        var requestedTargetNames = (targetNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        await using var connection = connectionFactory.CreateConnection();
        var identity = HttpContext.AdministrationIdentity();
        var targets = requestedTargetNames.Length == 0
            ? []
            : (await connection.QueryAsync<ItemTargetRow>(new CommandDefinition("""
                SELECT characterData.guid AS Guid, characterData.name AS Name,
                       characterData.class AS CharacterClass, characterData.race AS Race,
                       characterData.level AS CharacterLevel
                FROM acore_characters.characters characterData
                WHERE characterData.name IN @TargetNames
                  AND (@AllAccounts OR characterData.account IN @AllowedAccounts);
                """, new
                {
                    TargetNames = requestedTargetNames,
                    AllAccounts = identity?.AccountScope == "All",
                    AllowedAccounts = identity?.GameAccountIds ?? []
                }, cancellationToken: cancellationToken))).AsList();
        var suitabilityFilter = suitability == "off" || targets.Count == 0
            ? ""
            : $" AND ({string.Join(suitability == "all" ? " AND " : " OR ",
                targets.Select(ItemCompatibilitySql))})";
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var where = $"""
            WHERE item.name <> ''
              AND (@Search IS NULL OR item.name LIKE CONCAT('%', @Search, '%'))
              AND (@Quality IS NULL OR item.Quality = @Quality)
              AND (@MinimumItemLevel IS NULL OR item.ItemLevel >= @MinimumItemLevel)
              AND (@MaximumItemLevel IS NULL OR item.ItemLevel <= @MaximumItemLevel)
              AND (@MinimumRequiredLevel IS NULL OR item.RequiredLevel >= @MinimumRequiredLevel)
              AND (@MaximumRequiredLevel IS NULL OR item.RequiredLevel <= @MaximumRequiredLevel)
              {categoryFilter}
              {suitabilityFilter}
            """;
        var sql = $"""
            SELECT COUNT(*) FROM acore_world.item_template item {where};
            SELECT item.entry AS ItemId, item.name AS Name, item.class AS ItemClass,
                   item.subclass AS ItemSubclass, item.Quality AS Quality,
                   item.ItemLevel AS ItemLevel, item.RequiredLevel AS RequiredLevel,
                   item.InventoryType AS InventoryType,
                   CAST(item.AllowableClass AS SIGNED) AS AllowableClass,
                   CAST(item.AllowableRace AS SIGNED) AS AllowableRace
            FROM acore_world.item_template item
            {where}
            ORDER BY item.name, item.entry
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new
        {
            Search = normalizedSearch, Quality = quality,
            MinimumItemLevel = minimumItemLevel, MaximumItemLevel = maximumItemLevel,
            MinimumRequiredLevel = minimumRequiredLevel, MaximumRequiredLevel = maximumRequiredLevel,
            PageSize = pageSize, Offset = (page - 1) * pageSize
        };
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var items = (await results.ReadAsync<AdministrationItem>()).AsList();
        Dictionary<(ulong Guid, int SlotGroup), int> equippedLevels = [];
        if (targets.Count > 0 && items.Count > 0)
        {
            var equipped = await connection.QueryAsync<EquippedItemLevelRow>(new CommandDefinition("""
                SELECT inventory.guid AS Guid, template.InventoryType AS InventoryType,
                       MAX(template.ItemLevel) AS ItemLevel
                FROM acore_characters.character_inventory inventory
                INNER JOIN acore_characters.item_instance instance ON instance.guid = inventory.item
                INNER JOIN acore_world.item_template template ON template.entry = instance.itemEntry
                WHERE inventory.guid IN @Guids AND inventory.bag = 0 AND inventory.slot < 19
                GROUP BY inventory.guid, template.InventoryType;
                """, new { Guids = targets.Select(target => target.Guid).ToArray() },
                cancellationToken: cancellationToken));
            equippedLevels = equipped
                .GroupBy(row => (row.Guid, InventorySlotGroup(row.InventoryType)))
                .ToDictionary(group => group.Key, group => group.Max(row => row.ItemLevel));
        }
        foreach (var item in items)
        {
            var suitable = targets.Where(target => ItemCompatible(item, target)).ToArray();
            item.TargetCount = targets.Count;
            item.SuitableTargetCount = suitable.Length;
            item.SuitableTargetNames = suitable.Select(target => target.Name).ToArray();
            item.IncompatibleTargetNames = targets.Except(suitable).Select(target => target.Name).ToArray();
            var slotGroup = InventorySlotGroup(item.InventoryType);
            item.LikelyUpgradeTargetCount = slotGroup == 0 ? 0 : suitable.Count(target =>
                !equippedLevels.TryGetValue((target.Guid, slotGroup), out var equippedLevel)
                || item.ItemLevel > equippedLevel);
        }
        return Ok(new AdministrationItemSearchResult(items, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("teleport-locations")]
    public async Task<ActionResult<TeleportLocationSearchResult>> GetTeleportLocations(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        const string sql = """
            SELECT COUNT(*)
            FROM acore_world.game_tele location
            WHERE @Search IS NULL OR location.name LIKE CONCAT('%', @Search, '%');

            SELECT location.id AS Id, location.name AS Name, location.map AS MapId,
                   location.position_x AS PositionX, location.position_y AS PositionY,
                   location.position_z AS PositionZ
            FROM acore_world.game_tele location
            WHERE @Search IS NULL OR location.name LIKE CONCAT('%', @Search, '%')
            ORDER BY location.name
            LIMIT @PageSize OFFSET @Offset;
            """;
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql,
            new { Search = normalizedSearch, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var locations = (await results.ReadAsync<TeleportLocation>()).AsList();
        return Ok(new TeleportLocationSearchResult(locations, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("npc-teleports")]
    public async Task<ActionResult<NpcTeleportSearchResult>> GetNpcTeleports(
        [FromQuery] string characterName, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        var character = AzerothCoreSoapClient.RequirePlayerName(characterName);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var normalizedSearch = search?.Trim() ?? string.Empty;
        if (normalizedSearch.Length < 2)
            return Ok(new NpcTeleportSearchResult([], page, pageSize, 0, 0));

        await using var connection = connectionFactory.CreateConnection();
        var context = await connection.QuerySingleOrDefaultAsync<TrainerCharacterContext>(
            new CommandDefinition("""
                SELECT race AS CharacterRace, class AS CharacterClass, map AS MapId,
                       position_x AS PositionX, position_y AS PositionY
                FROM acore_characters.characters c
                WHERE name = @Character LIMIT 1;
                """, new { Character = character }, cancellationToken: cancellationToken));
        if (context is null)
            return NotFound(new AdministrationResult(false, "That character does not exist."));

        const string sql = """
            SELECT COUNT(*)
            FROM acore_world.creature spawn
            INNER JOIN acore_world.creature_template template ON template.entry = spawn.id
            WHERE spawn.map IN (0, 1, 530, 571, 609)
              AND template.name NOT LIKE '[UNUSED]%'
              AND template.name LIKE CONCAT('%', @Search, '%');

            SELECT spawn.guid AS SpawnId, template.entry AS CreatureId,
                   template.name AS Name, COALESCE(template.subname, '') AS Subname,
                   spawn.map AS MapId, spawn.zoneId AS ZoneId, spawn.areaId AS AreaId,
                   spawn.map = @MapId AS SameMap,
                   faction.ID IS NULL
                     OR (faction.EnemyGroup & @PlayerFactionGroup) <> 0
                     OR (@PlayerEnemyGroup & faction.FactionGroup) <> 0
                     AS PotentiallyHostile,
                   CASE WHEN spawn.map = @MapId
                        THEN SQRT(POW(spawn.position_x - @PositionX, 2)
                            + POW(spawn.position_y - @PositionY, 2))
                        ELSE NULL END AS Distance
            FROM acore_world.creature spawn
            INNER JOIN acore_world.creature_template template ON template.entry = spawn.id
            LEFT JOIN acore_world.factiontemplate_dbc faction ON faction.ID = template.faction
            WHERE spawn.map IN (0, 1, 530, 571, 609)
              AND template.name NOT LIKE '[UNUSED]%'
              AND template.name LIKE CONCAT('%', @Search, '%')
            ORDER BY SameMap DESC, Distance IS NULL, Distance, template.name, spawn.guid
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new
        {
            Search = normalizedSearch,
            PlayerFactionGroup = IsAllianceRace(context.CharacterRace) ? 3 : 5,
            PlayerEnemyGroup = IsAllianceRace(context.CharacterRace) ? 12 : 10,
            context.MapId,
            context.PositionX,
            context.PositionY,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        };
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var npcs = (await results.ReadAsync<NpcTeleportSpawn>()).AsList();
        return Ok(new NpcTeleportSearchResult(npcs, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("trainers")]
    public async Task<ActionResult<TrainerSearchResult>> GetTrainers(
        [FromQuery] string characterName, [FromQuery] string? search,
        [FromQuery] string category = "all", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        var character = AzerothCoreSoapClient.RequirePlayerName(characterName);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var normalizedCategory = category.ToLowerInvariant();
        if (normalizedCategory is not ("all" or "class" or "profession" or "weapon" or "riding" or "stable"))
            return BadRequest(new AdministrationResult(false, "Unknown trainer category."));
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        await using var connection = connectionFactory.CreateConnection();
        var context = await connection.QuerySingleOrDefaultAsync<TrainerCharacterContext>(new CommandDefinition("""
            SELECT race AS CharacterRace, class AS CharacterClass, map AS MapId,
                   position_x AS PositionX, position_y AS PositionY
            FROM acore_characters.characters
            WHERE name = @CharacterName LIMIT 1;
            """, new { CharacterName = character }, cancellationToken: cancellationToken));
        if (context is null)
            return NotFound(new AdministrationResult(false, "That character does not exist."));

        var categoryFilter = normalizedCategory == "all" ? "" : "AND trainerSpawn.Category = @Category";
        var sql = $"""
            WITH trainer_spawns AS
            (
                SELECT creature.guid AS SpawnId, template.entry AS CreatureId,
                       template.name AS Name, COALESCE(template.subname, '') AS Subname,
                       CASE
                           WHEN trainer.Type = 0 AND trainer.Requirement BETWEEN 1 AND 11
                                AND template.entry NOT BETWEEN 26324 AND 26332 THEN 'class'
                           WHEN template.subname REGEXP '^(Alchemy|Blacksmithing|Enchanting|Engineering|Herbalism|Inscription|Jewelcrafting|Leatherworking|Mining|Skinning|Tailoring|Cooking|First Aid|Fishing) Trainer' THEN 'profession'
                           WHEN template.subname LIKE '%Weapon Master%' THEN 'weapon'
                           WHEN template.subname LIKE '%Riding Trainer%' OR template.subname LIKE '%Riding Instructor%' THEN 'riding'
                           WHEN template.subname LIKE '%Stable Master%' THEN 'stable'
                           ELSE 'other'
                       END AS Category,
                       creature.map AS MapId, creature.zoneId AS ZoneId, creature.areaId AS AreaId,
                       creature.map = @MapId AS SameMap,
                       CASE WHEN creature.map = @MapId
                           THEN SQRT(POW(creature.position_x - @PositionX, 2) + POW(creature.position_y - @PositionY, 2))
                           ELSE NULL END AS Distance,
                       trainer.Type AS TrainerType, trainer.Requirement AS TrainerRequirement,
                       template.faction AS Faction
                FROM acore_world.creature creature
                INNER JOIN acore_world.creature_template template ON template.entry = creature.id
                LEFT JOIN acore_world.creature_default_trainer defaultTrainer ON defaultTrainer.CreatureId = template.entry
                LEFT JOIN acore_world.trainer trainer ON trainer.Id = defaultTrainer.TrainerId
                WHERE template.name NOT LIKE '[UNUSED]%'
            )
            SELECT trainerSpawn.SpawnId, trainerSpawn.CreatureId, trainerSpawn.Name, trainerSpawn.Subname,
                   trainerSpawn.Category, trainerSpawn.MapId, trainerSpawn.ZoneId, trainerSpawn.AreaId,
                   trainerSpawn.SameMap, trainerSpawn.Distance
            FROM trainer_spawns trainerSpawn
            WHERE trainerSpawn.Category IN ('class', 'profession', 'weapon', 'riding', 'stable')
              AND (trainerSpawn.Category <> 'class' OR trainerSpawn.TrainerRequirement = @CharacterClass)
              AND trainerSpawn.Faction NOT IN @HostileFactions
              AND (@Search IS NULL OR trainerSpawn.Name LIKE CONCAT('%', @Search, '%')
                   OR trainerSpawn.Subname LIKE CONCAT('%', @Search, '%'))
              {categoryFilter}
            ORDER BY trainerSpawn.SameMap DESC, trainerSpawn.Distance IS NULL,
                     trainerSpawn.Distance, trainerSpawn.Name, trainerSpawn.SpawnId;
            """;
        var parameters = new
        {
            Category = normalizedCategory, Search = normalizedSearch, context.CharacterClass,
            HostileFactions = GetHostileTrainerFactions(context.CharacterRace),
            context.MapId, context.PositionX, context.PositionY
        };
        var matchingTrainers = (await connection.QueryAsync<TrainerSpawn>(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken)));
        var allTrainers = matchingTrainers.AsList();
        var total = allTrainers.Count;
        var trainers = allTrainers.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Ok(new TrainerSearchResult(trainers, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpPost("trainers/teleport")]
    public async Task<ActionResult<AdministrationResult>> TeleportToTrainer(
        TeleportToTrainerRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the trainer teleport first."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        if (request.SpawnId == 0)
            return BadRequest(new AdministrationResult(false, "Trainer spawn ID is required."));

        await using (var connection = connectionFactory.CreateConnection())
        {
            var characterContext = await connection.QuerySingleOrDefaultAsync<TrainerCharacterContext>(
                new CommandDefinition("""
                    SELECT race AS CharacterRace, class AS CharacterClass
                    FROM acore_characters.characters
                    WHERE name = @Player LIMIT 1;
                    """, new { Player = player }, cancellationToken: cancellationToken));
            if (characterContext is null)
                return NotFound(new AdministrationResult(false, "That character does not exist."));

            var trainerName = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT template.name
                FROM acore_world.creature creature
                INNER JOIN acore_world.creature_template template ON template.entry = creature.id
                LEFT JOIN acore_world.creature_default_trainer defaultTrainer ON defaultTrainer.CreatureId = template.entry
                LEFT JOIN acore_world.trainer trainer ON trainer.Id = defaultTrainer.TrainerId
                WHERE creature.guid = @SpawnId AND template.name NOT LIKE '[UNUSED]%'
                  AND template.faction NOT IN @HostileFactions
                  AND (
                      (trainer.Type = 0 AND trainer.Requirement BETWEEN 1 AND 11
                       AND trainer.Requirement = @CharacterClass
                       AND template.entry NOT BETWEEN 26324 AND 26332)
                      OR template.subname REGEXP '^(Alchemy|Blacksmithing|Enchanting|Engineering|Herbalism|Inscription|Jewelcrafting|Leatherworking|Mining|Skinning|Tailoring|Cooking|First Aid|Fishing) Trainer'
                      OR template.subname LIKE '%Weapon Master%'
                      OR template.subname LIKE '%Riding Trainer%'
                      OR template.subname LIKE '%Riding Instructor%'
                      OR template.subname LIKE '%Stable Master%'
                )
                LIMIT 1;
                """, new
                {
                    request.SpawnId,
                    characterContext.CharacterClass,
                    HostileFactions = GetHostileTrainerFactions(characterContext.CharacterRace)
                }, cancellationToken: cancellationToken));
            if (trainerName is null)
                return NotFound(new AdministrationResult(false, "That trainer spawn does not exist."));
        }

        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildTrainerTeleportCommand(player, request.SpawnId), cancellationToken);
        Audit("TeleportToTrainer", player, $"Spawn={request.SpawnId}");
        return Ok(new AdministrationResult(true, $"{player} was teleported to the selected trainer.", output));
    }

    [HttpGet("collectibles")]
    public async Task<ActionResult<CollectibleSearchResult>> GetCollectibles(
        [FromQuery] string? search, [FromQuery] string type = "all", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var typeFilter = type.ToLowerInvariant() switch
        {
            "all" => "",
            "mount" => "AND item.subclass = 5",
            "companion" => "AND item.subclass = 2",
            _ => null
        };
        if (typeFilter is null) return BadRequest(new AdministrationResult(false, "Unknown collectible type."));
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var where = $"WHERE item.class = 15 AND item.subclass IN (2, 5) AND item.spellid_1 <> 0 " +
                    $"AND item.name <> '' AND (@Search IS NULL OR item.name LIKE CONCAT('%', @Search, '%')) {typeFilter}";
        var sql = $"""
            SELECT COUNT(*) FROM acore_world.item_template item {where};
            SELECT item.entry AS ItemId, item.name AS Name,
                   CASE WHEN item.subclass = 5 THEN 'Mount' ELSE 'Companion' END AS Type,
                   item.spellid_1 AS LearnSpellId, item.RequiredLevel, item.Quality
            FROM acore_world.item_template item {where}
            ORDER BY item.name, item.entry LIMIT @PageSize OFFSET @Offset;
            """;
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql,
            new { Search = normalizedSearch, PageSize = pageSize, Offset = (page - 1) * pageSize }, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var items = (await results.ReadAsync<CollectibleItem>()).AsList();
        return Ok(new CollectibleSearchResult(items, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("collectibles/collection")]
    public async Task<ActionResult<CharacterCollectibleSearchResult>> GetCharacterCollectibles(
        [FromQuery] string characterName, [FromQuery] string? search, [FromQuery] string type = "all",
        [FromQuery] bool missingOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        var character = AzerothCoreSoapClient.RequirePlayerName(characterName);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var typeFilter = type.ToLowerInvariant() switch
        {
            "all" => "", "mount" => "AND item.subclass = 5", "companion" => "AND item.subclass = 2", _ => null
        };
        if (typeFilter is null) return BadRequest(new AdministrationResult(false, "Unknown collectible type."));
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var sql = $"""
            SELECT item.entry AS ItemId, item.name AS Name,
                   CASE WHEN item.subclass = 5 THEN 'Mount' ELSE 'Companion' END AS Type,
                   item.spellid_1 AS LearnSpellId, item.RequiredLevel, item.Quality
            FROM acore_world.item_template item
            WHERE item.class = 15 AND item.subclass IN (2, 5) AND item.spellid_1 <> 0 AND item.name <> ''
              AND (@Search IS NULL OR item.name LIKE CONCAT('%', @Search, '%')) {typeFilter}
            ORDER BY item.name, item.entry;
            """;
        await using var connection = connectionFactory.CreateConnection();
        var context = await connection.QuerySingleOrDefaultAsync<CharacterCollectibleContext>(new CommandDefinition("""
            SELECT guid AS CharacterGuid, level AS CharacterLevel
            FROM acore_characters.characters WHERE name = @CharacterName LIMIT 1;
            """, new { CharacterName = character }, cancellationToken: cancellationToken));
        if (context is null) return NotFound(new AdministrationResult(false, "That character does not exist."));
        var learned = (await connection.QueryAsync<int>(new CommandDefinition(
            AzerothCoreQueries.CharacterLearnedSpells,
            new { context.CharacterGuid }, cancellationToken: cancellationToken))).ToHashSet();
        var rawItems = (await connection.QueryAsync<CollectibleItem>(new CommandDefinition(
            sql, new { Search = normalizedSearch }, cancellationToken: cancellationToken))).AsList();
        var allItems = rawItems.Select(item =>
        {
            var taughtSpell = spellMetadataProvider.Find((uint)item.LearnSpellId)?.LearnedSpellId;
            var learnedSpellId = checked((int)(taughtSpell ?? (uint)item.LearnSpellId));
            return new CharacterCollectibleItem(item.ItemId, item.Name, item.Type, learnedSpellId,
                item.RequiredLevel, item.Quality, learned.Contains(learnedSpellId), context.CharacterLevel >= item.RequiredLevel);
        }).ToArray();
        var knownCount = allItems.Count(item => item.Known);
        var filtered = missingOnly ? allItems.Where(item => !item.Known).ToArray() : allItems;
        var total = filtered.Length;
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Ok(new CharacterCollectibleSearchResult(items, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize), knownCount, allItems.Length - knownCount));
    }

    [HttpGet("creatures")]
    public async Task<ActionResult<AdministrationCreatureSearchResult>> GetCreatures(
        [FromQuery] string? search, [FromQuery] string filter = "tameable", [FromQuery] uint family = 0,
        [FromQuery] int? minimumLevel = null, [FromQuery] int? maximumLevel = null,
        [FromQuery] string sort = "name", [FromQuery] bool descending = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var filterSql = filter.ToLowerInvariant() switch
        {
            "all" => "",
            "tameable" => "AND template.type = 1 AND template.family <> 0 AND (template.type_flags & 1) <> 0",
            "exotic" => "AND template.type = 1 AND template.family <> 0 AND (template.type_flags & 1) <> 0 AND (template.type_flags & 65536) <> 0",
            _ => null
        };
        if (filterSql is null) return BadRequest(new AdministrationResult(false, "Unknown creature filter."));
        if (minimumLevel is < 1 or > 83 || maximumLevel is < 1 or > 83 || minimumLevel > maximumLevel)
            return BadRequest(new AdministrationResult(false, "Level filters must be between 1 and 83, with minimum no greater than maximum."));
        var orderBy = sort.ToLowerInvariant() switch
        {
            "name" => descending ? "template.name DESC, template.entry" : "template.name, template.entry",
            "level" => descending
                ? "template.minlevel DESC, template.maxlevel DESC, template.name"
                : "template.minlevel, template.maxlevel, template.name",
            _ => null
        };
        if (orderBy is null) return BadRequest(new AdministrationResult(false, "Unknown creature sort."));
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var where = $"WHERE template.name <> '' AND template.npcflag = 0 AND template.rank <> 3 " +
                    $"AND (@Search IS NULL OR template.name LIKE CONCAT('%', @Search, '%')) " +
                    $"AND (@Family = 0 OR template.family = @Family) " +
                    $"AND (@MinimumLevel IS NULL OR template.maxlevel >= @MinimumLevel) " +
                    $"AND (@MaximumLevel IS NULL OR template.minlevel <= @MaximumLevel) {filterSql}";
        var sql = $"""
            SELECT COUNT(*) FROM acore_world.creature_template template {where};
            SELECT template.entry AS CreatureId, template.name AS Name,
                   template.minlevel AS MinimumLevel, template.maxlevel AS MaximumLevel,
                   template.type AS CreatureType, template.family AS Family,
                   (template.type = 1 AND template.family <> 0 AND (template.type_flags & 1) <> 0) AS Tameable,
                   ((template.type_flags & 65536) <> 0) AS Exotic
            FROM acore_world.creature_template template
            {where}
            ORDER BY {orderBy}
            LIMIT @PageSize OFFSET @Offset;
            """;
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql,
            new { Search = normalizedSearch, Family = family, MinimumLevel = minimumLevel,
                MaximumLevel = maximumLevel, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var creatures = (await results.ReadAsync<AdministrationCreature>()).AsList();
        return Ok(new AdministrationCreatureSearchResult(creatures, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    private static string? GetItemCategoryFilter(string category) => category.ToLowerInvariant() switch
    {
        "all" => "",
        "weapons" => "AND item.class = 2",
        "armor" => "AND item.class = 4",
        "potions" => "AND item.class = 0 AND item.subclass = 1",
        "elixirs" => "AND item.class = 0 AND item.subclass = 2",
        "flasks" => "AND item.class = 0 AND item.subclass = 3",
        "scrolls" => "AND item.class = 0 AND item.subclass = 4",
        "food" => "AND item.class = 0 AND item.subclass = 5",
        "bags" => "AND item.class = 1",
        "gems" => "AND item.class = 3",
        "reagents" => "AND item.class = 5",
        "trade-goods" => "AND item.class = 7",
        "recipes" => "AND item.class = 9",
        "quest" => "AND item.class = 12",
        "miscellaneous" => "AND item.class = 15",
        "glyphs" => "AND item.class = 16",
        _ => null
    };

    private static string ItemCompatibilitySql(ItemTargetRow target)
    {
        var classMask = 1L << (target.CharacterClass - 1);
        var raceMask = 1L << (target.Race - 1);
        var weaponSubclasses = string.Join(',', WeaponSubclassesForClass(target.CharacterClass));
        var armorSubclasses = string.Join(',', ArmorSubclassesForClass(
            target.CharacterClass, target.CharacterLevel));
        return $"""
            (item.RequiredLevel <= {target.CharacterLevel}
             AND (CAST(item.AllowableClass AS SIGNED) IN (-1, 0)
                  OR (CAST(item.AllowableClass AS SIGNED) & {classMask}) <> 0)
             AND (CAST(item.AllowableRace AS SIGNED) IN (-1, 0)
                  OR (CAST(item.AllowableRace AS SIGNED) & {raceMask}) <> 0)
             AND (item.class NOT IN (2, 4)
                  OR (item.class = 2 AND item.subclass IN ({weaponSubclasses}))
                  OR (item.class = 4 AND item.subclass IN ({armorSubclasses}))))
            """;
    }

    private static bool ItemCompatible(AdministrationItem item, ItemTargetRow target)
    {
        var classMask = 1L << (target.CharacterClass - 1);
        var raceMask = 1L << (target.Race - 1);
        return item.RequiredLevel <= target.CharacterLevel
            && (item.AllowableClass is -1 or 0 || (item.AllowableClass & classMask) != 0)
            && (item.AllowableRace is -1 or 0 || (item.AllowableRace & raceMask) != 0)
            && (item.ItemClass != 2
                || WeaponSubclassesForClass(target.CharacterClass).Contains(item.ItemSubclass))
            && (item.ItemClass != 4
                || ArmorSubclassesForClass(target.CharacterClass, target.CharacterLevel)
                    .Contains(item.ItemSubclass));
    }

    private static int[] WeaponSubclassesForClass(int characterClass) => characterClass switch
    {
        1 => [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 13, 15, 16, 18],
        2 => [0, 1, 4, 5, 6, 7, 8],
        3 => [0, 1, 2, 3, 6, 7, 8, 10, 13, 15, 18],
        4 => [0, 2, 3, 4, 7, 13, 15, 16, 18],
        5 => [4, 10, 15, 19],
        6 => [0, 1, 4, 5, 6, 7, 8],
        7 => [0, 1, 4, 5, 10, 13, 15],
        8 or 9 => [7, 10, 15, 19],
        11 => [4, 5, 6, 10, 13, 15],
        _ => [0]
    };

    private static int[] ArmorSubclassesForClass(int characterClass, int characterLevel) =>
        characterClass switch
        {
            1 => characterLevel >= 40 ? [0, 1, 2, 3, 4, 6] : [0, 1, 2, 3, 6],
            2 => characterLevel >= 40 ? [0, 1, 2, 3, 4, 6, 7] : [0, 1, 2, 3, 6, 7],
            3 => characterLevel >= 40 ? [0, 1, 2, 3] : [0, 1, 2],
            7 => characterLevel >= 40 ? [0, 1, 2, 3, 6, 9] : [0, 1, 2, 6, 9],
            4 => [0, 1, 2],
            11 => [0, 1, 2, 8],
            5 or 8 or 9 => [0, 1],
            6 => [0, 1, 2, 3, 4, 10],
            _ => [0]
        };

    private static int InventorySlotGroup(int inventoryType) => inventoryType switch
    {
        1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 or 20 => 5, 6 => 6, 7 => 7,
        8 => 8, 9 => 9, 10 => 10, 11 => 11, 12 => 12, 13 or 17 or 21 => 13,
        14 or 22 or 23 => 14, 15 or 25 or 26 or 28 => 15, 16 => 16, 19 => 19,
        _ => 0
    };

    [HttpGet("settings/playerbots")]
    public ActionResult<PlayerBotSettings> GetPlayerBotSettings() =>
        IsLocalRequest() ? Ok(configurationManager.GetPlayerBotSettings()) : NotFound();

    [HttpPut("settings/playerbots")]
    public async Task<ActionResult<PlayerBotSettings>> UpdatePlayerBotSettings(
        UpdatePlayerBotSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        try
        {
            var result = await configurationManager.UpdatePlayerBotSettingsAsync(request, cancellationToken);
            logger.LogWarning("ADMIN AUDIT: PlayerBots configuration updated. A server restart is required.");
            return Ok(result);
        }
        catch (ArgumentException exception) { return BadRequest(new AdministrationResult(false, exception.Message)); }
        catch (InvalidOperationException exception) { return Conflict(new AdministrationResult(false, exception.Message)); }
    }

    [HttpGet("settings/rates")]
    public ActionResult<GameplayRateSettings> GetGameplayRateSettings() =>
        IsLocalRequest() ? Ok(configurationManager.GetGameplayRateSettings()) : NotFound();

    [HttpPut("settings/rates")]
    public async Task<ActionResult<GameplayRateSettings>> UpdateGameplayRateSettings(
        UpdateGameplayRateSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        try
        {
            var result = await configurationManager.UpdateGameplayRateSettingsAsync(request, cancellationToken);
            logger.LogWarning("ADMIN AUDIT: Gameplay rates configuration updated. A server restart is required.");
            return Ok(result);
        }
        catch (ArgumentException exception) { return BadRequest(new AdministrationResult(false, exception.Message)); }
        catch (InvalidOperationException exception) { return Conflict(new AdministrationResult(false, exception.Message)); }
    }

    [HttpGet("settings/auction-house-bot")]
    public ActionResult<AuctionHouseBotSettings> GetAuctionHouseBotSettings() =>
        IsLocalRequest() ? Ok(configurationManager.GetAuctionHouseBotSettings()) : NotFound();

    [HttpPut("settings/auction-house-bot")]
    public async Task<ActionResult<AuctionHouseBotSettings>> UpdateAuctionHouseBotSettings(
        AuctionHouseBotSettings request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var result = await configurationManager.UpdateAuctionHouseBotSettingsAsync(request, cancellationToken);
        Audit("UpdateAuctionHouseBotSettings", "server", "Module configuration updated; restart required");
        return Ok(result);
    }

    [HttpGet("settings/autobalance")]
    public ActionResult<AutoBalanceSettings> GetAutoBalanceSettings() =>
        IsLocalRequest() ? Ok(configurationManager.GetAutoBalanceSettings()) : NotFound();

    [HttpPut("settings/autobalance")]
    public async Task<ActionResult<AutoBalanceSettings>> UpdateAutoBalanceSettings(
        AutoBalanceSettings request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var result = await configurationManager.UpdateAutoBalanceSettingsAsync(request, cancellationToken);
        Audit("UpdateAutoBalanceSettings", "server", "Module configuration updated; restart required");
        return Ok(result);
    }

    [HttpGet("settings/transmog")]
    public ActionResult<TransmogSettings> GetTransmogSettings() =>
        IsLocalRequest() ? Ok(configurationManager.GetTransmogSettings()) : NotFound();

    [HttpPut("settings/transmog")]
    public async Task<ActionResult<TransmogSettings>> UpdateTransmogSettings(
        TransmogSettings request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var result = await configurationManager.UpdateTransmogSettingsAsync(request, cancellationToken);
        Audit("UpdateTransmogSettings", "server", "Module configuration updated; restart required");
        return Ok(result);
    }

    [HttpGet("settings/aoe-loot")]
    public ActionResult<AoeLootSettings> GetAoeLootSettings() =>
        IsLocalRequest() ? Ok(configurationManager.GetAoeLootSettings()) : NotFound();

    [HttpPut("settings/aoe-loot")]
    public async Task<ActionResult<AoeLootSettings>> UpdateAoeLootSettings(
        AoeLootSettings request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var result = await configurationManager.UpdateAoeLootSettingsAsync(request, cancellationToken);
        Audit("UpdateAoeLootSettings", "server", "Module configuration updated; restart required");
        return Ok(result);
    }

    [HttpPost("start")]
    public async Task<ActionResult<AdministrationResult>> Start(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        await serverManager.StartAsync(cancellationToken);
        return Ok(new AdministrationResult(true, "AzerothCore servers started."));
    }

    [HttpPost("stop")]
    public async Task<ActionResult<AdministrationResult>> Stop(
        [FromQuery] bool force, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        try
        {
            await serverManager.StopAsync(force, cancellationToken);
            return Ok(new AdministrationResult(true, "AzerothCore servers stopped."));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new AdministrationResult(false, exception.Message));
        }
    }

    [HttpPost("restart")]
    public async Task<ActionResult<AdministrationResult>> Restart(
        [FromQuery] bool force, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        try
        {
            await serverManager.StopAsync(force, cancellationToken);
            await serverManager.StartAsync(cancellationToken);
            return Ok(new AdministrationResult(true, "AzerothCore servers restarted."));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new AdministrationResult(false, exception.Message));
        }
    }

    [HttpPost("items/give")]
    public async Task<ActionResult<AdministrationResult>> GiveItem(
        GiveItemRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        if (request.ItemId == 0 || request.Quantity is < 1 or > 1000)
            return BadRequest("Item ID and a quantity from 1 to 1000 are required.");
        var output = await soapClient.ExecuteAsync(
            $"additem {player} {request.ItemId} {request.Quantity}", cancellationToken);
        Audit("GiveItem", player, $"Item={request.ItemId};Quantity={request.Quantity}");
        return Ok(new AdministrationResult(true, "Item command completed.", output));
    }

    [HttpPost("items/mail")]
    public async Task<ActionResult<AdministrationResult>> MailItem(
        MailItemRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        if (request.ItemId == 0 || request.Quantity is < 1 or > 1000)
            return BadRequest("Item ID and a quantity from 1 to 1000 are required.");
        var subject = Quote(request.Subject, 80);
        var message = Quote(request.Message, 255);
        var output = await soapClient.ExecuteAsync(
            $"send items {player} {subject} {message} {request.ItemId}:{request.Quantity}", cancellationToken);
        Audit("MailItem", player, $"Item={request.ItemId};Quantity={request.Quantity}");
        return Ok(new AdministrationResult(true, "Item mail command completed.", output));
    }

    [HttpPost("money/give")]
    public async Task<ActionResult<AdministrationResult>> GiveMoney(
        GiveMoneyRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        if (request.Gold < 0 || request.Silver is < 0 or > 99 || request.Copper is < 0 or > 99)
            return BadRequest(new AdministrationResult(false,
                "Gold cannot be negative; silver and copper must each be between 0 and 99."));
        var totalCopper = (long)request.Gold * 10_000 + request.Silver * 100L + request.Copper;
        if (totalCopper is < 1 or > uint.MaxValue)
            return BadRequest(new AdministrationResult(false, "Money must be between 1 copper and the character money limit."));
        var output = await soapClient.ExecuteAsync(
            $"send money {player} \"Server administration\" \"Money from the server administrator.\" {totalCopper}",
            cancellationToken);
        Audit("GiveMoney", player, $"Copper={totalCopper}");
        return Ok(new AdministrationResult(true, "Money was sent to the character by in-game mail.", output));
    }

    [HttpPost("players/teleport")]
    public async Task<ActionResult<AdministrationResult>> Teleport(
        TeleportPlayerRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        var location = AzerothCoreSoapClient.RequireLocation(request.Location);
        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildTeleportCommand(player, location), cancellationToken);
        Audit("Teleport", player, $"Location={location}");
        return Ok(new AdministrationResult(true, "Teleport command completed.", output));
    }

    [HttpPost("players/teleport-to-npc")]
    public async Task<ActionResult<AdministrationResult>> TeleportToNpc(
        TeleportPlayerToNpcRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        if (request.SpawnId == 0)
            return BadRequest(new AdministrationResult(false, "An NPC spawn is required."));

        await using var connection = connectionFactory.CreateConnection();
        var npc = await connection.QuerySingleOrDefaultAsync<NpcTeleportRiskRow>(new CommandDefinition("""
            SELECT template.name AS Name,
                   faction.ID IS NULL
                     OR (faction.EnemyGroup & CASE
                         WHEN characterData.race IN (1, 3, 4, 7, 11) THEN 3 ELSE 5 END) <> 0
                     OR (CASE WHEN characterData.race IN (1, 3, 4, 7, 11) THEN 12 ELSE 10 END
                         & faction.FactionGroup) <> 0 AS PotentiallyHostile
            FROM acore_world.creature spawn
            INNER JOIN acore_world.creature_template template ON template.entry = spawn.id
            LEFT JOIN acore_world.factiontemplate_dbc faction ON faction.ID = template.faction
            INNER JOIN acore_characters.characters characterData
                ON characterData.name = @PlayerName
            WHERE spawn.guid = @SpawnId AND spawn.map IN (0, 1, 530, 571, 609)
              AND template.name NOT LIKE '[UNUSED]%'
            LIMIT 1;
            """, new { request.SpawnId, PlayerName = player }, cancellationToken: cancellationToken));
        if (npc is null)
            return NotFound(new AdministrationResult(false, "That outdoor NPC spawn does not exist."));
        if (npc.PotentiallyHostile && !request.Confirmed)
            return BadRequest(new AdministrationResult(false,
                "This NPC may be hostile. Confirm the hostile NPC teleport first."));

        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildNpcTeleportCommand(
                player, request.SpawnId, npc.PotentiallyHostile && request.Confirmed), cancellationToken);
        Audit("TeleportToNpc", player,
            $"Spawn={request.SpawnId};Npc={npc.Name};PotentiallyHostile={npc.PotentiallyHostile}");
        return Ok(new AdministrationResult(true, $"Teleported to {npc.Name}.", output));
    }

    [HttpPost("players/teleport-to-player")]
    [HttpPost("players/summon-to-player")]
    public async Task<ActionResult<AdministrationResult>> PlayerRelativeTeleport(
        PlayerRelativeTeleportRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        var anchor = AzerothCoreSoapClient.RequirePlayerName(request.AnchorPlayerName);
        if (player.Equals(anchor, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new AdministrationResult(false, "The moving player and anchor player must be different."));
        var output = await soapClient.ExecuteAsync($"webadmin move {player} {anchor}", cancellationToken);
        Audit("MoveToPlayer", player, $"Anchor={anchor}");
        return Ok(new AdministrationResult(true, $"{player} was moved to {anchor}.", output));
    }

    [HttpPost("creatures/spawn")]
    public async Task<ActionResult<AdministrationResult>> SpawnCreature(
        SpawnCreatureRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the temporary creature spawn first."));
        var anchor = AzerothCoreSoapClient.RequirePlayerName(request.AnchorPlayerName);
        if (request.CreatureId == 0 || request.Level is < 1 or > 83
            || request.DespawnMinutes is < 1 or > 30 || request.Count is < 1 or > 25
            || request.SquareSideLength is < 1 or > 200)
            return BadRequest(new AdministrationResult(
                false,
                "A creature, level 1-83, despawn time 1-30 minutes, count 1-25, "
                + "and square side length 1-200 metres are required."));
        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildCreatureSpawnCommand(
                anchor, request.CreatureId, request.Level, request.DespawnMinutes,
                request.Count, request.SquareSideLength),
            cancellationToken);
        Audit("SpawnCreature", anchor,
            $"Creature={request.CreatureId};Level={request.Level};"
            + $"DespawnMinutes={request.DespawnMinutes};Count={request.Count};"
            + $"SquareSideLength={request.SquareSideLength}");
        return Ok(new AdministrationResult(
            true,
            $"{request.Count} temporary creature{(request.Count == 1 ? "" : "s")} spawned "
            + $"within a {request.SquareSideLength} by {request.SquareSideLength} metre square.",
            output));
    }

    [HttpGet("players/utility-npcs")]
    public ActionResult<IReadOnlyList<UtilityNpc>> GetUtilityNpcs() =>
        IsLocalRequest() ? Ok(UtilityNpcs) : NotFound();

    [HttpPost("players/utility-npcs/summon")]
    public async Task<ActionResult<AdministrationResult>> SummonUtilityNpc(
        SummonUtilityNpcRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the utility NPC summon first."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        var npc = UtilityNpcs.SingleOrDefault(value => value.CreatureId == request.CreatureId);
        if (npc is null)
            return BadRequest(new AdministrationResult(false, "That utility NPC is not allowlisted."));
        if (request.DespawnMinutes is < 1 or > 30)
            return BadRequest(new AdministrationResult(false, "Despawn time must be between 1 and 30 minutes."));

        var output = await soapClient.ExecuteAsync(
            $"webadmin creature spawn {player} {npc.CreatureId} {npc.Level} {request.DespawnMinutes}",
            cancellationToken);
        Audit("SummonUtilityNpc", player,
            $"Creature={npc.CreatureId};Service={npc.Service};DespawnMinutes={request.DespawnMinutes}");
        return Ok(new AdministrationResult(
            true, $"{npc.Name} summoned for up to {request.DespawnMinutes} minutes.", output));
    }

    [HttpPost("accounts/gm")]
    public async Task<ActionResult<AdministrationResult>> SetAccountGm(
        SetAccountGmRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed) return BadRequest(new AdministrationResult(false, "Confirm the GM-level change first."));
        var username = AzerothCoreSoapClient.RequireAccountName(request.Username);
        await using (var connection = connectionFactory.CreateConnection())
        {
            var currentLevel = await connection.ExecuteScalarAsync<byte?>(new CommandDefinition("""
                SELECT CASE WHEN COUNT(account.id) = 0 THEN NULL ELSE COALESCE(MAX(access.gmlevel), 0) END
                FROM acore_auth.account account
                LEFT JOIN acore_auth.account_access access ON access.id = account.id
                WHERE account.username = @Username;
                """, new { Username = username }, cancellationToken: cancellationToken));
            if (currentLevel is null)
                return NotFound(new AdministrationResult(false, "That account does not exist."));
            if (currentLevel >= 3)
                return BadRequest(new AdministrationResult(false, "Administrator-level accounts cannot be changed from this UI."));
        }
        var level = request.Enabled ? 2 : 0;
        var output = await soapClient.ExecuteAsync($"account set gmlevel {username} {level} -1", cancellationToken);
        Audit("SetAccountGm", username, $"GmLevel={level}");
        return Ok(new AdministrationResult(true, request.Enabled ? $"GM access enabled for {username}." : $"GM access removed from {username}.", output));
    }

    [HttpPost("players/speed")]
    public async Task<ActionResult<AdministrationResult>> SetPlayerSpeed(
        SetPlayerSpeedRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        if (request.Speed is < 0.5m or > 10m)
            return BadRequest(new AdministrationResult(false, "Speed must be between 0.5 and 10."));
        var speed = request.Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var output = await soapClient.ExecuteAsync($"webadmin speed {player} {speed}", cancellationToken);
        Audit("SetPlayerSpeed", player, $"Speed={speed}");
        return Ok(new AdministrationResult(true, $"{player}'s movement speed is now {speed}x.", output));
    }

    [HttpPost("characters/service")]
    public async Task<ActionResult<AdministrationResult>> ApplyCharacterService(
        CharacterServiceRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the character service first."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        CharacterServiceCommand service;
        try
        {
            service = CharacterServiceCommandBuilder.Build(player, request.Service, request.Level);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new AdministrationResult(false, exception.Message));
        }

        await using (var connection = connectionFactory.CreateConnection())
        {
            var characterOnline = await connection.QuerySingleOrDefaultAsync<byte?>(new CommandDefinition("""
                SELECT online FROM acore_characters.characters
                WHERE name = @CharacterName LIMIT 1;
                """, new { CharacterName = player }, cancellationToken: cancellationToken));
            if (characterOnline is null)
                return NotFound(new AdministrationResult(false, "That character does not exist."));
            if (service.RequiresOnlineCharacter && characterOnline == 0)
                return Conflict(new AdministrationResult(false,
                    "Reset spells requires the character to be online on this PlayerBots server revision."));
        }

        var output = await soapClient.ExecuteAsync(service.Command, cancellationToken);
        Audit("CharacterService", player, $"Service={request.Service};Level={request.Level}");
        return Ok(new AdministrationResult(true, service.Message, output));
    }

    [HttpGet("characters/service/transfer-accounts")]
    public async Task<ActionResult<IReadOnlyList<CharacterTransferAccount>>>
        GetCharacterTransferAccounts(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var identity = HttpContext.AdministrationIdentity();
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CharacterTransferAccountRow>(
            new CommandDefinition("""
                SELECT a.id AccountId, a.username Username,
                       CASE WHEN a.username LIKE CONCAT(@BotPrefix, '%')
                            THEN 'PlayerBot' ELSE 'Human' END Classification,
                       COUNT(c.guid) CharacterCount
                FROM acore_auth.account a
                LEFT JOIN acore_characters.characters c ON c.account=a.id
                WHERE UPPER(a.username)<>'AHBOT'
                  AND a.username NOT LIKE CONCAT(@BotPrefix, '%')
                  AND (@AllAccounts OR a.id IN @AllowedAccounts)
                GROUP BY a.id, a.username
                ORDER BY a.username, a.id;
                """,
                new
                {
                    BotPrefix = "rndbot",
                    AllAccounts = identity?.AccountScope == "All",
                    AllowedAccounts = identity?.GameAccountIds ?? []
                },
                cancellationToken: cancellationToken));
        return Ok(rows.Select(row => new CharacterTransferAccount(
            row.AccountId, row.Username, row.Classification,
            row.CharacterCount)).ToArray());
    }

    [HttpPost("characters/service/transfer")]
    public async Task<ActionResult<AdministrationResult>> TransferCharacterAccount(
        CharacterAccountTransferRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(
                false, "Confirm the account transfer first."));

        string player;
        try
        {
            player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new AdministrationResult(false, exception.Message));
        }

        var identity = HttpContext.AdministrationIdentity();
        CharacterTransferSourceRow? source;
        CharacterTransferAccountRow? destination;
        await using (var connection = connectionFactory.CreateConnection())
        {
            source = await connection.QuerySingleOrDefaultAsync<CharacterTransferSourceRow>(
                new CommandDefinition("""
                    SELECT c.account AccountId, a.username Username,
                           c.online<>0 Online
                    FROM acore_characters.characters c
                    JOIN acore_auth.account a ON a.id=c.account
                    WHERE c.name=@PlayerName
                    LIMIT 1;
                    """,
                    new { PlayerName = player },
                    cancellationToken: cancellationToken));
            destination =
                await connection.QuerySingleOrDefaultAsync<CharacterTransferAccountRow>(
                    new CommandDefinition("""
                        SELECT a.id AccountId, a.username Username,
                               CASE WHEN a.username LIKE CONCAT(@BotPrefix, '%')
                                    THEN 'PlayerBot' ELSE 'Human' END Classification,
                               COUNT(c.guid) CharacterCount
                        FROM acore_auth.account a
                        LEFT JOIN acore_characters.characters c ON c.account=a.id
                        WHERE a.id=@DestinationAccountId
                          AND UPPER(a.username)<>'AHBOT'
                          AND a.username NOT LIKE CONCAT(@BotPrefix, '%')
                          AND (@AllAccounts OR a.id IN @AllowedAccounts)
                        GROUP BY a.id, a.username
                        LIMIT 1;
                        """,
                        new
                        {
                            request.DestinationAccountId,
                            BotPrefix = "rndbot",
                            AllAccounts = identity?.AccountScope == "All",
                            AllowedAccounts = identity?.GameAccountIds ?? []
                        },
                        cancellationToken: cancellationToken));
        }

        if (source is null)
            return NotFound(new AdministrationResult(
                false, "That character does not exist."));
        if (destination is null)
            return NotFound(new AdministrationResult(
                false, "The destination account does not exist or is outside your scope."));
        if (source.AccountId == destination.AccountId)
            return Conflict(new AdministrationResult(
                false, $"{player} already belongs to {destination.Username}."));
        if (destination.CharacterCount >= 10)
            return Conflict(new AdministrationResult(
                false, $"{destination.Username} already has the maximum 10 characters."));

        string command;
        try
        {
            command = AzerothCoreSoapClient.BuildCharacterAccountTransferCommand(
                player, destination.Username);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new AdministrationResult(false, exception.Message));
        }

        var backup = await databaseBackupService.CreateAsync(cancellationToken);
        var output = await soapClient.ExecuteAsync(command, cancellationToken);
        Audit("TransferCharacterAccount", player,
            $"From={source.Username}({source.AccountId});"
            + $"To={destination.Username}({destination.AccountId});"
            + $"Backup={backup.BackupId}");
        var onlineNotice = source.Online
            ? " The character was online and AzerothCore disconnected it."
            : "";
        return Ok(new AdministrationResult(
            true,
            $"{player} moved from {source.Username} to {destination.Username}."
            + onlineNotice
            + $" Verified backup {backup.BackupId} was created first.",
            output));
    }

    [HttpGet("players/{playerName}/weapon-training")]
    public async Task<ActionResult<IReadOnlyList<WeaponTrainingStatus>>> GetWeaponTraining(
        string playerName, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(playerName);
        var output = await soapClient.ExecuteAsync($"webadmin weapon inspect {player}", cancellationToken);
        var statuses = new List<WeaponTrainingStatus>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 6 && fields[0] == "WEBADMIN_WEAPON"
                && int.TryParse(fields[3], out var learned) && int.TryParse(fields[4], out var current)
                && int.TryParse(fields[5], out var maximum))
                statuses.Add(new WeaponTrainingStatus(fields[1], fields[2], learned != 0, current, maximum));
        }
        if (statuses.Count == 0)
            throw new InvalidOperationException("The worldserver returned no weapon-training data. Rebuild and install the latest mod-web-admin module.");
        return Ok(statuses);
    }

    [HttpPost("players/weapon-training")]
    public async Task<ActionResult<AdministrationResult>> GrantWeaponTraining(
        GrantWeaponTrainingRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed) return BadRequest(new AdministrationResult(false, "Confirm the weapon training first."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "axes", "two-axes", "maces", "two-maces", "polearms", "swords", "two-swords", "staves",
          "bows", "guns", "daggers", "thrown", "wands", "crossbows", "fist" };
        if (!allowedKeys.Contains(request.WeaponKey))
            return BadRequest(new AdministrationResult(false, "Unknown weapon training type."));
        var output = await soapClient.ExecuteAsync($"webadmin weapon learn {player} {request.WeaponKey}", cancellationToken);
        Audit("GrantWeaponTraining", player, $"Weapon={request.WeaponKey}");
        return Ok(new AdministrationResult(true, "Weapon training granted.", output));
    }

    [HttpGet("players/{playerName}/guild-bank")]
    public async Task<ActionResult<GuildBankStatus>> GetGuildBank(
        string playerName, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var player = AzerothCoreSoapClient.RequirePlayerName(playerName);
        var output = await soapClient.ExecuteAsync($"webadmin guild inspect {player}", cancellationToken);
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith("WEBADMIN_GUILD\t", StringComparison.Ordinal));
        var fields = line?.Split('\t');
        if (fields is null || fields.Length < 7
            || !uint.TryParse(fields[1], out var guildId)
            || !int.TryParse(fields[4], out var isGuildMaster)
            || !int.TryParse(fields[5], out var tabs)
            || !uint.TryParse(fields[6], out var nextCost))
            throw new InvalidOperationException(
                "The worldserver returned no guild-bank data. Rebuild and install the latest mod-web-admin module.");
        return Ok(new GuildBankStatus(guildId, fields[2], fields[3], isGuildMaster != 0,
            tabs, 6, nextCost));
    }

    [HttpPost("players/guild-bank/unlock-tab")]
    public async Task<ActionResult<AdministrationResult>> UnlockGuildBankTab(
        UnlockGuildBankTabRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the guild-bank tab unlock first."));
        var player = AzerothCoreSoapClient.RequirePlayerName(request.PlayerName);
        var output = await soapClient.ExecuteAsync($"webadmin guild unlocktab {player}", cancellationToken);
        Audit("UnlockGuildBankTab", player, "Free admin unlock");
        return Ok(new AdministrationResult(true, "Guild bank tab unlocked without charging the character.", output));
    }

    [HttpGet("parties/{leaderName}")]
    public async Task<ActionResult<PartySnapshot>> GetParty(string leaderName, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(leaderName);
        await using var connection = connectionFactory.CreateConnection();
        if (!await IsCharacterOnlineAsync(connection, leader, cancellationToken))
            return Conflict(new AdministrationResult(false,
                "The selected party leader is offline. Log in and inspect the party again."));
        var output = await soapClient.ExecuteAsync($"webadmin group inspect {leader}", cancellationToken);
        return Ok(ParsePartySnapshot(leader, output));
    }

    [HttpPost("parties/bots/add")]
    public Task<ActionResult<AdministrationResult>> AddPartyBot(PartyBotRequest request, CancellationToken cancellationToken) =>
        ExecutePartyCommand("add", request.LeaderName, request.BotName, "Bot added to the party.", cancellationToken);

    [HttpPost("parties/bots/remove")]
    public Task<ActionResult<AdministrationResult>> RemovePartyBot(PartyBotRequest request, CancellationToken cancellationToken) =>
        ExecutePartyCommand("remove", request.LeaderName, request.BotName, "Bot removed from the party.", cancellationToken);

    [HttpPost("parties/bots/clear")]
    public Task<ActionResult<AdministrationResult>> ClearPartyBots(PartyLeaderRequest request, CancellationToken cancellationToken) =>
        ExecutePartyCommand("clear", request.LeaderName, null, "PlayerBots removed from the party.", cancellationToken);

    [HttpPost("parties/bots/fill")]
    public Task<ActionResult<AdministrationResult>> FillPartyWithBots(PartyLeaderRequest request, CancellationToken cancellationToken) =>
        ExecutePartyCommand("fill", request.LeaderName, null, "Party auto-fill completed.", cancellationToken);

    [HttpGet("questing-companions/{leaderName}")]
    public async Task<ActionResult<QuestingCompanionStatus>> GetQuestingCompanions(
        string leaderName, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(leaderName);
        await using var connection = connectionFactory.CreateConnection();
        var leaderRow = await connection.QuerySingleOrDefaultAsync<CompanionLeaderRow>(
            new CommandDefinition("""
                SELECT c.account AccountId, c.race CharacterRace,
                       c.level CharacterLevel, COALESCE(gm.guildid, 0) GuildId
                FROM acore_characters.characters c
                LEFT JOIN acore_characters.guild_member gm ON gm.guid=c.guid
                WHERE c.name=@Leader AND c.online<>0
                """, new { Leader = leader }, cancellationToken: cancellationToken));
        if (leaderRow is null)
            return Conflict(new AdministrationResult(false,
                "The selected leader must be online."));
        var identity = HttpContext.AdministrationIdentity();
        var candidates = (await connection.QueryAsync<QuestingCompanionCandidate>(
            new CommandDefinition(QuestingCompanionCandidateSql, new
                {
                    Leader = leader,
                    LeaderRace = leaderRow.CharacterRace,
                    LeaderLevel = leaderRow.CharacterLevel,
                    LeaderAccount = leaderRow.AccountId,
                    LeaderGuild = leaderRow.GuildId,
                    BotPrefix = "rndbot",
                    AllAccounts = identity?.AccountScope == "All",
                    AllowedAccounts = identity?.GameAccountIds ?? []
                }, cancellationToken: cancellationToken))).AsList();
        if (candidates.Count > 0)
        {
            await using var maintenance = connectionFactory.CreateMaintenanceConnection();
            var linkedAccounts = (await maintenance.QueryAsync<uint>(new CommandDefinition("""
                SELECT linked_account_id
                FROM acore_playerbots.playerbots_account_links
                WHERE account_id=@LeaderAccount
                  AND linked_account_id IN @CandidateAccounts;
                """, new
                {
                    LeaderAccount = leaderRow.AccountId,
                    CandidateAccounts = candidates.Select(candidate => candidate.AccountId)
                        .Distinct().ToArray()
                }, cancellationToken: cancellationToken))).ToHashSet();
            foreach (var candidate in candidates)
                candidate.AccountsLinked = linkedAccounts.Contains(candidate.AccountId);
        }
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion inspect {leader}", cancellationToken);
        var inspection = ParseQuestingCompanionInspection(output, leader);
        return Ok(new QuestingCompanionStatus(
            leader, inspection.ActiveCompanions, candidates,
            inspection.LeaderQuests, inspection.ProtocolVersion));
    }

    [HttpPost("questing-companions/start")]
    public async Task<ActionResult<AdministrationResult>> StartQuestingCompanion(
        QuestingCompanionRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, true, cancellationToken);
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion start {leader} {companion}", cancellationToken);
        Audit("StartQuestingCompanion", companion, $"Leader={leader}");
        return Ok(new AdministrationResult(
            true, $"{companion} is logging in and joining {leader}.", output));
    }

    [HttpPost("questing-companions/dismiss")]
    public async Task<ActionResult<AdministrationResult>> DismissQuestingCompanion(
        QuestingCompanionRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, false, cancellationToken);
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion dismiss {leader} {companion}", cancellationToken);
        Audit("DismissQuestingCompanion", companion, $"Leader={leader}");
        return Ok(new AdministrationResult(
            true, $"{companion} is logging out.", output));
    }

    [HttpPost("questing-companions/reset")]
    public async Task<ActionResult<AdministrationResult>> ResetQuestingCompanion(
        QuestingCompanionResetRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, false, cancellationToken);
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion reset {leader} {companion}", cancellationToken);
        Audit("ResetQuestingCompanion", companion, $"Leader={leader}");
        return Ok(new AdministrationResult(
            true, $"{companion}'s follow, combat and loot behaviour was reset.", output));
    }

    [HttpPost("questing-companions/behavior")]
    public async Task<ActionResult<AdministrationResult>> SetQuestingCompanionBehavior(
        QuestingCompanionBehaviorRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var preset = request.Preset.Trim().ToLowerInvariant();
        var role = request.Role.Trim().ToLowerInvariant();
        var movement = request.Movement.Trim().ToLowerInvariant();
        var focus = request.CombatFocus.Trim().ToLowerInvariant();
        if (preset is not ("custom" or "questing" or "dungeon-tank" or "dungeon-healer")
            || role is not ("auto" or "tank" or "healer" or "damage")
            || movement is not ("follow" or "stay")
            || focus is not ("assist" or "defend")
            || request.FollowDistance is < 1 or > 20)
        {
            return BadRequest(new AdministrationResult(
                false, "The companion behaviour settings are invalid."));
        }

        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, false, cancellationToken);
        var distance = request.FollowDistance.ToString(
            "0.0", System.Globalization.CultureInfo.InvariantCulture);
        var command =
            $"webadmin companion behavior {leader} {companion} {preset} {role} "
            + $"{movement} {focus} {distance} "
            + $"{(request.LootEnabled ? 1 : 0)} {(request.GatherEnabled ? 1 : 0)} "
            + $"{(request.AutoSellTrash ? 1 : 0)} {(request.AutoRepair ? 1 : 0)}";
        var output = await soapClient.ExecuteAsync(command, cancellationToken);
        Audit("SetQuestingCompanionBehavior", companion,
            $"Leader={leader};Preset={preset};Role={role};Movement={movement};"
                + $"Focus={focus};Distance={request.FollowDistance:0.0};"
                + $"Loot={request.LootEnabled};Gather={request.GatherEnabled};"
                + $"Sell={request.AutoSellTrash};Repair={request.AutoRepair}");
        return Ok(new AdministrationResult(
            true, $"{companion}'s behaviour was updated.", output));
    }

    [HttpPost("questing-companions/preset")]
    public async Task<ActionResult<AdministrationResult>> SetQuestingCompanionPreset(
        QuestingCompanionPresetRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var preset = request.Preset.Trim().ToLowerInvariant();
        if (preset is not ("questing" or "dungeon-tank" or "dungeon-healer"))
            return BadRequest(new AdministrationResult(false,
                "Select a supported companion preset."));
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, false, cancellationToken);
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion preset {leader} {companion} {preset}",
            cancellationToken);
        Audit("SetQuestingCompanionPreset", companion,
            $"Leader={leader};Preset={preset}");
        return Ok(new AdministrationResult(
            true, $"Applied the {preset.Replace('-', ' ')} preset to {companion}.",
            output));
    }

    [HttpPost("questing-companions/regroup")]
    public async Task<ActionResult<AdministrationResult>> RegroupQuestingCompanion(
        QuestingCompanionResetRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, false, cancellationToken);
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion regroup {leader} {companion}", cancellationToken);
        Audit("RegroupQuestingCompanion", companion, $"Leader={leader}");
        return Ok(new AdministrationResult(
            true, $"{companion} was reset to follow and regroup.", output));
    }

    [HttpPost("questing-companions/equipment-protection")]
    public async Task<ActionResult<AdministrationResult>>
        SetQuestingCompanionEquipmentProtection(
            QuestingCompanionEquipmentProtectionRequest request,
            CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (request.Slot is < 0 or >= 19)
            return BadRequest(new AdministrationResult(
                false, "The equipment slot is invalid."));

        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await ValidateCompanionPairAsync(leader, companion, false, cancellationToken);
        var output = await soapClient.ExecuteAsync(
            $"webadmin companion protect {leader} {companion} {request.Slot} "
            + (request.Protected ? "on" : "off"), cancellationToken);
        Audit(request.Protected
                ? "ProtectQuestingCompanionEquipment"
                : "UnprotectQuestingCompanionEquipment",
            companion, $"Leader={leader};Slot={request.Slot}");
        return Ok(new AdministrationResult(
            true,
            request.Protected
                ? "The equipped item is protected for this companion session."
                : "Equipment protection was removed from that slot.",
            output));
    }

    [HttpPost("questing-companions/account-link")]
    public async Task<ActionResult<AdministrationResult>> SetQuestingCompanionAccountLink(
        QuestingCompanionAccountLinkRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(
                false, "Confirm changing the trusted PlayerBots account link."));
        var identity = HttpContext.AdministrationIdentity();
        if (identity is null || identity.Role is not ("Owner" or "Administrator"))
            return Forbid();
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        var companion = AzerothCoreSoapClient.RequirePlayerName(request.CompanionName);
        await using var connection = connectionFactory.CreateConnection();
        var accounts = (await connection.QueryAsync<CompanionAccountRow>(
            new CommandDefinition("""
                SELECT c.name Name, c.account AccountId
                FROM acore_characters.characters c
                WHERE c.name IN @Names
                  AND (@AllAccounts OR c.account IN @AllowedAccounts);
                """, new
                {
                    Names = new[] { leader, companion },
                    AllAccounts = identity.AccountScope == "All",
                    AllowedAccounts = identity.GameAccountIds
                }, cancellationToken: cancellationToken))).AsList();
        var leaderAccount = accounts.FirstOrDefault(value =>
            value.Name.Equals(leader, StringComparison.OrdinalIgnoreCase))?.AccountId;
        var companionAccount = accounts.FirstOrDefault(value =>
            value.Name.Equals(companion, StringComparison.OrdinalIgnoreCase))?.AccountId;
        if (leaderAccount is null || companionAccount is null)
            return NotFound(new AdministrationResult(
                false, "Both characters must be within your permitted game-account scope."));
        if (leaderAccount == companionAccount)
            return BadRequest(new AdministrationResult(
                false, "Characters on the same account do not require a trusted link."));

        var backup = await databaseBackupService.CreateAsync(cancellationToken);
        await using var maintenance = connectionFactory.CreateMaintenanceConnection();
        await maintenance.OpenAsync(cancellationToken);
        await using var transaction = await maintenance.BeginTransactionAsync(cancellationToken);
        if (request.Linked)
        {
            await maintenance.ExecuteAsync(new CommandDefinition("""
                INSERT IGNORE INTO acore_playerbots.playerbots_account_links
                    (account_id, linked_account_id)
                VALUES (@LeaderAccount, @CompanionAccount),
                       (@CompanionAccount, @LeaderAccount);
                """, new { LeaderAccount = leaderAccount, CompanionAccount = companionAccount },
                transaction, cancellationToken: cancellationToken));
        }
        else
        {
            await maintenance.ExecuteAsync(new CommandDefinition("""
                DELETE FROM acore_playerbots.playerbots_account_links
                WHERE (account_id=@LeaderAccount AND linked_account_id=@CompanionAccount)
                   OR (account_id=@CompanionAccount AND linked_account_id=@LeaderAccount);
                """, new { LeaderAccount = leaderAccount, CompanionAccount = companionAccount },
                transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
        Audit(request.Linked ? "LinkPlayerBotsAccounts" : "UnlinkPlayerBotsAccounts",
            $"{leader}/{companion}",
            $"Accounts={leaderAccount}/{companionAccount};Backup={backup.BackupId}");
        return Ok(new AdministrationResult(true,
            request.Linked
                ? $"The accounts for {leader} and {companion} are now trusted. Backup {backup.BackupId} was created first."
                : $"The trusted account link was removed. Backup {backup.BackupId} was created first."));
    }

    [HttpGet("dungeons")]
    public async Task<ActionResult<IReadOnlyList<DungeonDestination>>> GetDungeons(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var output = await soapClient.ExecuteAsync("webadmin dungeon list", cancellationToken);
        var dungeons = ParseDungeons(output);
        if (dungeons.Count == 0)
            throw new InvalidOperationException("The worldserver returned no dungeon destinations. Rebuild and install the latest mod-web-admin module.");
        return Ok(dungeons.OrderBy(dungeon => dungeon.MinimumLevel).ThenBy(dungeon => dungeon.Name));
    }

    [HttpGet("dungeon-library/characters")]
    public async Task<ActionResult<IReadOnlyList<DungeonLibraryCharacter>>>
        GetDungeonLibraryCharacters(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        await using var connection = connectionFactory.CreateConnection();
        var identity = HttpContext.AdministrationIdentity();
        var rows = await connection.QueryAsync<DungeonLibraryCharacterRow>(
            new CommandDefinition("""
                SELECT characterData.guid Guid, characterData.name Name,
                       account.username Username, characterData.level Level,
                       characterData.class CharacterClass,
                       characterData.online<>0 Online
                FROM acore_characters.characters characterData
                JOIN acore_auth.account account ON account.id=characterData.account
                WHERE characterData.name<>''
                  AND account.username NOT LIKE CONCAT(@BotPrefix, '%')
                  AND UPPER(account.username)<>'AHBOT'
                  AND (@AllAccounts OR characterData.account IN @AllowedAccounts)
                ORDER BY characterData.online DESC, characterData.level, characterData.name
                """, new
                {
                    BotPrefix = "rndbot",
                    AllAccounts = identity?.AccountScope == "All",
                    AllowedAccounts = identity?.GameAccountIds ?? []
                }, cancellationToken: cancellationToken));
        return Ok(rows.Select(row => new DungeonLibraryCharacter(
            row.Guid, row.Name, row.Username, row.Level,
            row.CharacterClass, row.Online)).ToArray());
    }

    [HttpPost("dungeon-library/guide")]
    public async Task<ActionResult<DungeonGuide>> GetDungeonLibraryGuide(
        DungeonLibraryGuideRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var characterGuids = request.CharacterGuids.Distinct().Take(5).ToArray();
        if (request.CharacterGuids.Distinct().Count() > 5)
            return BadRequest(new AdministrationResult(
                false, "Select no more than five characters."));
        var dungeonOutput = await soapClient.ExecuteAsync(
            "webadmin dungeon list", cancellationToken);
        var dungeon = ParseDungeons(dungeonOutput)
            .FirstOrDefault(value => value.DungeonId == request.DungeonId);
        if (dungeon is null) return NotFound();
        await using var connection = connectionFactory.CreateConnection();
        var identity = HttpContext.AdministrationIdentity();
        IReadOnlyList<DungeonPartyCharacterRow> characters = characterGuids.Length == 0
            ? []
            : (await connection.QueryAsync<DungeonPartyCharacterRow>(
                new CommandDefinition("""
                    SELECT guid Guid, name Name, class CharacterClass,
                           race Race, level CharacterLevel
                    FROM acore_characters.characters
                    WHERE guid IN @Guids
                      AND (@AllAccounts OR account IN @AllowedAccounts)
                    """, new
                    {
                        Guids = characterGuids,
                        AllAccounts = identity?.AccountScope == "All",
                        AllowedAccounts = identity?.GameAccountIds ?? []
                    },
                    cancellationToken: cancellationToken))).AsList();
        return Ok(await dungeonGuideService.GetAsync(
            dungeon,
            characters.Select(character => new DungeonGuideService.Character(
                character.Guid, character.Name, character.CharacterClass,
                character.Race, character.CharacterLevel)).ToArray(),
            cancellationToken));
    }

    [HttpPost("dungeon-library/wishlist-plan")]
    public async Task<ActionResult<DungeonWishlistPlan>> GetDungeonWishlistPlan(
        DungeonWishlistPlanRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var itemIds = request.ItemIds.Distinct().Take(100).ToArray();
        var characterGuids = request.CharacterGuids.Distinct().Take(5).ToArray();
        if (request.ItemIds.Distinct().Count() > 100)
            return BadRequest(new AdministrationResult(false, "Wishlist is limited to 100 items."));
        if (request.CharacterGuids.Distinct().Count() > 5)
            return BadRequest(new AdministrationResult(false, "Select no more than five characters."));
        if (itemIds.Length == 0) return Ok(new DungeonWishlistPlan([], []));

        await using var connection = connectionFactory.CreateConnection();
        var identity = HttpContext.AdministrationIdentity();
        var targets = characterGuids.Length == 0
            ? []
            : (await connection.QueryAsync<ItemTargetRow>(new CommandDefinition("""
                SELECT guid Guid, name Name, class CharacterClass, race Race,
                       level CharacterLevel
                FROM acore_characters.characters
                WHERE guid IN @Guids
                  AND (@AllAccounts OR account IN @AllowedAccounts);
                """, new
                {
                    Guids = characterGuids,
                    AllAccounts = identity?.AccountScope == "All",
                    AllowedAccounts = identity?.GameAccountIds ?? []
                }, cancellationToken: cancellationToken))).AsList();
        var items = (await connection.QueryAsync<WishlistItemRow>(new CommandDefinition("""
            SELECT entry ItemId, name Name, Quality, ItemLevel, RequiredLevel,
                   class ItemClass, subclass ItemSubclass, InventoryType,
                   CAST(AllowableClass AS SIGNED) AllowableClass,
                   CAST(AllowableRace AS SIGNED) AllowableRace
            FROM acore_world.item_template WHERE entry IN @ItemIds;
            """, new { ItemIds = itemIds }, cancellationToken: cancellationToken))).AsList();
        var sources = (await connection.QueryAsync<WishlistSourceRow>(new CommandDefinition("""
            SELECT loot.Item ItemId, boss.entry BossCreatureId, boss.name BossName,
                   COALESCE(MIN(spawn.map), 0) MapId, ABS(loot.Chance) DropChance
            FROM acore_world.creature_template boss
            JOIN acore_world.creature_loot_template loot ON loot.Entry=boss.lootid
            LEFT JOIN acore_world.creature spawn ON spawn.id=boss.entry
            WHERE loot.Item IN @ItemIds AND loot.Item<>0
            GROUP BY loot.Item, boss.entry, boss.name, loot.Chance
            UNION ALL
            SELECT referenceLoot.Item, boss.entry, boss.name,
                   COALESCE(MIN(spawn.map), 0), ABS(referenceLoot.Chance)
            FROM acore_world.creature_template boss
            JOIN acore_world.creature_loot_template parentLoot
              ON parentLoot.Entry=boss.lootid AND parentLoot.Reference<>0
            JOIN acore_world.reference_loot_template referenceLoot
              ON referenceLoot.Entry=parentLoot.Reference
            LEFT JOIN acore_world.creature spawn ON spawn.id=boss.entry
            WHERE referenceLoot.Item IN @ItemIds AND referenceLoot.Item<>0
            GROUP BY referenceLoot.Item, boss.entry, boss.name, referenceLoot.Chance;
            """, new { ItemIds = itemIds }, cancellationToken: cancellationToken))).AsList();
        var ownership = targets.Count == 0
            ? []
            : (await connection.QueryAsync<WishlistOwnershipRow>(new CommandDefinition("""
                SELECT inventory.guid CharacterGuid, instance.itemEntry ItemId,
                       MAX(inventory.bag=0 AND inventory.slot<19) Equipped
                FROM acore_characters.character_inventory inventory
                JOIN acore_characters.item_instance instance ON instance.guid=inventory.item
                WHERE inventory.guid IN @Guids AND instance.itemEntry IN @ItemIds
                GROUP BY inventory.guid, instance.itemEntry;
                """, new
                {
                    Guids = targets.Select(target => target.Guid).ToArray(),
                    ItemIds = itemIds
                }, cancellationToken: cancellationToken))).AsList();

        var dungeonOutput = await soapClient.ExecuteAsync("webadmin dungeon list", cancellationToken);
        var dungeons = ParseDungeons(dungeonOutput);
        var planItems = items.Select(item =>
        {
            var compatibilityItem = new AdministrationItem
            {
                ItemId = item.ItemId, Name = item.Name, ItemClass = item.ItemClass,
                ItemSubclass = item.ItemSubclass, Quality = item.Quality,
                ItemLevel = item.ItemLevel, RequiredLevel = item.RequiredLevel,
                InventoryType = item.InventoryType, AllowableClass = item.AllowableClass,
                AllowableRace = item.AllowableRace
            };
            var itemSources = sources.Where(source => source.ItemId == item.ItemId)
                .GroupBy(source => (source.BossCreatureId, source.MapId))
                .Select(group => group.OrderByDescending(source => source.DropChance).First())
                .Select(source => new DungeonWishlistSource(
                    source.BossCreatureId, source.BossName, source.MapId,
                    source.DropChance, source.DropChance > 0 ? 100d / source.DropChance : null))
                .OrderByDescending(source => source.DropChance).ToArray();
            var characterStates = targets.Select(target =>
            {
                var owned = ownership.FirstOrDefault(value =>
                    value.CharacterGuid == target.Guid && value.ItemId == item.ItemId);
                return new DungeonWishlistCharacter(
                    (uint)target.Guid, target.Name, ItemCompatible(compatibilityItem, target),
                    owned is not null, owned?.Equipped == true);
            }).ToArray();
            return new DungeonWishlistPlanItem(
                item.ItemId, item.Name, item.Quality, item.ItemLevel,
                itemSources, characterStates);
        }).OrderBy(item => item.Name).ToArray();
        var runs = planItems.SelectMany(item => item.Sources.Select(source => (item, source)))
            .Where(value => value.source.MapId != 0)
            .GroupBy(value => value.source.MapId)
            .Select(group => new DungeonWishlistRun(
                group.Key,
                dungeons.Where(dungeon => dungeon.MapId == group.Key)
                    .Select(dungeon => dungeon.Name).Distinct().ToArray(),
                group.Select(value => value.item.ItemId).Distinct().Count(),
                group.Select(value => value.item.Name).Distinct().OrderBy(name => name).ToArray()))
            .OrderByDescending(run => run.WantedItemCount)
            .ThenBy(run => run.DungeonNames.FirstOrDefault() ?? "").ToArray();
        return Ok(new DungeonWishlistPlan(planItems, runs));
    }

    [HttpGet("parties/{leaderName}/dungeons/{dungeonId}/readiness")]
    public async Task<ActionResult<DungeonReadiness>> GetDungeonReadiness(
        string leaderName, uint dungeonId, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(leaderName);
        await using var connection = connectionFactory.CreateConnection();
        if (!await IsCharacterOnlineAsync(connection, leader, cancellationToken))
            return Conflict(new AdministrationResult(false,
                "The selected party leader is offline. Log in and inspect the party again."));
        var partyOutput = await soapClient.ExecuteAsync($"webadmin group inspect {leader}", cancellationToken);
        var party = ParsePartySnapshot(leader, partyOutput);
        var dungeonOutput = await soapClient.ExecuteAsync("webadmin dungeon list", cancellationToken);
        var dungeon = ParseDungeons(dungeonOutput).FirstOrDefault(item => item.DungeonId == dungeonId);
        if (dungeon is null) return NotFound("The selected dungeon is no longer available.");

        var playerNames = party.Members.Where(member => !member.IsPlayerBot)
            .Select(member => member.Name).ToArray();
        var questPlayers = playerNames.Length == 0
            ? []
            : (await connection.QueryAsync<DungeonQuestPlayerRow>(new CommandDefinition("""
                SELECT guid AS CharacterGuid, name AS PlayerName, race AS CharacterRace,
                       class AS CharacterClass, level AS CharacterLevel, online AS Online
                FROM acore_characters.characters
                WHERE name IN @PlayerNames;
                """, new { PlayerNames = playerNames }, cancellationToken: cancellationToken))).AsList();
        const string lockoutSql = """
            SELECT characterData.name AS PlayerName, instanceData.map AS MapId,
                   instanceData.difficulty AS Difficulty,
                   FROM_UNIXTIME(instanceData.resettime) AS ResetAtUtc
            FROM acore_characters.characters characterData
            INNER JOIN acore_characters.character_instance characterInstance
                ON characterInstance.guid = characterData.guid
            INNER JOIN acore_characters.instance instanceData
                ON instanceData.id = characterInstance.instance
            WHERE characterData.name IN @PlayerNames
              AND instanceData.map = @MapId
              AND instanceData.resettime > UNIX_TIMESTAMP()
            ORDER BY characterData.name, instanceData.difficulty;
            """;
        IReadOnlyList<DungeonLockout> lockouts = playerNames.Length == 0
            ? []
            : (await connection.QueryAsync<DungeonLockoutRow>(new CommandDefinition(
                lockoutSql, new { PlayerNames = playerNames, dungeon.MapId },
                cancellationToken: cancellationToken)))
                .Select(row => new DungeonLockout(
                    row.PlayerName, row.MapId, row.Difficulty, row.ResetAtUtc))
                .ToArray();

        const string questSql = """
            SELECT DISTINCT quest.ID AS QuestId, quest.LogTitle AS Title, quest.MinLevel AS MinimumLevel,
                   quest.AllowableRaces, COALESCE(addon.AllowableClasses, 0) AS AllowableClasses,
                   COALESCE(addon.PrevQuestID, 0) AS PreviousQuestId,
                   COALESCE(previousQuest.LogTitle, '') AS PreviousQuestTitle,
                   quest.RequiredFactionId1, quest.RequiredFactionValue1,
                   quest.RequiredFactionId2, quest.RequiredFactionValue2,
                   COALESCE(addon.RequiredMinRepFaction, 0) AS RequiredMinRepFaction,
                   COALESCE(addon.RequiredMinRepValue, 0) AS RequiredMinRepValue,
                   COALESCE(addon.RequiredMaxRepFaction, 0) AS RequiredMaxRepFaction,
                   COALESCE(addon.RequiredMaxRepValue, 0) AS RequiredMaxRepValue,
                   quest.StartItem
            FROM acore_world.quest_template quest
            LEFT JOIN acore_world.quest_template_addon addon ON addon.ID = quest.ID
            LEFT JOIN acore_world.quest_template previousQuest
                ON previousQuest.ID = ABS(COALESCE(addon.PrevQuestID, 0))
            INNER JOIN acore_world.creature spawn
                ON spawn.id IN (quest.RequiredNpcOrGo1, quest.RequiredNpcOrGo2,
                                quest.RequiredNpcOrGo3, quest.RequiredNpcOrGo4)
            WHERE spawn.map = @MapId
              AND quest.LogTitle <> ''
            ORDER BY quest.MinLevel, quest.ID
            LIMIT 30;
            """;
        var questRows = (await connection.QueryAsync<DungeonQuestRow>(new CommandDefinition(
            questSql, new { dungeon.MapId }, cancellationToken: cancellationToken))).AsList();
        var quests = new List<DungeonQuest>();
        foreach (var quest in questRows)
        {
            var partyRace = questPlayers.FirstOrDefault()?.CharacterRace ?? (byte)0;
            var giver = await connection.QueryFirstOrDefaultAsync<DungeonQuestGiverRow>(new CommandDefinition("""
                SELECT spawn.guid AS SpawnId, template.entry AS CreatureId, template.name AS Name,
                       spawn.map AS MapId, spawn.zoneId AS ZoneId
                FROM acore_world.creature_queststarter starter
                INNER JOIN acore_world.creature_template template ON template.entry = starter.id
                INNER JOIN acore_world.creature spawn ON spawn.id = template.entry
                WHERE starter.quest = @QuestId
                  AND template.faction NOT IN @HostileFactions
                ORDER BY spawn.map, spawn.guid
                LIMIT 1;
                """, new
                {
                    quest.QuestId,
                    HostileFactions = GetHostileTrainerFactions(partyRace)
                }, cancellationToken: cancellationToken));
            var prerequisiteQuestId = (uint)Math.Abs(quest.PreviousQuestId);
            var prerequisiteGiver = prerequisiteQuestId == 0
                ? null
                : await connection.QueryFirstOrDefaultAsync<DungeonQuestGiverRow>(new CommandDefinition("""
                    SELECT spawn.guid AS SpawnId, template.entry AS CreatureId, template.name AS Name,
                           spawn.map AS MapId, spawn.zoneId AS ZoneId
                    FROM acore_world.creature_queststarter starter
                    INNER JOIN acore_world.creature_template template ON template.entry = starter.id
                    INNER JOIN acore_world.creature spawn ON spawn.id = template.entry
                    WHERE starter.quest = @QuestId
                      AND template.faction NOT IN @HostileFactions
                    ORDER BY spawn.map, spawn.guid
                    LIMIT 1;
                    """, new
                    {
                        QuestId = prerequisiteQuestId,
                        HostileFactions = GetHostileTrainerFactions(partyRace)
                    }, cancellationToken: cancellationToken));

            const string statusSql = """
                SELECT characterData.name AS PlayerName,
                       CASE WHEN rewarded.quest IS NOT NULL THEN 2
                            WHEN progress.quest IS NOT NULL THEN 1 ELSE 0 END AS QuestState,
                       prerequisite.quest IS NOT NULL AS PrerequisiteCompleted,
                       COALESCE(reputation1.standing, 0) AS Reputation1,
                       COALESCE(reputation2.standing, 0) AS Reputation2,
                       COALESCE(reputationMinimum.standing, 0) AS ReputationMinimum,
                       COALESCE(reputationMaximum.standing, 0) AS ReputationMaximum
                FROM acore_characters.characters characterData
                LEFT JOIN acore_characters.character_queststatus progress
                    ON progress.guid = characterData.guid AND progress.quest = @QuestId
                LEFT JOIN acore_characters.character_queststatus_rewarded rewarded
                    ON rewarded.guid = characterData.guid AND rewarded.quest = @QuestId
                LEFT JOIN acore_characters.character_queststatus_rewarded prerequisite
                    ON prerequisite.guid = characterData.guid AND prerequisite.quest = @PreviousQuestId
                LEFT JOIN acore_characters.character_reputation reputation1
                    ON reputation1.guid = characterData.guid AND reputation1.faction = @RequiredFactionId1
                LEFT JOIN acore_characters.character_reputation reputation2
                    ON reputation2.guid = characterData.guid AND reputation2.faction = @RequiredFactionId2
                LEFT JOIN acore_characters.character_reputation reputationMinimum
                    ON reputationMinimum.guid = characterData.guid AND reputationMinimum.faction = @RequiredMinRepFaction
                LEFT JOIN acore_characters.character_reputation reputationMaximum
                    ON reputationMaximum.guid = characterData.guid AND reputationMaximum.faction = @RequiredMaxRepFaction
                WHERE characterData.name IN @PlayerNames;
                """;
            var states = playerNames.Length == 0
                ? []
                : (await connection.QueryAsync<DungeonQuestState>(new CommandDefinition(
                    statusSql, new
                    {
                        quest.QuestId,
                        PreviousQuestId = prerequisiteQuestId,
                        quest.RequiredFactionId1,
                        quest.RequiredFactionId2,
                        quest.RequiredMinRepFaction,
                        quest.RequiredMaxRepFaction,
                        PlayerNames = playerNames
                    },
                    cancellationToken: cancellationToken))).AsList();
            var playerStatuses = questPlayers.Select(player =>
            {
                var state = states.FirstOrDefault(item =>
                    item.PlayerName.Equals(player.PlayerName, StringComparison.OrdinalIgnoreCase));
                return EvaluateDungeonQuestStatus(player, quest, state, giver is not null);
            }).ToArray();
            quests.Add(new DungeonQuest(quest.QuestId, quest.Title, quest.MinimumLevel,
                states.Where(state => state.QuestState == 1).Select(state => state.PlayerName).ToArray(),
                states.Where(state => state.QuestState == 2).Select(state => state.PlayerName).ToArray(),
                giver is null ? null : new DungeonQuestGiver(
                    giver.SpawnId, giver.CreatureId, giver.Name, giver.MapId, giver.ZoneId),
                playerStatuses,
                prerequisiteQuestId == 0 ? null : new DungeonQuestPrerequisite(
                    prerequisiteQuestId,
                    string.IsNullOrWhiteSpace(quest.PreviousQuestTitle)
                        ? $"Quest {prerequisiteQuestId}" : quest.PreviousQuestTitle,
                    prerequisiteGiver is null ? null : new DungeonQuestGiver(
                        prerequisiteGiver.SpawnId, prerequisiteGiver.CreatureId,
                        prerequisiteGiver.Name, prerequisiteGiver.MapId, prerequisiteGiver.ZoneId))));
        }

        return Ok(DungeonReadinessEvaluator.Evaluate(party, dungeon, lockouts, quests));
    }

    [HttpGet("parties/{leaderName}/dungeons/{dungeonId}/guide")]
    public async Task<ActionResult<DungeonGuide>> GetDungeonGuide(
        string leaderName, uint dungeonId, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(leaderName);
        var dungeonOutput = await soapClient.ExecuteAsync(
            "webadmin dungeon list", cancellationToken);
        var dungeon = ParseDungeons(dungeonOutput)
            .FirstOrDefault(value => value.DungeonId == dungeonId);
        if (dungeon is null) return NotFound();
        var partyOutput = await soapClient.ExecuteAsync(
            $"webadmin group inspect {leader}", cancellationToken);
        var party = ParsePartySnapshot(leader, partyOutput);
        await using var connection = connectionFactory.CreateConnection();
        var partyCharacters = (await connection.QueryAsync<DungeonPartyCharacterRow>(
            new CommandDefinition("""
                SELECT name Name, class CharacterClass, level CharacterLevel
                FROM acore_characters.characters WHERE name IN @Names
                """, new { Names = party.Members.Select(member => member.Name).ToArray() },
                cancellationToken: cancellationToken))).AsList();
        var endEntry = await connection.QuerySingleOrDefaultAsync<uint?>(
            new CommandDefinition("""
                SELECT MAX(entry) FROM acore_world.instance_encounters
                WHERE lastEncounterDungeon=@DungeonId
                """, new { DungeonId = dungeonId },
                cancellationToken: cancellationToken));
        IReadOnlyList<DungeonEncounterRow> encounters;
        if (endEntry is not null)
        {
            encounters = (await connection.QueryAsync<DungeonEncounterRow>(
                new CommandDefinition("""
                    SELECT entry EncounterEntry, creditEntry CreatureId, comment Name
                    FROM acore_world.instance_encounters
                    WHERE creditType=0 AND entry BETWEEN
                      (SELECT COALESCE(MAX(entry), 0) + 1
                       FROM acore_world.instance_encounters
                       WHERE entry < @EndEntry AND lastEncounterDungeon<>0)
                      AND @EndEntry
                    ORDER BY entry
                    """, new { EndEntry = endEntry.Value },
                    cancellationToken: cancellationToken))).AsList();
        }
        else
        {
            encounters = (await connection.QueryAsync<DungeonEncounterRow>(
                new CommandDefinition("""
                    SELECT 0 EncounterEntry, template.entry CreatureId,
                           template.name Name
                    FROM acore_world.creature creature
                    JOIN acore_world.creature_template template
                      ON template.entry=creature.id
                    WHERE creature.map=@MapId AND template.rank=3
                      AND template.lootid<>0
                    GROUP BY template.entry, template.name, template.minlevel
                    ORDER BY template.minlevel, template.name
                    """, new { dungeon.MapId },
                    cancellationToken: cancellationToken))).AsList();
        }
        var bossIds = encounters.Select(encounter => encounter.CreatureId)
            .Distinct().ToArray();
        IReadOnlyList<DungeonLootRow> lootRows = bossIds.Length == 0
            ? []
            : (await connection.QueryAsync<DungeonLootRow>(
                new CommandDefinition("""
                    SELECT template.entry BossCreatureId, item.entry ItemId,
                           item.name Name, item.Quality Quality,
                           item.ItemLevel ItemLevel, item.RequiredLevel RequiredLevel,
                           item.class ItemClass, item.subclass ItemSubclass,
                           item.InventoryType InventoryType,
                           CAST(item.AllowableClass AS SIGNED) AllowableClass,
                           ABS(loot.Chance) DropChance,
                           loot.QuestRequired<>0 QuestRequired
                    FROM acore_world.creature_template template
                    JOIN acore_world.creature_loot_template loot
                      ON loot.Entry=template.lootid
                    JOIN acore_world.item_template item ON item.entry=loot.Item
                    WHERE template.entry IN @BossIds AND loot.Item<>0
                      AND (item.Quality>=2 OR loot.QuestRequired<>0)
                    UNION ALL
                    SELECT template.entry, item.entry, item.name, item.Quality,
                           item.ItemLevel, item.RequiredLevel, item.class,
                           item.subclass, item.InventoryType,
                           CAST(item.AllowableClass AS SIGNED),
                           ABS(referenceLoot.Chance),
                           referenceLoot.QuestRequired<>0
                    FROM acore_world.creature_template template
                    JOIN acore_world.creature_loot_template parentLoot
                      ON parentLoot.Entry=template.lootid
                    JOIN acore_world.reference_loot_template referenceLoot
                      ON referenceLoot.Entry=parentLoot.Reference
                    JOIN acore_world.item_template item
                      ON item.entry=referenceLoot.Item
                    WHERE template.entry IN @BossIds
                      AND parentLoot.Reference<>0 AND referenceLoot.Item<>0
                      AND (item.Quality>=2 OR referenceLoot.QuestRequired<>0)
                    """, new { BossIds = bossIds },
                    cancellationToken: cancellationToken))).AsList();
        var catalog = DungeonGuideCatalog.Find(dungeon.Name);
        var bosses = encounters.Select((encounter, index) =>
        {
            var loot = lootRows
                .Where(row => row.BossCreatureId == encounter.CreatureId)
                .GroupBy(row => row.ItemId)
                .Select(group => group.OrderByDescending(row => row.DropChance).First())
                .Select(row => new DungeonLootItem
                {
                    ItemId = row.ItemId,
                    Name = row.Name,
                    Quality = row.Quality,
                    ItemLevel = row.ItemLevel,
                    RequiredLevel = row.RequiredLevel,
                    ItemClass = row.ItemClass,
                    ItemSubclass = row.ItemSubclass,
                    InventoryType = row.InventoryType,
                    AllowableClass = row.AllowableClass,
                    DropChance = row.DropChance,
                    QuestRequired = row.QuestRequired,
                    SuggestedForParty = IsLootSuggested(row, partyCharacters)
                })
                .OrderByDescending(item => item.SuggestedForParty)
                .ThenByDescending(item => item.Quality)
                .ThenBy(item => item.Name)
                .ToArray();
            return new DungeonBossGuide(
                index + 1, encounter.CreatureId, encounter.Name,
                DungeonGuideCatalog.Tactics(catalog, encounter.Name), loot);
        }).ToArray();
        return Ok(new DungeonGuide(
            dungeon.DungeonId, dungeon.Name, catalog.Overview, catalog.Route,
            catalog.Notes, bosses));
    }

    [HttpPost("dungeon-quests/teleport")]
    public async Task<ActionResult<AdministrationResult>> TeleportToDungeonQuestGiver(
        TeleportToDungeonQuestGiverRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the quest-giver teleport first."));
        if (request.QuestId == 0 || request.SpawnId == 0)
            return BadRequest(new AdministrationResult(false, "A quest and quest giver are required."));

        var playerNames = request.PlayerNames
            .Select(AzerothCoreSoapClient.RequirePlayerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        if (playerNames.Length == 0)
            return BadRequest(new AdministrationResult(false, "Select at least one online player."));

        await using var connection = connectionFactory.CreateConnection();
        var players = (await connection.QueryAsync<DungeonQuestPlayerRow>(new CommandDefinition("""
            SELECT guid AS CharacterGuid, name AS PlayerName, race AS CharacterRace,
                   class AS CharacterClass, level AS CharacterLevel, online AS Online
            FROM acore_characters.characters
            WHERE name IN @PlayerNames;
            """, new { PlayerNames = playerNames }, cancellationToken: cancellationToken))).AsList();
        if (players.Count != playerNames.Length || players.Any(player => !player.Online))
            return BadRequest(new AdministrationResult(false, "Every selected real player must be online."));

        var giver = await connection.QuerySingleOrDefaultAsync<DungeonQuestGiverValidationRow>(
            new CommandDefinition("""
                SELECT template.faction AS Faction
                FROM acore_world.creature_queststarter starter
                INNER JOIN acore_world.creature spawn ON spawn.id = starter.id
                INNER JOIN acore_world.creature_template template ON template.entry = spawn.id
                WHERE starter.quest = @QuestId AND spawn.guid = @SpawnId
                LIMIT 1;
                """, new { request.QuestId, request.SpawnId }, cancellationToken: cancellationToken));
        if (giver is null)
            return NotFound(new AdministrationResult(false, "That NPC does not start the selected quest."));
        if (players.Any(player => GetHostileTrainerFactions(player.CharacterRace).Contains(giver.Faction)))
            return BadRequest(new AdministrationResult(false, "That quest giver is hostile to a selected player."));

        var outputs = new List<string>();
        foreach (var player in players)
        {
            outputs.Add(await soapClient.ExecuteAsync(
                AzerothCoreSoapClient.BuildTrainerTeleportCommand(player.PlayerName, request.SpawnId),
                cancellationToken));
            Audit("TeleportToDungeonQuestGiver", player.PlayerName,
                $"Quest={request.QuestId};Spawn={request.SpawnId}");
        }

        return Ok(new AdministrationResult(true,
            $"Teleported {players.Count} player{(players.Count == 1 ? "" : "s")} to the quest giver.",
            string.Join(Environment.NewLine, outputs)));
    }

    [HttpPost("dungeon-quests/return")]
    [HttpPost("players/return")]
    public async Task<ActionResult<AdministrationResult>> ReturnFromDungeonQuestGiver(
        ReturnDungeonQuestPlayersRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm returning the players first."));
        var playerNames = request.PlayerNames
            .Select(AzerothCoreSoapClient.RequirePlayerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        if (playerNames.Length == 0)
            return BadRequest(new AdministrationResult(false, "No players have a saved return location."));

        await using var connection = connectionFactory.CreateConnection();
        var onlineNames = (await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT name FROM acore_characters.characters
            WHERE name IN @PlayerNames AND online = 1;
            """, new { PlayerNames = playerNames }, cancellationToken: cancellationToken))).AsList();
        if (onlineNames.Count != playerNames.Length)
            return BadRequest(new AdministrationResult(false, "Every selected player must still be online."));

        var outputs = new List<string>();
        foreach (var player in playerNames)
        {
            outputs.Add(await soapClient.ExecuteAsync($"webadmin quest return {player}", cancellationToken));
            Audit("ReturnFromDungeonQuestGiver", player, "SavedRecallLocation");
        }
        return Ok(new AdministrationResult(true,
            $"Returned {playerNames.Length} player{(playerNames.Length == 1 ? "" : "s")}.",
            string.Join(Environment.NewLine, outputs)));
    }

    [HttpPost("parties/launch")]
    public async Task<ActionResult<AdministrationResult>> LaunchParty(
        LaunchDungeonRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Explicit confirmation is required before moving the party."));
        var leader = AzerothCoreSoapClient.RequirePlayerName(request.LeaderName);
        if (request.DungeonId == 0) return BadRequest(new AdministrationResult(false, "A dungeon is required."));
        var output = await soapClient.ExecuteAsync($"webadmin group launch {leader} {request.DungeonId}", cancellationToken);
        Audit("LaunchParty", leader, $"DungeonId={request.DungeonId}");
        return Ok(new AdministrationResult(true, "The party was moved to the dungeon destination.", output));
    }

    private async Task<ActionResult<AdministrationResult>> ExecutePartyCommand(string command, string leaderName,
        string? botName, string message, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(leaderName);
        var bot = botName is null ? null : AzerothCoreSoapClient.RequirePlayerName(botName);
        var output = await soapClient.ExecuteAsync($"webadmin group {command} {leader}{(bot is null ? "" : $" {bot}")}", cancellationToken);
        Audit($"PartyBot{command}", leader, bot is null ? "" : $"Bot={bot}");
        return Ok(new AdministrationResult(true, message, output));
    }

    private static PartySnapshot ParsePartySnapshot(string requestedLeader, string output)
    {
        var leader = requestedLeader;
        var count = 1;
        var members = new List<PartyMember>();
        var candidates = new List<PartyBotCandidate>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 3 && fields[0] == "WEBADMIN_PARTY")
            {
                leader = fields[1];
                int.TryParse(fields[2], out count);
            }
            else if (fields.Length >= 5 && fields[0] == "WEBADMIN_MEMBER" && int.TryParse(fields[2], out var level))
                members.Add(new PartyMember(fields[1], level, fields[3], fields[4] == "1"));
            else if (fields.Length >= 5 && fields[0] == "WEBADMIN_CANDIDATE"
                && int.TryParse(fields[2], out var candidateLevel) && int.TryParse(fields[4], out var characterClass))
                candidates.Add(new PartyBotCandidate(fields[1], candidateLevel, fields[3], characterClass));
        }
        if (members.Count == 0)
            throw new InvalidOperationException("The worldserver returned an unrecognized party response. Rebuild and install the latest mod-web-admin module.");
        return new PartySnapshot(leader, count, members, candidates);
    }

    private static IReadOnlyList<DungeonDestination> ParseDungeons(string output)
    {
        var dungeons = new List<DungeonDestination>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 7 && fields[0] == "WEBADMIN_DUNGEON"
                && uint.TryParse(fields[1], out var id) && int.TryParse(fields[3], out var minimumLevel)
                && int.TryParse(fields[4], out var maximumLevel) && uint.TryParse(fields[5], out var mapId))
                dungeons.Add(new DungeonDestination(id, fields[2], minimumLevel, maximumLevel, mapId, fields[6]));
        }
        return dungeons;
    }

    internal static QuestingCompanionInspection ParseQuestingCompanionInspection(
        string output, string leaderName)
    {
        var companionRows = new List<(
            string Name, int Level, int CharacterClass, bool InLeaderParty,
            bool LootEnabled, int FreeBagSlots, int TotalBagSlots)>();
        var questsByCharacter = new Dictionary<
            string, Dictionary<uint, QuestProgressBuilder>>(
                StringComparer.OrdinalIgnoreCase);
        var questObjectStatuses = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var itemsByCharacter = new Dictionary<string, List<QuestingCompanionItem>>(
            StringComparer.OrdinalIgnoreCase);
        var equipmentChangesByCharacter = new Dictionary<
            string, List<QuestingCompanionEquipmentChange>>(
                StringComparer.OrdinalIgnoreCase);
        var inventoryChangesByCharacter = new Dictionary<
            string, List<QuestingCompanionEquipmentChange>>(
                StringComparer.OrdinalIgnoreCase);
        var maintenanceByCharacter = new Dictionary<string, (bool AutoSell, bool AutoRepair)>(
            StringComparer.OrdinalIgnoreCase);
        var behaviorByCharacter = new Dictionary<string, QuestingCompanionBehavior>(
            StringComparer.OrdinalIgnoreCase);
        var protocolVersion = 0;
        string? error = null;

        foreach (var line in output.Split(
                     ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 2
                && fields[0] == "WEBADMIN_COMPANION_PROTOCOL"
                && int.TryParse(fields[1], out var parsedProtocolVersion))
            {
                protocolVersion = parsedProtocolVersion;
                continue;
            }

            if (fields.Length >= 5
                && fields[0] == "WEBADMIN_COMPANION"
                && int.TryParse(fields[2], out var level)
                && int.TryParse(fields[3], out var characterClass)
                && int.TryParse(fields[4], out var inParty))
            {
                var lootEnabled = fields.Length >= 6
                    && int.TryParse(fields[5], out var loot) && loot != 0;
                var freeBagSlots = fields.Length >= 7
                    && int.TryParse(fields[6], out var free) ? free : 0;
                var totalBagSlots = fields.Length >= 8
                    && int.TryParse(fields[7], out var total) ? total : 0;
                companionRows.Add((
                    fields[1], level, characterClass, inParty != 0,
                    lootEnabled, freeBagSlots, totalBagSlots));
                continue;
            }

            if (fields.Length >= 3
                && fields[0] == "WEBADMIN_COMPANION_GATHER")
            {
                questObjectStatuses[fields[1]] = fields[2];
                continue;
            }

            if (fields.Length >= 13
                && fields[0] == "WEBADMIN_COMPANION_ITEM"
                && int.TryParse(fields[3], out var bag)
                && int.TryParse(fields[4], out var slot)
                && uint.TryParse(fields[5], out var itemId)
                && int.TryParse(fields[6], out var count)
                && int.TryParse(fields[7], out var quality)
                && int.TryParse(fields[8], out var itemLevel)
                && int.TryParse(fields[9], out var durability)
                && int.TryParse(fields[10], out var maximumDurability)
                && int.TryParse(fields[11], out var protectedItem))
            {
                if (!itemsByCharacter.TryGetValue(fields[1], out var items))
                {
                    items = [];
                    itemsByCharacter.Add(fields[1], items);
                }
                items.Add(new QuestingCompanionItem(
                    fields[2], bag, slot, itemId, count, quality, itemLevel,
                    durability, maximumDurability, protectedItem != 0, fields[12]));
                continue;
            }

            if (fields.Length >= 4
                && fields[0] == "WEBADMIN_COMPANION_MAINTENANCE"
                && int.TryParse(fields[2], out var autoSell)
                && int.TryParse(fields[3], out var autoRepair))
            {
                maintenanceByCharacter[fields[1]] =
                    (autoSell != 0, autoRepair != 0);
                continue;
            }

            if (fields.Length >= 11
                && fields[0] == "WEBADMIN_COMPANION_BEHAVIOR"
                && double.TryParse(
                    fields[6], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var followDistance)
                && int.TryParse(fields[7], out var behaviorLoot)
                && int.TryParse(fields[8], out var gather)
                && int.TryParse(fields[9], out var behaviorAutoSell)
                && int.TryParse(fields[10], out var behaviorAutoRepair))
            {
                behaviorByCharacter[fields[1]] = new(
                    fields[2], fields[3], fields[4], fields[5], followDistance,
                    behaviorLoot != 0, gather != 0, behaviorAutoSell != 0,
                    behaviorAutoRepair != 0);
                continue;
            }

            if (fields.Length >= 4
                && fields[0] == "WEBADMIN_COMPANION_EQUIPMENT_CHANGE"
                && long.TryParse(fields[2], out var changedAtUnix))
            {
                if (!equipmentChangesByCharacter.TryGetValue(
                        fields[1], out var changes))
                {
                    changes = [];
                    equipmentChangesByCharacter.Add(fields[1], changes);
                }
                changes.Add(new QuestingCompanionEquipmentChange(
                    changedAtUnix, fields[3]));
                continue;
            }

            if (fields.Length >= 4
                && fields[0] == "WEBADMIN_COMPANION_INVENTORY_CHANGE"
                && long.TryParse(fields[2], out var inventoryChangedAtUnix))
            {
                if (!inventoryChangesByCharacter.TryGetValue(
                        fields[1], out var changes))
                {
                    changes = [];
                    inventoryChangesByCharacter.Add(fields[1], changes);
                }
                changes.Add(new QuestingCompanionEquipmentChange(
                    inventoryChangedAtUnix, fields[3]));
                continue;
            }

            if (fields.Length >= 5
                && fields[0] == "WEBADMIN_COMPANION_QUEST"
                && uint.TryParse(fields[2], out var questId)
                && int.TryParse(fields[3], out var complete))
            {
                var quests = GetOrCreateQuestMap(questsByCharacter, fields[1]);
                quests[questId] = new QuestProgressBuilder(
                    questId, fields[4], complete != 0);
                continue;
            }

            if (fields.Length >= 8
                && fields[0] == "WEBADMIN_COMPANION_OBJECTIVE"
                && uint.TryParse(fields[2], out var objectiveQuestId)
                && uint.TryParse(fields[4], out var entry)
                && int.TryParse(fields[5], out var current)
                && int.TryParse(fields[6], out var required))
            {
                var quests = GetOrCreateQuestMap(questsByCharacter, fields[1]);
                if (!quests.TryGetValue(objectiveQuestId, out var quest))
                {
                    quest = new QuestProgressBuilder(
                        objectiveQuestId, $"Quest {objectiveQuestId}", false);
                    quests.Add(objectiveQuestId, quest);
                }
                quest.Objectives.Add(new(
                    fields[3], entry, fields[7], current, required));
                continue;
            }

            if (!line.StartsWith("WEBADMIN_", StringComparison.Ordinal))
                error ??= line;
        }

        IReadOnlyList<QuestingCompanionQuest> BuildQuests(string characterName)
        {
            if (!questsByCharacter.TryGetValue(characterName, out var quests))
                return [];
            return quests.Values.Select(quest => new QuestingCompanionQuest(
                quest.QuestId, quest.Title, quest.Complete,
                quest.Objectives.ToArray())).ToArray();
        }

        var companions = companionRows.Select(companion =>
        {
            var items = itemsByCharacter.GetValueOrDefault(companion.Name, []);
            var maintenance = maintenanceByCharacter.GetValueOrDefault(companion.Name);
            var behavior = behaviorByCharacter.GetValueOrDefault(
                companion.Name, new QuestingCompanionBehavior(
                    "legacy", "auto", "follow", "assist", 3,
                    companion.LootEnabled, true,
                    maintenance.AutoSell, maintenance.AutoRepair));
            return new ActiveQuestingCompanion(
                companion.Name, companion.Level, companion.CharacterClass,
                companion.InLeaderParty, companion.LootEnabled,
                companion.FreeBagSlots, companion.TotalBagSlots,
                BuildQuests(companion.Name),
                questObjectStatuses.GetValueOrDefault(companion.Name, ""),
                maintenance.AutoSell, maintenance.AutoRepair,
                items.Where(item => item.Location.Equals(
                    "equipment", StringComparison.OrdinalIgnoreCase)).ToArray(),
                items.Where(item => !item.Location.Equals(
                    "equipment", StringComparison.OrdinalIgnoreCase)).ToArray(),
                equipmentChangesByCharacter.GetValueOrDefault(
                    companion.Name, []).ToArray(),
                inventoryChangesByCharacter.GetValueOrDefault(
                    companion.Name, []).ToArray(),
                behavior);
        }).ToArray();
        return new QuestingCompanionInspection(
            companions, BuildQuests(leaderName), protocolVersion, error);
    }

    private static Dictionary<uint, QuestProgressBuilder> GetOrCreateQuestMap(
        Dictionary<string, Dictionary<uint, QuestProgressBuilder>> questsByCharacter,
        string characterName)
    {
        if (questsByCharacter.TryGetValue(characterName, out var quests))
            return quests;

        quests = [];
        questsByCharacter.Add(characterName, quests);
        return quests;
    }

    private async Task ValidateCompanionPairAsync(
        string leader, string companion, bool starting,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var pair = (await connection.QueryAsync<CompanionValidationRow>(
            new CommandDefinition("""
                SELECT c.name Name, c.account AccountId, c.race CharacterRace,
                       c.online<>0 Online, COALESCE(gm.guildid, 0) GuildId
                FROM acore_characters.characters c
                LEFT JOIN acore_characters.guild_member gm ON gm.guid=c.guid
                WHERE c.name IN @Names
                """, new { Names = new[] { leader, companion } },
                cancellationToken: cancellationToken))).AsList();
        var leaderRow = pair.FirstOrDefault(value =>
            value.Name.Equals(leader, StringComparison.OrdinalIgnoreCase));
        var companionRow = pair.FirstOrDefault(value =>
            value.Name.Equals(companion, StringComparison.OrdinalIgnoreCase));
        if (leaderRow is null || !leaderRow.Online)
            throw new InvalidOperationException("The leader must be online.");
        if (companionRow is null)
            throw new InvalidOperationException("The companion character was not found.");
        if (starting && companionRow.Online)
            throw new InvalidOperationException("The companion is already online.");
        if (leaderRow.AccountId == companionRow.AccountId)
            throw new InvalidOperationException(
                "The leader and companion must be on different game accounts.");
        if (IsAllianceRace(leaderRow.CharacterRace)
            != IsAllianceRace(companionRow.CharacterRace))
            throw new InvalidOperationException(
                "The leader and companion must belong to the same faction.");
        if (starting && (leaderRow.GuildId == 0 || leaderRow.GuildId != companionRow.GuildId))
        {
            await using var maintenance = connectionFactory.CreateMaintenanceConnection();
            var linked = await maintenance.ExecuteScalarAsync<long>(new CommandDefinition("""
                SELECT COUNT(*)
                FROM acore_playerbots.playerbots_account_links
                WHERE account_id=@LeaderAccount
                  AND linked_account_id=@CompanionAccount;
                """, new
                {
                    LeaderAccount = leaderRow.AccountId,
                    CompanionAccount = companionRow.AccountId
                }, cancellationToken: cancellationToken));
            if (linked == 0)
                throw new InvalidOperationException(
                    "The accounts are not trusted in PlayerBots and the characters do not share a guild. Link the accounts first.");
        }
    }

    private static bool IsAllianceRace(int race) =>
        race is 1 or 3 or 4 or 7 or 11;

    private static bool IsLootSuggested(
        DungeonLootRow item, IReadOnlyList<DungeonPartyCharacterRow> party) =>
        party.Any(character =>
            (item.AllowableClass is -1 or 0
                || (item.AllowableClass
                    & (1L << (character.CharacterClass - 1))) != 0)
            && item.RequiredLevel <= character.CharacterLevel + 5);

    private static DungeonQuestPlayerStatus EvaluateDungeonQuestStatus(
        DungeonQuestPlayerRow player, DungeonQuestRow quest, DungeonQuestState? state, bool hasGiver)
    {
        if (!player.Online)
            return new(player.PlayerName, "Offline", "Player must be online to teleport.", false);
        if (state?.QuestState == 2)
            return new(player.PlayerName, "Completed", "Already completed.", false);
        if (state?.QuestState == 1)
            return new(player.PlayerName, "InProgress", "Already in the quest log.", false);
        if (player.CharacterLevel < quest.MinimumLevel)
            return new(player.PlayerName, "LevelTooLow", $"Requires level {quest.MinimumLevel}.", false);
        if (!DungeonQuestEligibilityRules.MaskAllows(quest.AllowableRaces, player.CharacterRace))
            return new(player.PlayerName, "WrongRace", "Unavailable to this race.", false);
        if (!DungeonQuestEligibilityRules.MaskAllows(quest.AllowableClasses, player.CharacterClass))
            return new(player.PlayerName, "WrongClass", "Unavailable to this class.", false);
        if (quest.PreviousQuestId != 0 && state?.PrerequisiteCompleted != true)
        {
            var prerequisite = string.IsNullOrWhiteSpace(quest.PreviousQuestTitle)
                ? $"quest {Math.Abs(quest.PreviousQuestId)}"
                : quest.PreviousQuestTitle;
            return new(player.PlayerName, "MissingPrerequisite", $"Complete {prerequisite} first.", false);
        }
        if (quest.RequiredFactionId1 != 0 && state is not null
            && state.Reputation1 < quest.RequiredFactionValue1)
            return new(player.PlayerName, "ReputationRequired",
                $"Faction {quest.RequiredFactionId1}: {state.Reputation1:N0}/{quest.RequiredFactionValue1:N0}.", false);
        if (quest.RequiredFactionId2 != 0 && state is not null
            && state.Reputation2 < quest.RequiredFactionValue2)
            return new(player.PlayerName, "ReputationRequired",
                $"Faction {quest.RequiredFactionId2}: {state.Reputation2:N0}/{quest.RequiredFactionValue2:N0}.", false);
        if (quest.RequiredMinRepFaction != 0 && state is not null
            && state.ReputationMinimum < quest.RequiredMinRepValue)
            return new(player.PlayerName, "ReputationRequired",
                $"Faction {quest.RequiredMinRepFaction}: {state.ReputationMinimum:N0}/{quest.RequiredMinRepValue:N0}.", false);
        if (quest.RequiredMaxRepFaction != 0 && state is not null
            && state.ReputationMaximum > quest.RequiredMaxRepValue)
            return new(player.PlayerName, "ReputationTooHigh",
                $"Faction {quest.RequiredMaxRepFaction} must not exceed {quest.RequiredMaxRepValue:N0}.", false);
        if (!hasGiver)
            return new(player.PlayerName, quest.StartItem == 0 ? "NoNpcGiver" : "StartedByItem",
                quest.StartItem == 0 ? "No compatible NPC quest giver was found." : $"Started by item {quest.StartItem}.", false);
        return new(player.PlayerName, "Available", "Eligible to collect now.", true);
    }

    private static async Task<bool> IsCharacterOnlineAsync(
        MySqlConnector.MySqlConnection connection, string playerName, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM acore_characters.characters
            WHERE name = @PlayerName AND online = 1;
            """, new { PlayerName = playerName }, cancellationToken: cancellationToken)) > 0;

    private sealed class DungeonQuestRow
    {
        public uint QuestId { get; init; }
        public string Title { get; init; } = string.Empty;
        public byte MinimumLevel { get; init; }
        public uint AllowableRaces { get; init; }
        public uint AllowableClasses { get; init; }
        public int PreviousQuestId { get; init; }
        public string PreviousQuestTitle { get; init; } = string.Empty;
        public ushort RequiredFactionId1 { get; init; }
        public int RequiredFactionValue1 { get; init; }
        public ushort RequiredFactionId2 { get; init; }
        public int RequiredFactionValue2 { get; init; }
        public ushort RequiredMinRepFaction { get; init; }
        public int RequiredMinRepValue { get; init; }
        public ushort RequiredMaxRepFaction { get; init; }
        public int RequiredMaxRepValue { get; init; }
        public uint StartItem { get; init; }
    }

    private sealed class CompanionLeaderRow
    {
        public uint AccountId { get; init; }
        public int CharacterRace { get; init; }
        public int CharacterLevel { get; init; }
        public uint GuildId { get; init; }
    }

    private sealed class CompanionAccountRow
    {
        public string Name { get; init; } = "";
        public uint AccountId { get; init; }
    }

    private sealed class CompanionValidationRow
    {
        public string Name { get; init; } = "";
        public uint AccountId { get; init; }
        public int CharacterRace { get; init; }
        public bool Online { get; init; }
        public uint GuildId { get; init; }
    }

    internal sealed record QuestingCompanionInspection(
        IReadOnlyList<ActiveQuestingCompanion> ActiveCompanions,
        IReadOnlyList<QuestingCompanionQuest> LeaderQuests,
        int ProtocolVersion,
        string? Error);

    private sealed class QuestProgressBuilder(
        uint questId, string title, bool complete)
    {
        public uint QuestId { get; } = questId;
        public string Title { get; } = title;
        public bool Complete { get; } = complete;
        public List<QuestingCompanionObjective> Objectives { get; } = [];
    }

    private sealed class DungeonEncounterRow
    {
        public uint EncounterEntry { get; init; }
        public uint CreatureId { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class DungeonPartyCharacterRow
    {
        public uint Guid { get; init; }
        public string Name { get; init; } = "";
        public int CharacterClass { get; init; }
        public int Race { get; init; }
        public int CharacterLevel { get; init; }
    }

    private sealed class DungeonLibraryCharacterRow
    {
        public uint Guid { get; init; }
        public string Name { get; init; } = "";
        public string Username { get; init; } = "";
        public int Level { get; init; }
        public int CharacterClass { get; init; }
        public bool Online { get; init; }
    }

    private sealed class CharacterTransferSourceRow
    {
        public uint AccountId { get; init; }
        public string Username { get; init; } = "";
        public bool Online { get; init; }
    }

    private sealed class CharacterTransferAccountRow
    {
        public uint AccountId { get; init; }
        public string Username { get; init; } = "";
        public string Classification { get; init; } = "";
        public int CharacterCount { get; init; }
    }

    private sealed class DungeonLootRow
    {
        public uint BossCreatureId { get; init; }
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
    }

    private sealed class DungeonQuestState
    {
        public string PlayerName { get; init; } = string.Empty;
        public int QuestState { get; init; }
        public bool PrerequisiteCompleted { get; init; }
        public int Reputation1 { get; init; }
        public int Reputation2 { get; init; }
        public int ReputationMinimum { get; init; }
        public int ReputationMaximum { get; init; }
    }

    private sealed class DungeonLockoutRow
    {
        public string PlayerName { get; init; } = string.Empty;
        public ushort MapId { get; init; }
        public byte Difficulty { get; init; }
        public DateTime ResetAtUtc { get; init; }
    }

    private sealed class DungeonQuestPlayerRow
    {
        public uint CharacterGuid { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public byte CharacterRace { get; init; }
        public byte CharacterClass { get; init; }
        public byte CharacterLevel { get; init; }
        public bool Online { get; init; }
    }

    private sealed class DungeonQuestGiverRow
    {
        public uint SpawnId { get; init; }
        public uint CreatureId { get; init; }
        public string Name { get; init; } = string.Empty;
        public ushort MapId { get; init; }
        public ushort ZoneId { get; init; }
    }

    private sealed class DungeonQuestGiverValidationRow
    {
        public ushort Faction { get; init; }
    }

    private sealed class CharacterCollectibleContext
    {
        public uint CharacterGuid { get; init; }
        public byte CharacterLevel { get; init; }
    }

    private sealed class TrainerCharacterContext
    {
        public byte CharacterRace { get; init; }
        public byte CharacterClass { get; init; }
        public ushort MapId { get; init; }
        public float PositionX { get; init; }
        public float PositionY { get; init; }
    }

    private sealed class NpcTeleportRiskRow
    {
        public string Name { get; init; } = "";
        public bool PotentiallyHostile { get; init; }
    }

    private sealed class ItemTargetRow
    {
        public ulong Guid { get; init; }
        public string Name { get; init; } = "";
        public byte CharacterClass { get; init; }
        public byte Race { get; init; }
        public byte CharacterLevel { get; init; }
    }

    private sealed class EquippedItemLevelRow
    {
        public ulong Guid { get; init; }
        public int InventoryType { get; init; }
        public int ItemLevel { get; init; }
    }

    private sealed class WishlistItemRow
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = "";
        public byte Quality { get; init; }
        public ushort ItemLevel { get; init; }
        public byte RequiredLevel { get; init; }
        public byte ItemClass { get; init; }
        public byte ItemSubclass { get; init; }
        public byte InventoryType { get; init; }
        public long AllowableClass { get; init; }
        public long AllowableRace { get; init; }
    }

    private sealed class WishlistSourceRow
    {
        public uint ItemId { get; init; }
        public uint BossCreatureId { get; init; }
        public string BossName { get; init; } = "";
        public uint MapId { get; init; }
        public double DropChance { get; init; }
    }

    private sealed class WishlistOwnershipRow
    {
        public ulong CharacterGuid { get; init; }
        public uint ItemId { get; init; }
        public bool Equipped { get; init; }
    }

    private static bool IsAllianceRace(byte characterRace) =>
        characterRace is 1 or 3 or 4 or 7 or 11;

    private static int[] GetHostileTrainerFactions(byte characterRace) =>
        characterRace is 1 or 3 or 4 or 7 or 11
            ? [29, 68, 104, 126, 1604, 1695, 1744]
            : [11, 12, 55, 80, 875, 894, 1638, 1640, 1698, 1741];

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is { } address
        && IPAddress.IsLoopback(address);

    private void Audit(string operation, string target, string detail) =>
        logger.LogWarning("ADMIN AUDIT: Operation={Operation};Target={Target};Detail={Detail}", operation, target, detail);

    private static string Quote(string? value, int maximumLength)
    {
        var safe = (value ?? string.Empty).Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
        return $"\"{safe[..Math.Min(safe.Length, maximumLength)]}\"";
    }
}
