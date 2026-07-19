namespace AzerothCore_UI.Api.Models;

public sealed record AccountWithCharacters(
    uint AccountId,
    string Username,
    string Classification,
    DateTime? LastLogin,
    IReadOnlyList<CharacterSummary> Characters);

public sealed record CharacterSummary(
    uint Guid,
    string Name,
    byte Level,
    byte Race,
    byte Class,
    bool Online,
    string LocationName);
