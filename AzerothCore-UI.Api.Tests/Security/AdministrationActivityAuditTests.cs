using AzerothCore_UI.Api.Security;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Security;

public sealed class AdministrationActivityAuditTests
{
    [Fact]
    public void RedactsSensitivePropertiesAtEveryLevel()
    {
        var result = AdministrationActivityAudit.SanitizeJsonForAudit(
            """{"playerName":"Hundead","password":"bad","nested":{"apiKey":"secret","itemId":4245}}""");

        Assert.Contains("\"playerName\":\"Hundead\"", result);
        Assert.Contains("\"itemId\":4245", result);
        Assert.DoesNotContain("\"bad\"", result);
        Assert.DoesNotContain("\"secret\"", result);
        Assert.Equal(2, result.Split("[REDACTED]").Length - 1);
    }

    [Fact]
    public void RedactsPasswordVariants()
    {
        var result = AdministrationActivityAudit.SanitizeJsonForAudit(
            """{"currentPassword":"old","newPassword":"new","securityStamp":"stamp"}""");

        Assert.DoesNotContain("\"old\"", result);
        Assert.DoesNotContain("\"new\"", result);
        Assert.DoesNotContain("\"stamp\"", result);
        Assert.Equal(3, result.Split("[REDACTED]").Length - 1);
    }
}
