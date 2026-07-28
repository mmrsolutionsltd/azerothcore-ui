using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class DungeonGuideCatalogTests
{
    [Fact]
    public void FindsCuratedGuideByDifficultySuffixedName()
    {
        var guide = DungeonGuideCatalog.Find("The Deadmines (Normal)");

        Assert.Contains("pirate ship", guide.Overview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adds promptly",
            DungeonGuideCatalog.Tactics(guide, "Edwin VanCleef"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownDungeonGetsUsefulFallback()
    {
        var guide = DungeonGuideCatalog.Find("An entirely custom dungeon");

        Assert.NotEmpty(guide.Route);
        Assert.NotEmpty(guide.Notes);
        Assert.Contains("tank",
            DungeonGuideCatalog.Tactics(guide, "Custom boss"),
            StringComparison.OrdinalIgnoreCase);
    }
}
