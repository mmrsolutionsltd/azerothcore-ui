using System.Text.Json;
using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public sealed class DatabaseBackupScheduler(
    IConfiguration configuration,
    DatabaseBackupService backupService,
    ILogger<DatabaseBackupScheduler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly string statePath = Path.Combine(
        configuration["AzerothCore:Backups:RootPath"]
        ?? Path.Combine(
            configuration["AzerothCore:Server:RootPath"] ?? @"C:\AzerothServer-PlayerBots",
            "backups", "database"),
        "schedule.json");
    private SchedulerState state = new();

    public DatabaseBackupDashboard GetDashboard()
    {
        Load();
        var schedule = state.Schedule;
        var now = DateTime.Now;
        var backups = backupService.GetBackups();
        var lastSuccess = state.LastSuccessUtc ?? backups.FirstOrDefault()?.CreatedAtUtc;
        DateTime? next = schedule.Enabled
            ? DatabaseBackupScheduleCalculator.NextLocalOccurrence(schedule, now).ToUniversalTime()
            : null;
        var due = schedule.Enabled
            ? DatabaseBackupScheduleCalculator.MostRecentLocalOccurrence(schedule, now).ToUniversalTime()
            : (DateTime?)null;
        return new(schedule, next, state.LastAttemptUtc, lastSuccess,
            due.HasValue && (!lastSuccess.HasValue || lastSuccess < due),
            state.LastError, backups.Count, backups.Sum(backup => backup.TotalBytes),
            state.Activity.OrderByDescending(item => item.OccurredAtUtc).Take(30).ToArray());
    }

    public DatabaseBackupDashboard UpdateSchedule(DatabaseBackupSchedule schedule)
    {
        Validate(schedule);
        Load();
        state.Schedule = schedule with
        {
            Frequency = schedule.Frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase)
                ? "Weekly" : "Daily",
            LocalTime = DatabaseBackupScheduleCalculator.ParseTime(schedule.LocalTime).ToString("HH:mm"),
            RetentionCount = Math.Clamp(schedule.RetentionCount, 1, 100)
        };
        AddActivity("Configuration", schedule.Enabled
            ? $"Automatic {state.Schedule.Frequency.ToLowerInvariant()} backups enabled."
            : "Automatic backups disabled.");
        Save();
        backupService.ApplyRetention(state.Schedule.RetentionCount);
        return GetDashboard();
    }

    public async Task<DatabaseBackupSummary> RunNowAsync(
        string source, CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            Load();
            state.LastAttemptUtc = DateTime.UtcNow;
            AddActivity("Started", $"{source} database backup started.");
            Save();
            try
            {
                var backup = await backupService.CreateAsync(cancellationToken);
                state.LastSuccessUtc = backup.CreatedAtUtc;
                state.LastError = null;
                AddActivity("Succeeded", $"{source} backup completed.", backup.BackupId);
                backupService.ApplyRetention(state.Schedule.RetentionCount);
                Save();
                return backup;
            }
            catch (Exception exception)
            {
                state.LastError = exception.Message;
                AddActivity("Failed", $"{source} backup failed: {exception.Message}");
                Save();
                throw;
            }
        }
        finally { operationGate.Release(); }
    }

    public async Task CheckScheduleAsync(CancellationToken cancellationToken)
    {
        Load();
        if (!state.Schedule.Enabled || operationGate.CurrentCount == 0) return;
        var dueUtc = DatabaseBackupScheduleCalculator
            .MostRecentLocalOccurrence(state.Schedule, DateTime.Now).ToUniversalTime();
        var lastSuccess = state.LastSuccessUtc
            ?? backupService.GetBackups().FirstOrDefault()?.CreatedAtUtc;
        if (lastSuccess >= dueUtc || state.LastAttemptUtc >= dueUtc) return;
        if (state.Schedule.OnlyWhenServersStopped && backupService.ServersRunning())
        {
            if (!state.LastDeferredUtc.HasValue
                || DateTime.UtcNow - state.LastDeferredUtc >= TimeSpan.FromMinutes(30))
            {
                state.LastDeferredUtc = DateTime.UtcNow;
                AddActivity("Deferred", "Scheduled backup deferred because an AzerothCore server is running.");
                Save();
            }
            return;
        }
        try { await RunNowAsync("Scheduled", cancellationToken); }
        catch (Exception exception) { logger.LogError(exception, "Scheduled database backup failed."); }
    }

    private static void Validate(DatabaseBackupSchedule schedule)
    {
        if (!schedule.Frequency.Equals("Daily", StringComparison.OrdinalIgnoreCase)
            && !schedule.Frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Frequency must be Daily or Weekly.");
        DatabaseBackupScheduleCalculator.ParseTime(schedule.LocalTime);
        if (schedule.RetentionCount is < 1 or > 100)
            throw new ArgumentException("Retention count must be between 1 and 100.");
    }

    private void Load()
    {
        if (!File.Exists(statePath)) return;
        try
        {
            state = JsonSerializer.Deserialize<SchedulerState>(
                File.ReadAllText(statePath), JsonOptions) ?? new();
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Could not read database backup schedule state.");
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var temporaryPath = statePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, statePath, true);
    }

    private void AddActivity(string outcome, string message, string? backupId = null)
    {
        state.Activity.Add(new(DateTime.UtcNow, outcome, message, backupId));
        if (state.Activity.Count > 100)
            state.Activity.RemoveRange(0, state.Activity.Count - 100);
    }

    private sealed class SchedulerState
    {
        public DatabaseBackupSchedule Schedule { get; set; } =
            new(false, "Daily", "03:00", DayOfWeek.Sunday, true, 20);
        public DateTime? LastAttemptUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        public DateTime? LastDeferredUtc { get; set; }
        public string? LastError { get; set; }
        public List<DatabaseBackupActivity> Activity { get; set; } = [];
    }
}
