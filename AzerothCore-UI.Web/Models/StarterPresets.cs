namespace AzerothCore_UI.Web.Models;

public sealed record StarterPresetRequest(
    IReadOnlyList<string> PlayerNames, string Preset, int BagCount, bool IncludeHeirlooms,
    bool IncludeHearthstone, bool IncludeFoodAndDrink, bool IncludeClassSupplies,
    int MoneyGold, bool Confirmed);

public sealed record StarterPresetAction(
    string Kind, uint? ItemId, string Description, int Quantity,
    string Delivery, bool Skipped, string? SkipReason);

public sealed record StarterPresetCharacterPreview(
    string PlayerName, byte Level, byte Race, byte Class, bool Online,
    IReadOnlyList<StarterPresetAction> Actions);

public sealed record StarterPresetPreview(
    string Preset, IReadOnlyList<StarterPresetCharacterPreview> Characters);

public sealed record StarterPresetActionResult(
    string Description, bool Success, bool Skipped, string Message);

public sealed record StarterPresetCharacterResult(
    string PlayerName, bool Success, IReadOnlyList<StarterPresetActionResult> Actions);

public sealed record StarterPresetApplyResult(
    bool Success, string Message, IReadOnlyList<StarterPresetCharacterResult> Characters);
