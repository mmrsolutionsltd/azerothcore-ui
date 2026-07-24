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
