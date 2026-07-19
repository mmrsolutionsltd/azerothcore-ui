namespace AzerothCore_UI.Web.Models;

public sealed record BagItem(
    uint BagGuid,
    string BagName,
    byte Slot,
    uint ItemGuid,
    uint ItemEntry,
    string? Name,
    byte Quality,
    ushort ItemLevel,
    uint Count);
