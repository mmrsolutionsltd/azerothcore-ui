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

public sealed record GrantProfessionTrainingRequest(
    uint CharacterGuid,
    uint SpellId,
    bool Confirmed);

public sealed record ProfessionStarterCharacter(
    uint CharacterGuid,
    string CharacterName,
    byte CharacterLevel,
    bool Online,
    int PrimaryProfessionCount,
    IReadOnlyList<AvailableProfession> AvailableProfessions);

public sealed record AvailableProfession(
    ushort SkillId,
    string Name,
    string Category,
    uint ApprenticeSpellId,
    byte RequiredLevel);

public sealed record LearnProfessionRequest(
    uint CharacterGuid,
    ushort SkillId,
    bool Confirmed);

public sealed record ProfessionManagementCharacter(
    uint CharacterGuid,
    string CharacterName,
    bool Online,
    IReadOnlyList<ManagedProfession> Professions);

public sealed record ManagedProfession(
    ushort SkillId,
    string Name,
    string Category,
    ushort CurrentSkill,
    ushort MaximumSkill);

public sealed record UnlearnProfessionRequest(
    uint CharacterGuid,
    ushort SkillId,
    bool Confirmed);
