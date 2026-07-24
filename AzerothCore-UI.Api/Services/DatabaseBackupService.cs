using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AzerothCore_UI.Api.Models;
using MySqlConnector;

namespace AzerothCore_UI.Api.Services;

public sealed class DatabaseBackupService(
    IConfiguration configuration,
    ILogger<DatabaseBackupService> logger)
{
    private static readonly string[] Databases = ["acore_auth", "acore_characters", "acore_world"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string backupRoot = Path.GetFullPath(Path.Combine(
        configuration["AzerothCore:Server:RootPath"] ?? @"C:\AzerothServer-PlayerBots",
        "backups", "database"));
    private readonly int retentionCount = Math.Max(
        1, configuration.GetValue("AzerothCore:Backups:RetentionCount", 20));
    private readonly string connectionString =
        configuration.GetConnectionString("AzerothCoreMaintenance")
        ?? throw new InvalidOperationException(
            "Connection string 'AzerothCoreMaintenance' is not configured in API user-secrets.");

    public IReadOnlyList<DatabaseBackupSummary> GetBackups()
    {
        if (!Directory.Exists(backupRoot))
            return [];

        return Directory.EnumerateDirectories(backupRoot)
            .Select(TryReadManifest)
            .Where(backup => backup is not null)
            .Cast<DatabaseBackupSummary>()
            .OrderByDescending(backup => backup.CreatedAtUtc)
            .ToArray();
    }

    public async Task<DatabaseBackupSummary> CreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupRoot);
        var backupId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var directory = ResolveBackupDirectory(backupId);
        Directory.CreateDirectory(directory);
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var files = new List<DatabaseBackupFile>();

        try
        {
            foreach (var database in Databases)
            {
                var fileName = $"{database}.sql";
                var path = Path.Combine(directory, fileName);
                await DumpDatabaseAsync(builder, database, path, cancellationToken);
                var file = new FileInfo(path);
                if (!file.Exists || file.Length < 100)
                    throw new InvalidOperationException($"{database} backup was unexpectedly empty.");
                files.Add(new(database, fileName, file.Length, await HashAsync(path, cancellationToken)));
            }

            var summary = new DatabaseBackupSummary(
                backupId,
                DateTime.UtcNow,
                files.Sum(file => file.Bytes),
                true,
                !ServersRunning(),
                files);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "manifest.json"),
                JsonSerializer.Serialize(summary, JsonOptions),
                cancellationToken);
            ApplyRetention();
            logger.LogWarning(
                "ADMIN AUDIT: Verified database backup {BackupId} created ({Bytes} bytes).",
                summary.BackupId, summary.TotalBytes);
            return summary;
        }
        catch
        {
            File.WriteAllText(Path.Combine(directory, "FAILED.txt"),
                $"Backup failed at {DateTime.UtcNow:O}.");
            throw;
        }
    }

    public async Task RestoreAsync(string backupId, CancellationToken cancellationToken)
    {
        if (ServersRunning())
            throw new InvalidOperationException(
                "Both worldserver and authserver must be stopped before restoring a database backup.");

        var directory = ResolveBackupDirectory(backupId);
        var backup = TryReadManifest(directory)
            ?? throw new FileNotFoundException("The selected database backup was not found.");
        await VerifyAsync(directory, backup, cancellationToken);

        // Always create a verified recovery point immediately before a restore.
        var safetyBackup = await CreateAsync(cancellationToken);
        var builder = new MySqlConnectionStringBuilder(connectionString);
        foreach (var database in Databases)
        {
            var file = backup.Files.SingleOrDefault(item => item.Database == database)
                ?? throw new InvalidOperationException($"The backup is missing {database}.");
            await RestoreDatabaseAsync(
                builder, database, Path.Combine(directory, file.FileName), cancellationToken);
        }

        logger.LogCritical(
            "ADMIN AUDIT: Database backup {BackupId} restored. Pre-restore safety backup: {SafetyBackupId}.",
            backupId, safetyBackup.BackupId);
    }

    private async Task VerifyAsync(
        string directory,
        DatabaseBackupSummary backup,
        CancellationToken cancellationToken)
    {
        if (!backup.Verified || backup.Files.Count != Databases.Length)
            throw new InvalidOperationException("The selected backup is not marked as verified.");
        foreach (var file in backup.Files)
        {
            var path = Path.GetFullPath(Path.Combine(directory, file.FileName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path)
                || new FileInfo(path).Length != file.Bytes
                || !string.Equals(await HashAsync(path, cancellationToken), file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Backup verification failed for {file.Database}.");
        }
    }

    private async Task DumpDatabaseAsync(
        MySqlConnectionStringBuilder builder,
        string database,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateMySqlProcess(
            "mysqldump.exe",
            builder,
            ["--single-transaction", "--quick", "--routines", "--events", "--triggers", "--no-tablespaces",
             "--hex-blob", "--set-gtid-purged=OFF", "--column-statistics=0",
             "--default-character-set=utf8mb4", database]);
        startInfo.RedirectStandardOutput = true;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start mysqldump.");
        await using (var output = new FileStream(
            outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mysqldump failed for {database}: {error.Trim()}");
    }

    private async Task RestoreDatabaseAsync(
        MySqlConnectionStringBuilder builder,
        string database,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateMySqlProcess(
            "mysql.exe", builder, ["--default-character-set=utf8mb4", database]);
        startInfo.RedirectStandardInput = true;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start mysql.");
        await using (var input = new FileStream(
            inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
        process.StandardInput.Close();
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Restore failed for {database}: {error.Trim()}");
    }

    private static ProcessStartInfo CreateMySqlProcess(
        string executable,
        MySqlConnectionStringBuilder builder,
        IEnumerable<string> arguments)
    {
        var path = ResolveExecutable(executable);
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add($"--host={builder.Server}");
        startInfo.ArgumentList.Add($"--port={builder.Port}");
        startInfo.ArgumentList.Add($"--user={builder.UserID}");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["MYSQL_PWD"] = builder.Password;
        return startInfo;
    }

    private static string ResolveExecutable(string name)
    {
        var configured = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "MySQL", "MySQL Server 8.0", "bin", name);
        return File.Exists(configured) ? configured : name;
    }

    private string ResolveBackupDirectory(string backupId)
    {
        if (backupId.Length is < 15 or > 40
            || backupId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Invalid backup identifier.", nameof(backupId));
        var directory = Path.GetFullPath(Path.Combine(backupRoot, backupId));
        if (!directory.StartsWith(backupRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The backup path is outside the configured backup directory.");
        return directory;
    }

    private static DatabaseBackupSummary? TryReadManifest(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<DatabaseBackupSummary>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ApplyRetention()
    {
        foreach (var backup in GetBackups().Skip(retentionCount))
        {
            var directory = ResolveBackupDirectory(backup.BackupId);
            Directory.Delete(directory, true);
        }
    }

    private static bool ServersRunning() =>
        Process.GetProcessesByName("worldserver").Length > 0
        || Process.GetProcessesByName("authserver").Length > 0;

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
