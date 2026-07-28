using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class SecurityDashboardServiceTests
{
    [Theory]
    [InlineData("/.env")]
    [InlineData("/backend/.env.production")]
    [InlineData("/.git/config")]
    [InlineData("/wp-admin/setup.php")]
    [InlineData("/phpinfo.php")]
    [InlineData("/.aws/credentials")]
    public void RecognisesCommonPublicProbePaths(string path)
    {
        Assert.True(SecurityDashboardService.IsSuspiciousPath(path));
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/player-actions")]
    [InlineData("/activity-audit")]
    [InlineData("/favicon.ico")]
    public void DoesNotFlagNormalApplicationPaths(string path)
    {
        Assert.False(SecurityDashboardService.IsSuspiciousPath(path));
    }
}
