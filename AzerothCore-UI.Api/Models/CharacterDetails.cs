namespace AzerothCore_UI.Api.Models;

public sealed record CharacterDetails(
    uint Guid,
    uint AccountId,
    string Username,
    string Name,
    byte Level,
    byte Race,
    byte Class,
    bool Online,
    uint Money,
    uint TotalTime,
    ushort Map,
    ushort Zone,
    string LocationName);

public sealed record CharacterOverviewSummary(
    uint Guid,
    string Username,
    string Name,
    byte Level,
    byte Race,
    byte Class,
    bool Online,
    uint Money,
    uint TotalTime,
    ushort Map,
    ushort Zone,
    string LocationName,
    int ActiveQuestCount,
    int ProfessionCount,
    string? PetName,
    ushort? HomebindMap,
    ushort? HomebindZone);
