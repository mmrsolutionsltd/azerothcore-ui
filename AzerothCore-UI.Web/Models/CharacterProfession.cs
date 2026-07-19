namespace AzerothCore_UI.Web.Models;

public sealed record CharacterProfession(
    ushort SkillId,
    string Name,
    string Category,
    ushort Value,
    ushort Maximum);
