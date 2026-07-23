using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/auction-house")]
public sealed class AuctionHouseDashboardController(
    AzerothCoreConnectionFactory connectionFactory,
    AzerothCoreSoapClient soapClient,
    ILogger<AuctionHouseDashboardController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuctionHouseDashboard>> GetDashboard(
        [FromQuery] string? search, [FromQuery] int houseId = 0,
        [FromQuery] int category = -1, [FromQuery] int quality = -1,
        [FromQuery] string sort = "expiry", [FromQuery] bool descending = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalRequest()) return NotFound();
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        houseId = houseId is 2 or 6 or 7 ? houseId : 0;
        category = category is >= 0 and <= 15 ? category : -1;
        quality = quality is >= 0 and <= 7 ? quality : -1;

        var orderBy = sort.ToLowerInvariant() switch
        {
            "item" => "template.name",
            "house" => "auction.houseid",
            "seller" => "Seller",
            "stack" => "instance.count",
            "buyout" => "auction.buyoutprice",
            _ => "auction.time"
        };
        var direction = descending ? "DESC" : "ASC";
        var where = """
            WHERE (@Search IS NULL OR template.name LIKE CONCAT('%', @Search, '%')
                   OR CAST(template.entry AS CHAR) = @Search)
              AND (@HouseId = 0 OR auction.houseid = @HouseId)
              AND (@Category = -1 OR template.class = @Category)
              AND (@Quality = -1 OR template.Quality = @Quality)
            """;
        var parameters = new
        {
            Search = search, HouseId = houseId, Category = category, Quality = quality,
            Offset = (page - 1) * pageSize, PageSize = pageSize
        };

        await using var connection = connectionFactory.CreateConnection();
        var totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
            SELECT COUNT(*)
            FROM acore_characters.auctionhouse auction
            INNER JOIN acore_characters.item_instance instance ON instance.guid = auction.itemguid
            INNER JOIN acore_world.item_template template ON template.entry = instance.itemEntry
            {where};
            """, parameters, cancellationToken: cancellationToken));

        var rows = (await connection.QueryAsync<ListingRow>(new CommandDefinition($"""
            SELECT auction.id AS AuctionId, auction.houseid AS HouseId,
                   template.entry AS ItemId, template.name AS ItemName,
                   template.Quality AS Quality, template.class AS ItemClass,
                   instance.count AS StackSize, auction.startbid AS StartBid,
                   auction.lastbid AS LastBid, auction.buyoutprice AS BuyoutPrice,
                   auction.time AS ExpiresUnix,
                   COALESCE(seller.name, CONCAT('Character ', auction.itemowner)) AS Seller,
                   bidder.name AS Bidder, sellerAccount.username = 'AHBOT' AS IsBotAuctionValue
            FROM acore_characters.auctionhouse auction
            INNER JOIN acore_characters.item_instance instance ON instance.guid = auction.itemguid
            INNER JOIN acore_world.item_template template ON template.entry = instance.itemEntry
            LEFT JOIN acore_characters.characters seller ON seller.guid = auction.itemowner
            LEFT JOIN acore_auth.account sellerAccount ON sellerAccount.id = seller.account
            LEFT JOIN acore_characters.characters bidder ON bidder.guid = auction.buyguid
            {where}
            ORDER BY {orderBy} {direction}, auction.id
            LIMIT @Offset, @PageSize;
            """, parameters, cancellationToken: cancellationToken))).ToArray();

        var overall = await connection.QuerySingleAsync<OverallRow>(new CommandDefinition("""
            SELECT COUNT(*) AS TotalAuctions,
                   COALESCE(SUM(sellerAccount.username = 'AHBOT'), 0) AS BotAuctions,
                   COALESCE(SUM(sellerAccount.username IS NULL OR sellerAccount.username <> 'AHBOT'), 0) AS PlayerAuctions,
                   COALESCE(SUM(auction.time BETWEEN UNIX_TIMESTAMP() AND UNIX_TIMESTAMP() + 3600), 0) AS ExpiringWithinHour
            FROM acore_characters.auctionhouse auction
            LEFT JOIN acore_characters.characters seller ON seller.guid = auction.itemowner
            LEFT JOIN acore_auth.account sellerAccount ON sellerAccount.id = seller.account;
            """, cancellationToken: cancellationToken));
        var houseCounts = await connection.QueryAsync<CountRow>(new CommandDefinition("""
            SELECT houseid AS Id, COUNT(*) AS Count
            FROM acore_characters.auctionhouse GROUP BY houseid;
            """, cancellationToken: cancellationToken));
        var qualityCounts = await connection.QueryAsync<CountRow>(new CommandDefinition("""
            SELECT template.Quality AS Id, COUNT(*) AS Count
            FROM acore_characters.auctionhouse auction
            INNER JOIN acore_characters.item_instance instance ON instance.guid = auction.itemguid
            INNER JOIN acore_world.item_template template ON template.entry = instance.itemEntry
            GROUP BY template.Quality;
            """, cancellationToken: cancellationToken));
        var categoryCounts = await connection.QueryAsync<CountRow>(new CommandDefinition("""
            SELECT template.class AS Id, COUNT(*) AS Count
            FROM acore_characters.auctionhouse auction
            INNER JOIN acore_characters.item_instance instance ON instance.guid = auction.itemguid
            INNER JOIN acore_world.item_template template ON template.entry = instance.itemEntry
            GROUP BY template.class;
            """, cancellationToken: cancellationToken));

        var summary = new AuctionHouseSummary(
            overall.TotalAuctions, overall.BotAuctions, overall.PlayerAuctions, overall.ExpiringWithinHour,
            new[] { 2, 6, 7 }.Select(id => new AuctionHouseCount(
                id, HouseName(id), houseCounts.FirstOrDefault(item => item.Id == id)?.Count ?? 0)).ToArray(),
            Enumerable.Range(0, 8).Select(id => new AuctionHouseCount(
                id, QualityName(id), qualityCounts.FirstOrDefault(item => item.Id == id)?.Count ?? 0)).ToArray(),
            Enumerable.Range(0, 16).Select(id => new AuctionHouseCount(
                id, CategoryName(id), categoryCounts.FirstOrDefault(item => item.Id == id)?.Count ?? 0)).ToArray());

        return Ok(new AuctionHouseDashboard(
            summary,
            rows.Select(row => new AuctionHouseListing(
                row.AuctionId, row.HouseId, HouseName(row.HouseId), row.ItemId, row.ItemName,
                row.Quality, QualityName(row.Quality), row.ItemClass, CategoryName(row.ItemClass),
                row.StackSize, row.StartBid, row.LastBid, row.BuyoutPrice,
                DateTimeOffset.FromUnixTimeSeconds(row.ExpiresUnix).UtcDateTime,
                row.Seller, row.Bidder, row.IsBotAuctionValue != 0)).ToArray(),
            page, pageSize, totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    [HttpPost("restock")]
    public async Task<ActionResult<AdministrationResult>> EnableRestocking(
        AuctionHouseRestockRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm enabling AHBot restocking first."));
        var output = await soapClient.ExecuteAsync(
            AzerothCoreSoapClient.BuildAuctionHouseSellerCommand(true), cancellationToken);
        logger.LogInformation("AHBot seller enabled from the auction dashboard.");
        return Ok(new AdministrationResult(true,
            "AHBot selling was enabled. It will add stock during its normal update cycles.", output));
    }

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is null
        || System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress);

    internal static string HouseName(int id) => id switch
    {
        2 => "Alliance", 6 => "Horde", 7 => "Neutral", _ => $"House {id}"
    };

    internal static string QualityName(int id) => id switch
    {
        0 => "Poor", 1 => "Common", 2 => "Uncommon", 3 => "Rare",
        4 => "Epic", 5 => "Legendary", 6 => "Artifact", 7 => "Heirloom", _ => $"Quality {id}"
    };

    internal static string CategoryName(int id) => id switch
    {
        0 => "Consumable", 1 => "Container", 2 => "Weapon", 3 => "Gem",
        4 => "Armor", 5 => "Reagent", 6 => "Projectile", 7 => "Trade goods",
        8 => "Generic", 9 => "Recipe", 10 => "Money", 11 => "Quiver",
        12 => "Quest", 13 => "Key", 14 => "Permanent", 15 => "Miscellaneous",
        _ => $"Category {id}"
    };

    private sealed class ListingRow
    {
        public uint AuctionId { get; init; }
        public byte HouseId { get; init; }
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = "";
        public byte Quality { get; init; }
        public byte ItemClass { get; init; }
        public uint StackSize { get; init; }
        public uint StartBid { get; init; }
        public uint LastBid { get; init; }
        public uint BuyoutPrice { get; init; }
        public uint ExpiresUnix { get; init; }
        public string Seller { get; init; } = "";
        public string? Bidder { get; init; }
        public byte IsBotAuctionValue { get; init; }
    }

    private sealed class OverallRow
    {
        public int TotalAuctions { get; init; }
        public int BotAuctions { get; init; }
        public int PlayerAuctions { get; init; }
        public int ExpiringWithinHour { get; init; }
    }

    private sealed class CountRow
    {
        public int Id { get; init; }
        public int Count { get; init; }
    }
}
