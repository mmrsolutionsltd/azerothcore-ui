using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class DungeonQuestEligibilityRulesTests
{
    [Fact]
    public void MaskAllows_ZeroMask_AllowsAnyValidValue()
    {
        Assert.True(DungeonQuestEligibilityRules.MaskAllows(0, 10));
    }

    [Fact]
    public void MaskAllows_SetBit_AllowsMatchingRaceOrClass()
    {
        const uint bloodElfMask = 1u << (10 - 1);

        Assert.True(DungeonQuestEligibilityRules.MaskAllows(bloodElfMask, 10));
        Assert.False(DungeonQuestEligibilityRules.MaskAllows(bloodElfMask, 11));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void MaskAllows_RestrictedMask_RejectsOutOfRangeValues(byte value)
    {
        Assert.False(DungeonQuestEligibilityRules.MaskAllows(uint.MaxValue, value));
    }
}
