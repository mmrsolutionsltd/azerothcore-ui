namespace AzerothCore_UI.Api.Models;

public sealed record MissingClassSpell(
    uint SpellId,
    string? Name,
    string? Rank,
    byte RequiredLevel,
    uint TrainingCost);
