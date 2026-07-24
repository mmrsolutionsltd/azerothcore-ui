using AzerothCore_UI.Api.Data;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Data;

public sealed class ProfessionPairingGuideTests
{
    [Theory]
    [InlineData(171, 182)]
    [InlineData(165, 393)]
    [InlineData(202, 186)]
    [InlineData(773, 182)]
    public void GetPairings_ReturnsExpectedGatheringOrCraftingPartner(
        ushort skillId,
        ushort expectedPairing)
    {
        Assert.Contains(
            ProfessionPairingGuide.GetPairings(skillId),
            profession => profession.SkillId == expectedPairing);
    }

    [Fact]
    public void GetPairings_ReturnsEmptyForSecondaryProfession()
    {
        Assert.Empty(ProfessionPairingGuide.GetPairings(185));
    }
}
