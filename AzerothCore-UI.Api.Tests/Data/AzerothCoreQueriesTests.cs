using AzerothCore_UI.Api.Data;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Data;

public sealed class AzerothCoreQueriesTests
{
    [Fact]
    public void CharacterLearnedSpells_UsesCurrentAzerothCoreSchema()
    {
        var query = AzerothCoreQueries.CharacterLearnedSpells;

        Assert.Contains("SELECT spell", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guid = @CharacterGuid", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("active", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled", query, StringComparison.OrdinalIgnoreCase);
    }
}
