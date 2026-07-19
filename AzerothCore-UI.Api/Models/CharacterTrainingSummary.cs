namespace AzerothCore_UI.Api.Models;

public sealed record CharacterTrainingSummary(
    uint AccountId,
    string Username,
    uint CharacterGuid,
    string CharacterName,
    byte CharacterLevel,
    IReadOnlyList<TrainingRequirement> Requirements);

public sealed record TrainingRequirement(
    string Category,
    string Discipline,
    uint SpellId,
    string? Name,
    string? Rank,
    byte RequiredLevel,
    ushort? RequiredSkillRank,
    uint TrainingCost);
