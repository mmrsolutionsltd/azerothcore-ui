namespace AzerothCore_UI.Api.Models;

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
