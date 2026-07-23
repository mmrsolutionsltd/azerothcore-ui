namespace AzerothCore_UI.Web.Models;

public sealed record DiagnosticCheck(
    string Category, string Name, string Status, string Summary,
    string? Detail = null, DateTime? Timestamp = null);

public sealed record DiagnosticLogGroup(
    string Source, string Category, int Count, string LatestSample);

public sealed record DiagnosticsDashboard(
    DateTime GeneratedAtUtc,
    IReadOnlyList<DiagnosticCheck> Checks,
    IReadOnlyList<DiagnosticLogGroup> RecentErrors);
