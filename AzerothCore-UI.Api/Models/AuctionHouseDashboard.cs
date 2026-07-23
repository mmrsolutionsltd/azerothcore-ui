namespace AzerothCore_UI.Api.Models;

public sealed record AuctionHouseSummary(
    int TotalAuctions, int BotAuctions, int PlayerAuctions, int ExpiringWithinHour,
    IReadOnlyList<AuctionHouseCount> Houses,
    IReadOnlyList<AuctionHouseCount> Qualities,
    IReadOnlyList<AuctionHouseCount> Categories);

public sealed record AuctionHouseCount(int Id, string Name, int Count);

public sealed record AuctionHouseListing(
    uint AuctionId, byte HouseId, string HouseName, uint ItemId, string ItemName,
    byte Quality, string QualityName, byte ItemClass, string CategoryName,
    uint StackSize, uint StartBid, uint LastBid, uint BuyoutPrice,
    DateTime ExpiresAtUtc, string Seller, string? Bidder, bool IsBotAuction);

public sealed record AuctionHouseDashboard(
    AuctionHouseSummary Summary, IReadOnlyList<AuctionHouseListing> Auctions,
    int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record AuctionHouseRestockRequest(bool Confirmed);
