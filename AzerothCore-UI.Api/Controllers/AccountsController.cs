using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using AzerothCore_UI.Api.Security;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(AzerothCoreConnectionFactory connectionFactory) : ControllerBase
{
    private const string PlayerBotAccountPrefix = "rndbot";
    private const string SelectAccountsSql = """
        SELECT
            a.id AS AccountId,
            a.username AS Username,
            a.last_login AS LastLogin,
            CASE
                WHEN a.username LIKE CONCAT(@PlayerBotPrefix, '%') THEN 'PlayerBot'
                ELSE 'Human'
            END AS Classification,
            c.guid AS CharacterGuid,
            c.name AS CharacterName,
            c.level AS CharacterLevel,
            c.race AS CharacterRace,
            c.class AS CharacterClass,
            c.online AS CharacterOnline,
            c.zone AS CharacterZone,
            NULLIF(area.AreaName_Lang_enUS, '') AS CharacterDatabaseLocationName
        FROM acore_auth.account AS a
        LEFT JOIN acore_characters.characters AS c ON c.account = a.id
        LEFT JOIN acore_world.areatable_dbc AS area ON area.ID = c.zone
        WHERE (@AccountId IS NULL OR a.id = @AccountId)
        ORDER BY a.id, c.guid;
        """;

    [HttpGet]
    public async Task<ActionResult<PagedAccounts>> GetAccounts(
        CancellationToken cancellationToken,
        [FromQuery] string? search,
        [FromQuery] string type = "human",
        [FromQuery] string sort = "username",
        [FromQuery] bool descending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        type = type.ToLowerInvariant() switch
        {
            "playerbot" => "playerbot",
            "all" => "all",
            _ => "human"
        };

        var orderBy = sort.ToLowerInvariant() switch
        {
            "accountid" => "a.id",
            "lastlogin" => "a.last_login",
            "charactercount" => "CharacterCount",
            _ => "a.username"
        };
        var direction = descending ? "DESC" : "ASC";

        var filters = """
            WHERE (@Search IS NULL OR a.username LIKE CONCAT('%', @Search, '%'))
              AND (
                  @Type = 'all'
                  OR (@Type = 'playerbot' AND a.username LIKE CONCAT(@PlayerBotPrefix, '%'))
                  OR (@Type = 'human' AND a.username NOT LIKE CONCAT(@PlayerBotPrefix, '%'))
              )
              AND (@AllAccounts OR a.id IN @AllowedAccounts)
            """;

        var countSql = $"""
            SELECT COUNT(*)
            FROM acore_auth.account AS a
            {filters};
            """;

        var pageSql = $"""
            SELECT
                a.id AS AccountId,
                a.username AS Username,
                CASE
                    WHEN a.username LIKE CONCAT(@PlayerBotPrefix, '%') THEN 'PlayerBot'
                    ELSE 'Human'
                END AS Classification,
                a.last_login AS LastLogin,
                COALESCE((SELECT MAX(access.gmlevel) FROM acore_auth.account_access access WHERE access.id = a.id), 0) AS GmLevel,
                COUNT(c.guid) AS CharacterCount,
                COALESCE(SUM(CASE WHEN c.online <> 0 THEN 1 ELSE 0 END), 0) AS OnlineCharacterCount
            FROM acore_auth.account AS a
            LEFT JOIN acore_characters.characters AS c ON c.account = a.id
            {filters}
            GROUP BY a.id, a.username, a.last_login
            ORDER BY {orderBy} {direction}, a.id ASC
            LIMIT @PageSize OFFSET @Offset;
            """;

        var identity = HttpContext.AdministrationIdentity();
        var parameters = new
        {
            Search = search,
            Type = type,
            PlayerBotPrefix = PlayerBotAccountPrefix,
            PageSize = pageSize,
            Offset = (long)(page - 1) * pageSize,
            AllAccounts = identity?.AccountScope == "All",
            AllowedAccounts = identity?.GameAccountIds ?? []
        };

        await using var connection = connectionFactory.CreateConnection();
        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<AccountSummaryRow>(
            new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = rows.Select(row => new AccountSummary(
            row.AccountId,
            row.Username,
            row.Classification,
            row.LastLogin,
            row.CharacterCount,
            row.OnlineCharacterCount,
            row.GmLevel)).ToArray();

        return Ok(new PagedAccounts(items, page, pageSize, totalItems, totalPages));
    }

    [HttpGet("{accountId:long}")]
    public async Task<ActionResult<AccountWithCharacters>> GetAccount(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId is < 0 or > uint.MaxValue)
        {
            return BadRequest("The account ID is outside the supported range.");
        }

        var account = (await QueryAccounts((uint)accountId, cancellationToken)).SingleOrDefault();
        return account is null ? NotFound() : Ok(account);
    }

    private async Task<IReadOnlyList<AccountWithCharacters>> QueryAccounts(
        uint? accountId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AccountCharacterRow>(
            new CommandDefinition(
                SelectAccountsSql,
                new { PlayerBotPrefix = PlayerBotAccountPrefix, AccountId = accountId },
                cancellationToken: cancellationToken));

        return rows
            .GroupBy(row => new { row.AccountId, row.Username, row.Classification, row.LastLogin })
            .Select(group => new AccountWithCharacters(
                group.Key.AccountId,
                group.Key.Username,
                group.Key.Classification,
                group.Key.LastLogin,
                group
                    .Where(row => row.CharacterGuid.HasValue)
                    .Select(row => new CharacterSummary(
                        row.CharacterGuid!.Value,
                        row.CharacterName!,
                        row.CharacterLevel!.Value,
                        row.CharacterRace!.Value,
                        row.CharacterClass!.Value,
                        row.CharacterOnline!.Value != 0,
                        AreaNameResolver.Resolve(
                            row.CharacterZone!.Value,
                            row.CharacterDatabaseLocationName)))
                    .ToArray()))
            .ToArray();
    }

    private sealed class AccountCharacterRow
    {
        public uint AccountId { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Classification { get; init; } = string.Empty;
        public DateTime? LastLogin { get; init; }
        public uint? CharacterGuid { get; init; }
        public string? CharacterName { get; init; }
        public byte? CharacterLevel { get; init; }
        public byte? CharacterRace { get; init; }
        public byte? CharacterClass { get; init; }
        public byte? CharacterOnline { get; init; }
        public ushort? CharacterZone { get; init; }
        public string? CharacterDatabaseLocationName { get; init; }
    }

    private sealed class AccountSummaryRow
    {
        public uint AccountId { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Classification { get; init; } = string.Empty;
        public DateTime? LastLogin { get; init; }
        public long CharacterCount { get; init; }
        public long OnlineCharacterCount { get; init; }
        public byte GmLevel { get; init; }
    }
}
