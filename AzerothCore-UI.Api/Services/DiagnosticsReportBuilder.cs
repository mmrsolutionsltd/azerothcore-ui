using System.Text;
using System.Text.RegularExpressions;
using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public static partial class DiagnosticsReportBuilder
{
    public static string Build(DiagnosticsDashboard dashboard)
    {
        var output = new StringBuilder()
            .AppendLine("AzerothCore UI diagnostic report")
            .AppendLine($"Generated (UTC): {dashboard.GeneratedAtUtc:O}")
            .AppendLine();
        foreach (var category in dashboard.Checks.GroupBy(check => check.Category))
        {
            output.AppendLine($"[{category.Key}]");
            foreach (var check in category)
            {
                output.Append($"- {check.Status}: {check.Name} — {Redact(check.Summary)}");
                if (check.Timestamp is not null) output.Append($" ({check.Timestamp:O})");
                output.AppendLine();
                if (!string.IsNullOrWhiteSpace(check.Detail))
                    output.AppendLine($"  {Redact(check.Detail)}");
            }
            output.AppendLine();
        }
        output.AppendLine("[Recent error groups]");
        foreach (var group in dashboard.RecentErrors)
            output.AppendLine($"- {group.Source}/{group.Category}: {group.Count} — {Redact(group.LatestSample)}");
        return output.ToString();
    }

    public static string Redact(string value)
    {
        var redacted = ConnectionSecretRegex().Replace(value, "$1<redacted>");
        return PasswordAssignmentRegex().Replace(redacted, "$1<redacted>");
    }

    [GeneratedRegex(@"(?i)\b(Password|Pwd|User ID|Uid)\s*=\s*[^;\s]+")]
    private static partial Regex ConnectionSecretRegex();

    [GeneratedRegex(@"(?i)\b(password|secret|token)\s*[:=]\s*[^\s;,]+")]
    private static partial Regex PasswordAssignmentRegex();
}
