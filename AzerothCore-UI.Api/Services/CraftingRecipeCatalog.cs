using System.Buffers.Binary;

namespace AzerothCore_UI.Api.Services;

public sealed class CraftingRecipeCatalog
{
    private const int SpellIdField = 0;
    private const int SpellReagentField = 52;
    private const int SpellReagentCountField = 60;
    private const int SpellEffectItemField = 107;
    private const int SkillLineField = 1;
    private const int SkillSpellField = 2;
    private const int SkillMinimumRankField = 7;

    private static readonly HashSet<ushort> EquipmentProfessionSkills =
    [164, 165, 171, 197, 202, 333, 755, 773];

    private readonly Lazy<CatalogData> data;

    public CraftingRecipeCatalog(IConfiguration configuration)
    {
        data = new Lazy<CatalogData>(() => Load(configuration), true);
    }

    internal CraftingRecipeCatalog(IReadOnlyList<CraftingRecipeDefinition> recipes)
    {
        data = new Lazy<CatalogData>(() => new CatalogData(
            recipes, "Test crafting catalog"), true);
    }

    public IReadOnlyList<CraftingRecipeDefinition> Recipes => data.Value.Recipes;
    public string DataSource => data.Value.DataSource;

    private static CatalogData Load(IConfiguration configuration)
    {
        var dbcDirectory = ResolveDbcDirectory(configuration);
        var spellPath = Path.Combine(dbcDirectory, "Spell.dbc");
        var skillPath = Path.Combine(dbcDirectory, "SkillLineAbility.dbc");
        var spells = ReadSpellCraftingData(spellPath);
        var recipes = new List<CraftingRecipeDefinition>();

        foreach (var record in ReadRecords(skillPath, 14))
        {
            var skillId = checked((ushort)ReadUInt32(record, SkillLineField));
            if (!EquipmentProfessionSkills.Contains(skillId))
                continue;

            var spellId = ReadUInt32(record, SkillSpellField);
            if (!spells.TryGetValue(spellId, out var spell))
                continue;

            var requiredSkill = checked((ushort)Math.Min(
                ReadUInt32(record, SkillMinimumRankField), ushort.MaxValue));
            foreach (var outputItemId in spell.OutputItemIds)
            {
                recipes.Add(new CraftingRecipeDefinition(
                    spellId, skillId, requiredSkill, outputItemId,
                    spell.Reagents));
            }
        }

        return new CatalogData(
            recipes
                .DistinctBy(recipe => (recipe.SpellId, recipe.SkillId,
                    recipe.OutputItemId))
                .OrderBy(recipe => recipe.SkillId)
                .ThenBy(recipe => recipe.RequiredSkill)
                .ThenBy(recipe => recipe.SpellId)
                .ToArray(),
            "AzerothCore 3.3.5a DBC files");
    }

    private static Dictionary<uint, SpellCraftingData> ReadSpellCraftingData(
        string path)
    {
        var result = new Dictionary<uint, SpellCraftingData>();
        foreach (var record in ReadRecords(path, 234))
        {
            var outputs = Enumerable.Range(0, 3)
                .Select(index => ReadUInt32(record, SpellEffectItemField + index))
                .Where(itemId => itemId != 0)
                .Distinct()
                .ToArray();
            if (outputs.Length == 0)
                continue;

            var reagents = new List<CraftingReagentDefinition>();
            for (var index = 0; index < 8; index++)
            {
                var itemId = ReadInt32(record, SpellReagentField + index);
                var count = ReadInt32(record, SpellReagentCountField + index);
                if (itemId > 0 && count > 0)
                    reagents.Add(new CraftingReagentDefinition(
                        checked((uint)itemId), count));
            }

            var spellId = ReadUInt32(record, SpellIdField);
            result[spellId] = new SpellCraftingData(outputs, reagents);
        }
        return result;
    }

    private static IEnumerable<byte[]> ReadRecords(string path, int minimumFields)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"The required AzerothCore DBC file was not found: {path}", path);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var magic = new string(reader.ReadChars(4));
        if (magic != "WDBC")
            throw new InvalidDataException($"{path} is not a WDBC file.");

        var recordCount = reader.ReadUInt32();
        var fieldCount = reader.ReadUInt32();
        var recordSize = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // String block size.
        if (fieldCount < minimumFields || recordSize < minimumFields * 4)
            throw new InvalidDataException(
                $"{path} has an unsupported record layout ({fieldCount} fields, {recordSize} bytes)." );

        for (var index = 0u; index < recordCount; index++)
        {
            var record = reader.ReadBytes(checked((int)recordSize));
            if (record.Length != recordSize)
                throw new EndOfStreamException(
                    $"{path} ended before record {index + 1} of {recordCount}.");
            yield return record;
        }
    }

    private static uint ReadUInt32(byte[] record, int field) =>
        BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(field * 4, 4));

    private static int ReadInt32(byte[] record, int field) =>
        BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(field * 4, 4));

    private static string ResolveDbcDirectory(IConfiguration configuration)
    {
        var configuredData = configuration["AzerothCore:Server:DataPath"];
        var root = configuration["AzerothCore:Server:RootPath"]
            ?? (OperatingSystem.IsWindows()
                ? @"C:\AzerothServer-PlayerBots"
                : "/opt/azerothcore/server/bin");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredData))
            candidates.Add(configuredData);
        candidates.AddRange([
            Path.Combine(root, "data"),
            Path.Combine(root, "Data"),
            Path.Combine(root, "data", "dbc"),
            Path.Combine(root, "Data", "dbc"),
            Path.Combine(root, "..", "data"),
            Path.Combine(root, "..", "Data")
        ]);

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct())
        {
            var directory = Path.GetFileName(candidate)
                .Equals("dbc", StringComparison.OrdinalIgnoreCase)
                ? candidate : Path.Combine(candidate, "dbc");
            if (File.Exists(Path.Combine(directory, "Spell.dbc"))
                && File.Exists(Path.Combine(directory, "SkillLineAbility.dbc")))
                return directory;
        }

        throw new DirectoryNotFoundException(
            "AzerothCore Spell.dbc and SkillLineAbility.dbc were not found. " +
            "Configure AzerothCore:Server:DataPath to the server data directory.");
    }

    private sealed record CatalogData(
        IReadOnlyList<CraftingRecipeDefinition> Recipes, string DataSource);
    private sealed record SpellCraftingData(
        IReadOnlyList<uint> OutputItemIds,
        IReadOnlyList<CraftingReagentDefinition> Reagents);
}

public sealed record CraftingRecipeDefinition(
    uint SpellId, ushort SkillId, ushort RequiredSkill,
    uint OutputItemId, IReadOnlyList<CraftingReagentDefinition> Reagents);

public sealed record CraftingReagentDefinition(uint ItemId, int Quantity);
