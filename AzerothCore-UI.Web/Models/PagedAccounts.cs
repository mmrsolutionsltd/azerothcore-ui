namespace AzerothCore_UI.Web.Models;

public sealed record PagedAccounts(
    IReadOnlyList<AccountSummary> Items,
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages);

public sealed record AccountSummary(
    uint AccountId,
    string Username,
    string Classification,
    DateTime? LastLogin,
    long CharacterCount,
    long OnlineCharacterCount);
