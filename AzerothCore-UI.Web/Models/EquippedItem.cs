namespace AzerothCore_UI.Web.Models;

public sealed record EquippedItem(
    byte Slot,
    uint ItemGuid,
    uint ItemEntry,
    string? Name,
    byte Quality,
    ushort ItemLevel,
    uint Count,
    ushort Durability,
    ushort MaxDurability);
