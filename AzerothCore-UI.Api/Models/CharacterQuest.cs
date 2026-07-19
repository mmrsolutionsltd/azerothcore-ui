namespace AzerothCore_UI.Api.Models;

public sealed record CharacterQuest(
    uint QuestId,
    string Title,
    byte Status);

public sealed record CompletedCharacterQuest(
    uint QuestId,
    string? Title,
    bool Active);
