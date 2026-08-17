using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public sealed class OperationsAlertWorker(
    OperationsDashboardService dashboardService,
    OperationsAlertStore alertStore,
    OperationsEmailSender emailSender,
    IConfiguration configuration,
    ILogger<OperationsAlertWorker> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromMinutes(Math.Clamp(
        configuration.GetValue("Operations:AlertIntervalMinutes", 5), 1, 60));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var timer = new PeriodicTimer(interval);
        do
        {
            try { await CheckAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Operations alert check failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task CheckAsync(CancellationToken token)
    {
        var settings = alertStore.GetSettings();
        if (!settings.Enabled || !emailSender.IsConfigured) return;

        var dashboard = await dashboardService.GetAsync(token);
        var currentAlerts = dashboard.Alerts.Where(alert => ShouldNotify(alert, settings)).ToArray();
        var currentKeys = currentAlerts.Select(alert => alert.Key)
            .ToHashSet(StringComparer.Ordinal);
        var previousKeys = alertStore.GetActiveAlertKeys();
        var newAlerts = currentAlerts.Where(alert => !previousKeys.Contains(alert.Key)).ToArray();
        var recoveredKeys = previousKeys.Where(key => !currentKeys.Contains(key)).ToArray();
        if (newAlerts.Length == 0 && recoveredKeys.Length == 0)
        {
            alertStore.RecordMonitorResult(currentKeys);
            return;
        }

        var subject = newAlerts.Length > 0
            ? $"AzerothCore alert: {newAlerts[0].Title}"
            : "AzerothCore recovery notification";
        var body = BuildMessage(dashboard, newAlerts, recoveredKeys);
        try
        {
            await emailSender.SendAsync(settings.EmailRecipient, subject, body, token);
            alertStore.RecordMonitorResult(currentKeys, new(
                DateTime.UtcNow, "Sent", subject,
                $"Sent to {settings.EmailRecipient}."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not send operations alert email.");
            alertStore.RecordMonitorResult(currentKeys, new(
                DateTime.UtcNow, "Failed", subject,
                DiagnosticsReportBuilder.Redact(exception.Message)));
        }
    }

    internal static bool ShouldNotify(OperationsAlert alert, OperationsAlertSettings settings) =>
        alert.Key.StartsWith("service:", StringComparison.Ordinal) ? settings.NotifyServiceDown
        : alert.Key.StartsWith("backup:", StringComparison.Ordinal) ? settings.NotifyBackupOverdue
        : alert.Key == "machine:disk" ? settings.NotifyLowDiskSpace
        : alert.Key == "public:ddns" ? settings.NotifyDdnsMismatch
        : alert.Key.StartsWith("public:", StringComparison.Ordinal) ? settings.NotifyCertificateExpiry
        : true;

    private static string BuildMessage(
        OperationsDashboard dashboard,
        IReadOnlyList<OperationsAlert> newAlerts,
        IReadOnlyList<string> recoveredKeys)
    {
        var lines = new List<string>
        {
            $"AzerothCore operations status: {dashboard.OverallStatus}",
            $"Host: {dashboard.Hostname}",
            $"Checked: {dashboard.GeneratedAtUtc:O}",
            ""
        };
        if (newAlerts.Count > 0)
        {
            lines.Add("New alerts:");
            lines.AddRange(newAlerts.Select(alert =>
                $"- [{alert.Severity}] {alert.Title}: {alert.Message}"));
        }
        if (recoveredKeys.Count > 0)
        {
            lines.Add("");
            lines.Add("Recovered alerts:");
            lines.AddRange(recoveredKeys.Select(key => $"- {key}"));
        }
        lines.Add("");
        lines.Add($"Dashboard: https://{dashboard.PublicEndpoint.Hostname}/operations");
        return string.Join(Environment.NewLine, lines);
    }
}
