namespace AzerothCore_UI.Web.Models;

public sealed record OperationsDashboard(
    DateTime GeneratedAtUtc,
    string OverallStatus,
    string Hostname,
    TimeSpan Uptime,
    IReadOnlyList<OperationsServiceStatus> Services,
    ServerPopulation Population,
    OperationsMachineStatus Machine,
    OperationsBackupStatus Backup,
    OperationsPublicEndpointStatus PublicEndpoint,
    int FailedAdministrationActions24Hours,
    int RecentServerErrorLines,
    OperationsAlertSettings AlertSettings,
    bool EmailConfigured,
    string EmailStatus,
    IReadOnlyList<OperationsAlert> Alerts,
    IReadOnlyList<OperationsNotification> RecentNotifications);

public sealed record OperationsServiceStatus(
    string Name, string Status, string Detail, DateTime? StartedAtUtc = null);

public sealed record OperationsMachineStatus(
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    long DiskTotalBytes,
    long DiskFreeBytes,
    double? CpuTemperatureCelsius)
{
    public int MemoryUsedPercent => TotalMemoryBytes <= 0 ? 0
        : Math.Clamp((int)Math.Round(
            (TotalMemoryBytes - AvailableMemoryBytes) * 100d / TotalMemoryBytes), 0, 100);
    public int DiskUsedPercent => DiskTotalBytes <= 0 ? 0
        : Math.Clamp((int)Math.Round(
            (DiskTotalBytes - DiskFreeBytes) * 100d / DiskTotalBytes), 0, 100);
    public int DiskFreePercent => 100 - DiskUsedPercent;
}

public sealed record OperationsBackupStatus(
    bool ScheduleEnabled,
    DateTime? LastSuccessUtc,
    DateTime? NextRunUtc,
    bool Overdue,
    string? LastError,
    int BackupCount);

public sealed record OperationsPublicEndpointStatus(
    string Hostname,
    string? PublicIp,
    IReadOnlyList<string> ResolvedAddresses,
    bool? DdnsMatches,
    bool HttpsReachable,
    DateTime? CertificateExpiresAtUtc,
    int? CertificateDaysRemaining,
    string Detail);

public sealed class OperationsAlertSettings
{
    public bool Enabled { get; set; }
    public string EmailRecipient { get; set; } = "";
    public bool NotifyServiceDown { get; set; } = true;
    public bool NotifyBackupOverdue { get; set; } = true;
    public bool NotifyLowDiskSpace { get; set; } = true;
    public int MinimumDiskFreePercent { get; set; } = 15;
    public bool NotifyCertificateExpiry { get; set; } = true;
    public int CertificateWarningDays { get; set; } = 14;
    public bool NotifyDdnsMismatch { get; set; } = true;
}

public sealed record OperationsAlert(
    string Key, string Severity, string Title, string Message, string? Link = null);

public sealed record OperationsNotification(
    DateTime OccurredAtUtc, string Outcome, string Subject, string Detail);
