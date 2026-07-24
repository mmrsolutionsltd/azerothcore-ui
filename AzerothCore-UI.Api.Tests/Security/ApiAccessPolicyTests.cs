using System.Net;
using AzerothCore_UI.Api.Security;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Security;

public sealed class ApiAccessPolicyTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void MatchingServiceKey_AllowsRemoteRequest() =>
        Assert.True(ApiAccessPolicy.IsAuthorized(
            IPAddress.Parse("192.0.2.10"), ValidKey, ValidKey, false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public void MissingOrIncorrectServiceKey_DeniesRemoteRequest(string? supplied) =>
        Assert.False(ApiAccessPolicy.IsAuthorized(
            IPAddress.Parse("192.0.2.10"), supplied, ValidKey, false));

    [Fact]
    public void DevelopmentFallback_AllowsLoopbackOnly()
    {
        Assert.True(ApiAccessPolicy.IsAuthorized(
            IPAddress.Loopback, null, null, true));
        Assert.False(ApiAccessPolicy.IsAuthorized(
            IPAddress.Parse("192.0.2.10"), null, null, true));
    }

    [Fact]
    public void ProductionKeyValidation_RejectsShortKey() =>
        Assert.Throws<InvalidOperationException>(
            () => ApiAccessPolicy.ValidateProductionKey("too-short"));
}
