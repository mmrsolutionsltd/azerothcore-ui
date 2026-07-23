using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class DiagnosticsReportBuilderTests
{
    [Theory]
    [InlineData("Server=localhost;User ID=root;Password=secret;Database=acore_world")]
    [InlineData("password: hunter2 token=abc123")]
    [InlineData("Pwd=my-password;Uid=administrator")]
    public void Redact_RemovesCredentialValues(string input)
    {
        var result = DiagnosticsReportBuilder.Redact(input);

        Assert.Contains("<redacted>", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("my-password", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("administrator", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_GroupsChecksAndRedactsDetailsAndLogs()
    {
        var dashboard = new DiagnosticsDashboard(
            new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc),
            [new("Database", "MySQL", "Error", "Password=secret", "token=abc")],
            [new("Errors.log", "Database", 2, "Pwd=hidden")]);

        var report = DiagnosticsReportBuilder.Build(dashboard);

        Assert.Contains("[Database]", report);
        Assert.Contains("[Recent error groups]", report);
        Assert.DoesNotContain("secret", report);
        Assert.DoesNotContain("abc", report);
        Assert.DoesNotContain("hidden", report);
    }
}
