using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public static class OperationsAlertEvaluator
{
    public static IReadOnlyList<OperationsAlert> Evaluate(
        IReadOnlyList<OperationsServiceStatus> services,
        OperationsMachineStatus machine,
        OperationsBackupStatus backup,
        OperationsPublicEndpointStatus publicEndpoint,
        OperationsAlertSettings settings)
    {
        var alerts = new List<OperationsAlert>();
        if (settings.NotifyServiceDown)
        {
            alerts.AddRange(services
                .Where(service => service.Status.Equals("Error", StringComparison.OrdinalIgnoreCase))
                .Select(service => new OperationsAlert(
                    $"service:{service.Name.ToLowerInvariant().Replace(' ', '-')}",
                    "Error", $"{service.Name} is unavailable", service.Detail, "/server")));
        }
        if (settings.NotifyBackupOverdue && backup.Overdue)
            alerts.Add(new("backup:overdue", "Error", "Database backup is overdue",
                backup.LastError ?? "The scheduled backup has not completed successfully.",
                "/database-backups"));
        else if (settings.NotifyBackupOverdue && backup.LastSuccessUtc is null)
            alerts.Add(new("backup:missing", "Warning", "No successful database backup",
                "Create and verify a complete AzerothCore database backup.",
                "/database-backups"));
        if (settings.NotifyLowDiskSpace
            && machine.DiskFreePercent < settings.MinimumDiskFreePercent)
            alerts.Add(new("machine:disk", machine.DiskFreePercent < 5 ? "Error" : "Warning",
                "Server disk space is low",
                $"Only {machine.DiskFreePercent}% of the monitored disk is free.",
                "/diagnostics"));
        if (settings.NotifyCertificateExpiry
            && publicEndpoint.CertificateDaysRemaining is { } days
            && days <= settings.CertificateWarningDays)
            alerts.Add(new("public:certificate", days < 1 ? "Error" : "Warning",
                "HTTPS certificate needs attention",
                days < 1 ? "The public certificate has expired."
                    : $"The public certificate expires in {days} day(s).",
                "/security-dashboard"));
        if (settings.NotifyCertificateExpiry && !publicEndpoint.HttpsReachable)
            alerts.Add(new("public:https", "Error", "Public website is unreachable",
                publicEndpoint.Detail, "/security-dashboard"));
        if (settings.NotifyDdnsMismatch && publicEndpoint.DdnsMatches == false)
            alerts.Add(new("public:ddns", "Error", "Dynamic DNS does not match",
                $"Public IP {publicEndpoint.PublicIp ?? "unknown"}; "
                + $"DNS resolves to {string.Join(", ", publicEndpoint.ResolvedAddresses)}.",
                "/security-dashboard"));
        return alerts;
    }
}
