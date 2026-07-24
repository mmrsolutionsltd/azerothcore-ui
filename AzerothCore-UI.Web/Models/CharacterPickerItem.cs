namespace AzerothCore_UI.Web.Models;

public sealed record CharacterPickerItem(
    string Value,
    string Name,
    string Detail,
    bool Online,
    bool IsPlayerBot = false);
