using System.Diagnostics;
using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;
using MySqlConnector;

namespace AzerothCore_UI.Api.Services;

public sealed class AzerothCoreDiagnosticsService(
    IConfiguration configuration,
    AzerothCoreConnectionFactory connectionFactory,
    AzerothCoreServerManager serverManager,
    AzerothCoreConfigurationManager configurationManager)
{
    private readonly string serverRoot = configuration["AzerothCore:Server:RootPath"] ?? @"C:\AzerothServer-PlayerBots";
    private readonly string configurationRoot = configuration["AzerothCore:Server:ConfigurationPath"]
        ?? Path.Combine(configuration["AzerothCore:Server:RootPath"]
            ?? @"C:\AzerothServer-PlayerBots", "configs");
    private readonly string logRoot = configuration["AzerothCore:Server:LogPath"]
        ?? configuration["AzerothCore:Server:RootPath"]
        ?? @"C:\AzerothServer-PlayerBots";
    private readonly string backupRoot = configuration["AzerothCore:Backups:RootPath"]
        ?? Path.Combine(configuration["AzerothCore:Server:RootPath"]
            ?? @"C:\AzerothServer-PlayerBots", "backups", "database");
    private readonly string sourceRoot = configuration["AzerothCore:Diagnostics:SourcePath"] ?? @"C:\AzerothCore-PlayerBots";
    private readonly string buildRoot = configuration["AzerothCore:Diagnostics:BuildPath"] ?? @"C:\AzerothBuild-PlayerBots";
    private readonly string clientRoot = configuration["AzerothCore:Diagnostics:ClientPath"] ?? @"C:\TheraWoW wotlk";

    public async Task<DiagnosticsDashboard> GetAsync(CancellationToken cancellationToken)
    {
        var checks = new List<DiagnosticCheck>();
        ServerStatus? status = null;
        try
        {
            status = await serverManager.GetStatusAsync(cancellationToken);
            AddProcessChecks(checks, status);
        }
        catch (Exception exception)
        {
            checks.Add(Error("Processes", "Server status", exception.Message));
        }

        await AddDatabaseChecksAsync(checks, cancellationToken);
        AddExecutableChecks(checks);
        AddSourceChecks(checks);
        AddModuleChecks(checks);
        AddAracChecks(checks);
        AddBackupChecks(checks);
        AddConfigurationChecks(checks, status);
        var errors = ReadLogGroups();
        checks.Add(new("Logs", "Recent server errors",
            errors.Count == 0 ? "Healthy" : "Warning",
            errors.Count == 0 ? "No recent error patterns found." : $"{errors.Sum(group => group.Count)} recent matching log lines in {errors.Count} groups."));
        return new(DateTime.UtcNow, checks, errors);
    }

    private static void AddProcessChecks(List<DiagnosticCheck> checks, ServerStatus status)
    {
        foreach (var process in new[] { status.WorldServer, status.AuthServer })
            checks.Add(new("Processes", process.Name,
                process.IsRunning ? "Healthy" : "Warning",
                process.IsRunning
                    ? $"Running (PID {process.ProcessId}, {FormatBytes(process.WorkingSetBytes)})"
                    : "Not running.",
                process.StartedAt is null ? null : $"Started {process.StartedAt:O}",
                process.StartedAt));
        checks.Add(new("Connectivity", "SOAP",
            status.SoapReachable ? "Healthy" : status.SoapConfigured ? "Warning" : "Error",
            status.SoapReachable ? "Configured and reachable."
                : status.SoapConfigured ? "Configured but not reachable." : "Not configured."));
    }

    private async Task AddDatabaseChecksAsync(List<DiagnosticCheck> checks, CancellationToken token)
    {
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(token);
            var builder = new MySqlConnectionStringBuilder(connection.ConnectionString);
            checks.Add(new("Database", "MySQL connection", "Healthy",
                $"Connected to {builder.Server}:{builder.Port}.",
                $"Default database: {builder.Database}"));
            foreach (var database in new[] { "acore_auth", "acore_characters", "acore_world" })
            {
                try
                {
                    var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                        $"SELECT COUNT(*) FROM {database}.updates;", cancellationToken: token));
                    var pending = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                        $"SELECT COUNT(*) FROM {database}.updates WHERE state = 'PENDING';", cancellationToken: token));
                    checks.Add(new("Database", database, pending == 0 ? "Healthy" : "Warning",
                        $"{count:N0} recorded updates; {pending} pending."));
                }
                catch (Exception exception) { checks.Add(Error("Database", database, exception.Message)); }
            }
            var version = await connection.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
                "SELECT core_version AS CoreVersion, core_revision AS CoreRevision, db_version AS DatabaseVersion FROM acore_world.version LIMIT 1;",
                cancellationToken: token));
            if (version is not null)
                checks.Add(new("Database", "AzerothCore revision", "Healthy",
                    version.CoreVersion, $"Core {version.CoreRevision}; database {version.DatabaseVersion}"));
            var totemColumns = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'acore_world' AND table_name = 'player_totem_model'
                  AND column_name IN ('TotemID', 'RaceID', 'ModelID');
                """, cancellationToken: token));
            checks.Add(new("mod-arac", "World database schema", totemColumns == 3 ? "Healthy" : "Warning",
                totemColumns == 3 ? "Compatible player_totem_model schema detected." : "Expected mod-arac-compatible totem columns were not all found."));
        }
        catch (Exception exception)
        {
            checks.Add(Error("Database", "MySQL connection", exception.Message));
        }
    }

    private void AddExecutableChecks(List<DiagnosticCheck> checks)
    {
        foreach (var processName in new[] { "worldserver", "authserver" })
        {
            var name = ExecutableName(processName);
            var path = Path.Combine(serverRoot, name);
            if (!File.Exists(path)) { checks.Add(Error("Binaries", name, $"Missing from {serverRoot}.")); continue; }
            var file = new FileInfo(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            checks.Add(new("Binaries", name, "Healthy",
                $"{FormatBytes(file.Length)}; version {version.FileVersion ?? "not embedded"}.",
                path, file.LastWriteTimeUtc));
        }
    }

    private void AddSourceChecks(List<DiagnosticCheck> checks)
    {
        var binaryPath = Path.Combine(serverRoot, ExecutableName("worldserver"));
        if (!Directory.Exists(sourceRoot) || !File.Exists(binaryPath))
        {
            checks.Add(new("Build", "Source versus worldserver", "Warning",
                "Source tree or deployed worldserver was not found.", $"Source: {sourceRoot}; binary: {binaryPath}"));
            return;
        }
        try
        {
            var newest = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            var binary = new FileInfo(binaryPath);
            var rebuild = newest is not null && newest.LastWriteTimeUtc > binary.LastWriteTimeUtc;
            checks.Add(new("Build", "Source versus worldserver", rebuild ? "Warning" : "Healthy",
                rebuild ? "C++ source is newer than the deployed worldserver; rebuild and deploy it."
                    : "The deployed worldserver is newer than the latest C++ source file.",
                newest is null ? null : $"Newest source: {newest.FullName}; build tree: {buildRoot}",
                newest?.LastWriteTimeUtc));
        }
        catch (Exception exception) { checks.Add(Error("Build", "Source versus worldserver", exception.Message)); }
    }

    private void AddModuleChecks(List<DiagnosticCheck> checks)
    {
        var modules = new[]
        {
            ("mod-ah-bot", "mod_ahbot.conf"), ("mod-aoe-loot", "mod_aoe_loot.conf"),
            ("mod-autobalance", "AutoBalance.conf"), ("mod-playerbots", "playerbots.conf"),
            ("mod-transmog", "transmog.conf"), ("mod-web-admin", (string?)null)
        };
        foreach (var (module, config) in modules)
        {
            var sourceExists = Directory.Exists(Path.Combine(sourceRoot, "modules", module));
            var configExists = config is null || File.Exists(Path.Combine(configurationRoot, "modules", config));
            checks.Add(new("Modules", module,
                sourceExists && configExists ? "Healthy" : "Warning",
                sourceExists && configExists ? "Source and required configuration found."
                    : $"Source: {(sourceExists ? "found" : "missing")}; configuration: {(configExists ? "found" : "missing")}."));
        }
    }

    private void AddAracChecks(List<DiagnosticCheck> checks)
    {
        var files = new[]
        {
            (Path.Combine(clientRoot, "Data", "Patch-A.MPQ"), "Client Patch-A.MPQ"),
            (Path.Combine(serverRoot, "data", "dbc", "CharBaseInfo.dbc"), "Server CharBaseInfo.dbc"),
            (Path.Combine(serverRoot, "data", "dbc", "CharStartOutfit.dbc"), "Server CharStartOutfit.dbc"),
            (Path.Combine(serverRoot, "data", "dbc", "SkillRaceClassInfo.dbc"), "Server SkillRaceClassInfo.dbc")
        };
        foreach (var (path, name) in files)
        {
            var file = new FileInfo(path);
            checks.Add(new("mod-arac", name, file.Exists && file.Length > 0 ? "Healthy" : "Error",
                file.Exists ? $"{FormatBytes(file.Length)} found." : "Missing.",
                path, file.Exists ? file.LastWriteTimeUtc : null));
        }
    }

    private void AddBackupChecks(List<DiagnosticCheck> checks)
    {
        var directory = backupRoot;
        var newest = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        var age = newest is null ? (TimeSpan?)null : DateTime.UtcNow - newest.LastWriteTimeUtc;
        checks.Add(new("Backups", "Database backup",
            newest is null ? "Error" : age <= TimeSpan.FromDays(7) ? "Healthy" : "Warning",
            newest is null ? "No SQL backup found." : $"{newest.Name}; {FormatBytes(newest.Length)}; {FormatAge(age!.Value)} old.",
            newest?.FullName, newest?.LastWriteTimeUtc));

        var configRoot = configurationRoot;
        var configBackup = Directory.Exists(configRoot)
            ? Directory.EnumerateFiles(configRoot, "*.bak", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        checks.Add(new("Backups", "Configuration backup",
            configBackup is null ? "Warning" : "Healthy",
            configBackup is null ? "No configuration backup found." : $"{configBackup.Name}; {FormatAge(DateTime.UtcNow - configBackup.LastWriteTimeUtc)} old.",
            configBackup?.FullName, configBackup?.LastWriteTimeUtc));
    }

    private void AddConfigurationChecks(List<DiagnosticCheck> checks, ServerStatus? status)
    {
        var paths = new[]
        {
            Path.Combine(configurationRoot, "worldserver.conf"),
            Path.Combine(configurationRoot, "authserver.conf"),
            Path.Combine(configurationRoot, "modules", "playerbots.conf")
        };
        foreach (var path in paths)
            checks.Add(new("Configuration", Path.GetFileName(path), File.Exists(path) ? "Healthy" : "Error",
                File.Exists(path) ? "Configuration file found." : "Configuration file missing.", path));
        try
        {
            var bots = configurationManager.GetPlayerBotSettings();
            var valid = bots.MinRandomBots >= 0 && bots.MaxRandomBots <= 5000
                && bots.MinRandomBots <= bots.MaxRandomBots
                && bots.MinLevel >= 1 && bots.MaxLevel <= 80 && bots.MinLevel <= bots.MaxLevel;
            checks.Add(new("Configuration", "PlayerBots ranges", valid ? "Healthy" : "Error",
                valid
                    ? $"Population {bots.MinRandomBots:N0}–{bots.MaxRandomBots:N0}; levels {bots.MinLevel}–{bots.MaxLevel}."
                    : "PlayerBots population or level ranges are invalid."));
        }
        catch (Exception exception) { checks.Add(Error("Configuration", "PlayerBots ranges", exception.Message)); }

        if (status?.WorldServer.StartedAt is { } startedAt)
        {
            var newer = Directory.EnumerateFiles(configurationRoot, "*.conf", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.LastWriteTime > startedAt)
                .OrderByDescending(file => file.LastWriteTime)
                .ToArray();
            checks.Add(new("Configuration", "Restart requirement", newer.Length == 0 ? "Healthy" : "Warning",
                newer.Length == 0 ? "No configuration files are newer than the running worldserver."
                    : $"{newer.Length} configuration file(s) changed after worldserver start; restart may be required.",
                newer.FirstOrDefault()?.FullName, newer.FirstOrDefault()?.LastWriteTimeUtc));
        }
    }

    private IReadOnlyList<DiagnosticLogGroup> ReadLogGroups()
    {
        var rows = new List<(string Source, string Category, string Text)>();
        foreach (var name in new[] { "Errors.log", "Server.log", "Auth.log", "Playerbots.log" })
        {
            var path = Path.Combine(logRoot, name);
            if (!File.Exists(path)) continue;
            try
            {
                foreach (var line in File.ReadLines(path).TakeLast(2000)
                    .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("exception", StringComparison.OrdinalIgnoreCase)))
                {
                    var category = line.Contains("database", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("sql", StringComparison.OrdinalIgnoreCase) ? "Database"
                        : line.Contains("soap", StringComparison.OrdinalIgnoreCase) ? "SOAP"
                        : line.Contains("playerbot", StringComparison.OrdinalIgnoreCase) ? "PlayerBots"
                        : "General";
                    rows.Add((name, category, DiagnosticsReportBuilder.Redact(line.Length > 300 ? line[..300] : line)));
                }
            }
            catch (IOException) { }
        }
        return rows.GroupBy(row => new { row.Source, row.Category })
            .Select(group => new DiagnosticLogGroup(
                group.Key.Source, group.Key.Category, group.Count(), group.Last().Text))
            .OrderByDescending(group => group.Count).ToArray();
    }

    private static DiagnosticCheck Error(string category, string name, string message) =>
        new(category, name, "Error", DiagnosticsReportBuilder.Redact(message));
    private static string FormatBytes(long? bytes) => bytes is null ? "memory unavailable"
        : bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824d:N1} GB"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576d:N1} MB"
        : $"{bytes / 1024d:N1} KB";
    private static string FormatAge(TimeSpan age) => age.TotalDays >= 1
        ? $"{(int)age.TotalDays}d {age.Hours}h" : $"{(int)age.TotalHours}h {age.Minutes}m";
    private static string ExecutableName(string name) =>
        OperatingSystem.IsWindows() ? $"{name}.exe" : name;

    private sealed class VersionRow
    {
        public string CoreVersion { get; init; } = "";
        public string? CoreRevision { get; init; }
        public string? DatabaseVersion { get; init; }
    }
}
