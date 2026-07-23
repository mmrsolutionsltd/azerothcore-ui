using System.Text;
using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class Diagnostics
{
    private DiagnosticsDashboard? dashboard;
    private string category = "all", statusFilter = "all";
    private string? message, reportDataUri, reportFileName;
    private bool isLoading = true, isGeneratingReport;
    private IReadOnlyList<string> Categories =>
        dashboard?.Checks.Select(check => check.Category).Distinct().OrderBy(value => value).ToArray() ?? [];
    private IReadOnlyList<DiagnosticCheck> FilteredChecks =>
        dashboard?.Checks.Where(check =>
            (category == "all" || check.Category == category)
            && (statusFilter == "all" || check.Status == statusFilter)).ToArray() ?? [];

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        isLoading = true;
        message = null;
        reportDataUri = null;
        try { dashboard = await AccountsClient.GetDiagnosticsAsync(); }
        catch (Exception exception) { message = exception.Message; }
        finally { isLoading = false; }
    }

    private int Count(string status) => dashboard?.Checks.Count(check => check.Status == status) ?? 0;

    private async Task GenerateReportAsync()
    {
        isGeneratingReport = true;
        try
        {
            var report = await AccountsClient.GetDiagnosticsReportAsync();
            reportDataUri = $"data:text/plain;charset=utf-8;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(report))}";
            reportFileName = $"azerothcore-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        }
        catch (Exception exception) { message = exception.Message; }
        finally { isGeneratingReport = false; }
    }

    private static string StatusBadge(string status) => status switch
    {
        "Healthy" => "text-bg-success", "Warning" => "text-bg-warning",
        "Error" => "text-bg-danger", _ => "text-bg-secondary"
    };
}
