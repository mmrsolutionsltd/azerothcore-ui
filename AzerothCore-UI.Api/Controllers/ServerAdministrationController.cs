using System.Net;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using AzerothCore_UI.Api.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/server-administration")]
public sealed class ServerAdministrationController(
    AzerothCoreServerManager serverManager,
    AzerothCoreSoapClient soapClient,
    AzerothCoreConfigurationManager configurationManager,
    AzerothCoreConnectionFactory connectionFactory,
    SpellMetadataProvider spellMetadataProvider,
    ILogger<ServerAdministrationController> logger) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<ServerStatus>> GetStatus(CancellationToken cancellationToken) =>
        IsLocalRequest() ? Ok(await serverManager.GetStatusAsync(cancellationToken)) : NotFound();

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
            ORDER BY characterData.online DESC,
                     CASE WHEN account.username LIKE CONCAT(@BotPrefix, '%') THEN 1 ELSE 0 END,
                     characterData.name
            LIMIT 5000;
            """;
        await using var connection = connectionFactory.CreateConnection();
        var players = await connection.QueryAsync<AdministrationPlayer>(new CommandDefinition(
            sql, new { BotPrefix = "rndbot" }, cancellationToken: cancellationToken));
        return Ok(players.AsList());
    }

    [HttpGet("items")]
    public async Task<ActionResult<AdministrationItemSearchResult>> GetItems(
        [FromQuery] string? search, [FromQuery] string category = "all", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var categoryFilter = GetItemCategoryFilter(category);
        if (categoryFilter is null) return BadRequest("Unknown item category.");
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var where = $"WHERE item.name <> '' AND (@Search IS NULL OR item.name LIKE CONCAT('%', @Search, '%')) {categoryFilter}";
        var sql = $"""
            SELECT COUNT(*) FROM acore_world.item_template item {where};
            SELECT item.entry AS ItemId, item.name AS Name, item.class AS ItemClass,
                   item.subclass AS ItemSubclass, item.Quality AS Quality,
                   item.ItemLevel AS ItemLevel, item.RequiredLevel AS RequiredLevel
            FROM acore_world.item_template item
            {where}
            ORDER BY item.name, item.entry
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new { Search = normalizedSearch, PageSize = pageSize, Offset = (page - 1) * pageSize };
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var items = (await results.ReadAsync<AdministrationItem>()).AsList();
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
        if (request.CreatureId == 0 || request.Level is < 1 or > 83 || request.DespawnMinutes is < 1 or > 30)
            return BadRequest(new AdministrationResult(false, "A creature, level 1-83, and despawn time of 1-30 minutes are required."));
        var output = await soapClient.ExecuteAsync(
            $"webadmin creature spawn {anchor} {request.CreatureId} {request.Level} {request.DespawnMinutes}", cancellationToken);
        Audit("SpawnCreature", anchor,
            $"Creature={request.CreatureId};Level={request.Level};DespawnMinutes={request.DespawnMinutes}");
        return Ok(new AdministrationResult(true, "Temporary creature spawned beside the player.", output));
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

    [HttpGet("parties/{leaderName}")]
    public async Task<ActionResult<PartySnapshot>> GetParty(string leaderName, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var leader = AzerothCoreSoapClient.RequirePlayerName(leaderName);
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

    [HttpGet("dungeons")]
    public async Task<ActionResult<IReadOnlyList<DungeonDestination>>> GetDungeons(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var output = await soapClient.ExecuteAsync("webadmin dungeon list", cancellationToken);
        var dungeons = new List<DungeonDestination>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 7 && fields[0] == "WEBADMIN_DUNGEON"
                && uint.TryParse(fields[1], out var id) && int.TryParse(fields[3], out var minimumLevel)
                && int.TryParse(fields[4], out var maximumLevel) && uint.TryParse(fields[5], out var mapId))
                dungeons.Add(new DungeonDestination(id, fields[2], minimumLevel, maximumLevel, mapId, fields[6]));
        }
        if (dungeons.Count == 0)
            throw new InvalidOperationException("The worldserver returned no dungeon destinations. Rebuild and install the latest mod-web-admin module.");
        return Ok(dungeons.OrderBy(dungeon => dungeon.MinimumLevel).ThenBy(dungeon => dungeon.Name));
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

    private sealed class CharacterCollectibleContext
    {
        public uint CharacterGuid { get; init; }
        public byte CharacterLevel { get; init; }
    }

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
