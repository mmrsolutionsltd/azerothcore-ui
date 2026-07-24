using AzerothCore_UI.Api.Data;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Data;

public sealed class ProfessionCatalogTests
{
    [Fact]
    public void CanLearn_AllowsFirstOrSecondPrimaryProfession()
    {
        IReadOnlySet<ushort> knownSkills = new HashSet<ushort> { 171 };

        Assert.True(ProfessionCatalog.CanLearn(
            ProfessionCatalog.All[164], 5, knownSkills));
    }

    [Fact]
    public void CanLearn_RejectsThirdPrimaryProfession()
    {
        IReadOnlySet<ushort> knownSkills = new HashSet<ushort> { 171, 182 };

        Assert.False(ProfessionCatalog.CanLearn(
            ProfessionCatalog.All[164], 5, knownSkills));
    }

    [Fact]
    public void CanLearn_AllowsSecondaryProfessionWithTwoPrimaries()
    {
        IReadOnlySet<ushort> knownSkills = new HashSet<ushort> { 171, 182 };

        Assert.True(ProfessionCatalog.CanLearn(
            ProfessionCatalog.All[185], 5, knownSkills));
    }

    [Fact]
    public void CanLearn_RejectsKnownProfession()
    {
        IReadOnlySet<ushort> knownSkills = new HashSet<ushort> { 171 };

        Assert.False(ProfessionCatalog.CanLearn(
            ProfessionCatalog.All[171], 80, knownSkills));
    }

    [Fact]
    public void CanLearn_EnforcesMinimumLevel()
    {
        IReadOnlySet<ushort> knownSkills = new HashSet<ushort>();

        Assert.False(ProfessionCatalog.CanLearn(
            ProfessionCatalog.All[164], 4, knownSkills));
    }
}
