namespace AzerothCore_UI.Api.Models;

public sealed record CharacterProfession(
    ushort SkillId,
    string Name,
    string Category,
    ushort Value,
    ushort Maximum);
