using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class GatheringAbundanceServiceTests
{
    [Theory]
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(175)]
    [InlineData(500)]
    public void ValidatePercentage_AcceptsFivePercentSteps(int value) =>
        GatheringAbundanceService.ValidatePercentage(value, "Herbs");

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(101)]
    [InlineData(501)]
    public void ValidatePercentage_RejectsUnsafeOrPartialSteps(int value) =>
        Assert.Throws<ArgumentException>(() =>
            GatheringAbundanceService.ValidatePercentage(value, "Herbs"));
}
