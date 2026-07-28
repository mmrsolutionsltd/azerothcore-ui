using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class DungeonItemStatCatalogTests
{
    [Fact]
    public void MapsPrimaryRatingsAndResistances()
    {
        var stats = DungeonItemStatCatalog.Create(
            [(4, 8), (7, 12), (32, 5), (0, 40)],
            0, 3, 0, 0, 4, 0);

        Assert.Contains(stats, stat => stat.Name == "Strength" && stat.Value == 8);
        Assert.Contains(stats, stat => stat.Name == "Stamina" && stat.Value == 12);
        Assert.Contains(stats, stat => stat.Name == "Critical strike rating"
            && stat.Value == 5 && stat.Rating);
        Assert.Contains(stats, stat => stat.Name == "Mana" && stat.Value == 40);
        Assert.Contains(stats, stat => stat.Name == "Fire resistance" && stat.Value == 3);
        Assert.Contains(stats, stat => stat.Name == "Shadow resistance" && stat.Value == 4);
    }
}
