namespace AzerothCore_UI.Web.Models;

public sealed record DatabaseBackupSummary(
    string BackupId,
    DateTime CreatedAtUtc,
    long TotalBytes,
    bool Verified,
    bool ServersWereStopped,
    IReadOnlyList<DatabaseBackupFile> Files);

public sealed record DatabaseBackupFile(
    string Database,
    string FileName,
    long Bytes,
    string Sha256);

public sealed record CreateDatabaseBackupRequest(bool Confirmed);
public sealed record RestoreDatabaseBackupRequest(string BackupId, bool Confirmed);
public sealed record DatabaseBackupSchedule(
    bool Enabled,
    string Frequency,
    string LocalTime,
    DayOfWeek DayOfWeek,
    bool OnlyWhenServersStopped,
    int RetentionCount);
public sealed record DatabaseBackupActivity(
    DateTime OccurredAtUtc, string Outcome, string Message, string? BackupId = null);
public sealed record DatabaseBackupDashboard(
    DatabaseBackupSchedule Schedule,
    DateTime? NextRunUtc,
    DateTime? LastAttemptUtc,
    DateTime? LastSuccessUtc,
    bool Overdue,
    string? LastError,
    int BackupCount,
    long TotalBytes,
    IReadOnlyList<DatabaseBackupActivity> Activity);
