using AzerothCore_UI.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class CraftingRecipeCatalogTests
{
    [Fact]
    public void ReadsCraftedEquipmentAndReagentsFromAzerothCoreDbcFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"crafting-dbc-{Guid.NewGuid():N}");
        var dbc = Path.Combine(root, "dbc");
        Directory.CreateDirectory(dbc);
        try
        {
            WriteDbc(Path.Combine(dbc, "Spell.dbc"), 234, record =>
            {
                WriteField(record, 0, 3755);   // Linen Bag recipe spell.
                WriteField(record, 52, 2996); // Bolt of Linen Cloth.
                WriteField(record, 60, 6);
                WriteField(record, 107, 4238); // Linen Bag.
            });
            WriteDbc(Path.Combine(dbc, "SkillLineAbility.dbc"), 14, record =>
            {
                WriteField(record, 1, 197);  // Tailoring.
                WriteField(record, 2, 3755);
                WriteField(record, 7, 45);
            });
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzerothCore:Server:DataPath"] = root
                }).Build();

            var recipe = Assert.Single(new CraftingRecipeCatalog(configuration).Recipes);

            Assert.Equal((uint)3755, recipe.SpellId);
            Assert.Equal((ushort)197, recipe.SkillId);
            Assert.Equal((ushort)45, recipe.RequiredSkill);
            Assert.Equal((uint)4238, recipe.OutputItemId);
            var reagent = Assert.Single(recipe.Reagents);
            Assert.Equal((uint)2996, reagent.ItemId);
            Assert.Equal(6, reagent.Quantity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteDbc(
        string path, int fieldCount, Action<byte[]> populate)
    {
        var record = new byte[fieldCount * 4];
        populate(record);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("WDBC"u8.ToArray());
        writer.Write((uint)1);
        writer.Write((uint)fieldCount);
        writer.Write((uint)record.Length);
        writer.Write((uint)0);
        writer.Write(record);
    }

    private static void WriteField(byte[] record, int field, uint value) =>
        BitConverter.GetBytes(value).CopyTo(record, field * 4);
}
