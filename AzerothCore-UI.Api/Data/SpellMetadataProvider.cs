using System.Text.Json;

namespace AzerothCore_UI.Api.Data;

public sealed class SpellMetadataProvider
{
    private const string ResourceName =
        "AzerothCore_UI.Api.Data.SpellMetadata.spell-metadata.json";

    private readonly IReadOnlyDictionary<uint, SpellMetadata> metadataById;

    public SpellMetadataProvider()
    {
        using var stream = typeof(SpellMetadataProvider).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded spell metadata resource '{ResourceName}' was not found.");

        var rows = JsonSerializer.Deserialize<SpellMetadataRow[]>(stream)
            ?? throw new InvalidOperationException("The embedded spell metadata could not be read.");

        metadataById = rows.ToDictionary(
            row => row.Id,
            row => new SpellMetadata(row.Name, row.Rank, row.LearnedSpellId));
    }

    public SpellMetadata? Find(uint spellId) =>
        metadataById.GetValueOrDefault(spellId);

    private sealed record SpellMetadataRow(
        uint Id,
        string Name,
        string? Rank,
        uint? LearnedSpellId);
}

public sealed record SpellMetadata(string Name, string? Rank, uint? LearnedSpellId);
