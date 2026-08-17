using System.Text.Json;
using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

public sealed class OperationsAlertStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly object gate = new();
    private readonly string statePath;
    private State state = new();

    public OperationsAlertStore(IConfiguration configuration)
    {
        statePath = configuration["Operations:StatePath"]
            ?? Path.Combine(
                configuration["AzerothCore:Backups:RootPath"]
                    ?? Path.Combine(
                        configuration["AzerothCore:Server:RootPath"]
                            ?? @"C:\AzerothServer-PlayerBots",
                        "backups", "database"),
                "operations-dashboard.json");
        Load();
    }

    public OperationsAlertSettings GetSettings()
    {
        lock (gate) return Clone(state.Settings);
    }

    public OperationsAlertSettings UpdateSettings(OperationsAlertSettings settings)
    {
        Validate(settings);
        lock (gate)
        {
            state.Settings = Clone(settings);
            Save();
            return Clone(state.Settings);
        }
    }

    public IReadOnlyList<OperationsNotification> GetNotifications()
    {
        lock (gate)
            return state.Notifications.OrderByDescending(item => item.OccurredAtUtc)
                .Take(20).ToArray();
    }

    public HashSet<string> GetActiveAlertKeys()
    {
        lock (gate) return state.ActiveAlertKeys.ToHashSet(StringComparer.Ordinal);
    }

    public void RecordMonitorResult(
        IEnumerable<string> activeAlertKeys,
        OperationsNotification? notification = null)
    {
        lock (gate)
        {
            state.ActiveAlertKeys = activeAlertKeys.Distinct(StringComparer.Ordinal).ToList();
            if (notification is not null)
            {
                state.Notifications.Add(notification);
                if (state.Notifications.Count > 100)
                    state.Notifications.RemoveRange(0, state.Notifications.Count - 100);
            }
            Save();
        }
    }

    internal static void Validate(OperationsAlertSettings settings)
    {
        if (settings.MinimumDiskFreePercent is < 5 or > 50)
            throw new ArgumentException("Minimum free disk space must be between 5% and 50%.");
        if (settings.CertificateWarningDays is < 1 or > 90)
            throw new ArgumentException("Certificate warning must be between 1 and 90 days.");
        if (settings.Enabled && string.IsNullOrWhiteSpace(settings.EmailRecipient))
            throw new ArgumentException("Enter the email address that should receive alerts.");
        if (!string.IsNullOrWhiteSpace(settings.EmailRecipient)
            && !System.Net.Mail.MailAddress.TryCreate(settings.EmailRecipient, out _))
            throw new ArgumentException("The alert email address is not valid.");
    }

    private void Load()
    {
        lock (gate)
        {
            if (!File.Exists(statePath)) return;
            try
            {
                state = JsonSerializer.Deserialize<State>(
                    File.ReadAllText(statePath), JsonOptions) ?? new();
            }
            catch (JsonException)
            {
                state = new();
            }
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var temporaryPath = statePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, statePath, true);
    }

    private static OperationsAlertSettings Clone(OperationsAlertSettings value) => new()
    {
        Enabled = value.Enabled,
        EmailRecipient = value.EmailRecipient.Trim(),
        NotifyServiceDown = value.NotifyServiceDown,
        NotifyBackupOverdue = value.NotifyBackupOverdue,
        NotifyLowDiskSpace = value.NotifyLowDiskSpace,
        MinimumDiskFreePercent = value.MinimumDiskFreePercent,
        NotifyCertificateExpiry = value.NotifyCertificateExpiry,
        CertificateWarningDays = value.CertificateWarningDays,
        NotifyDdnsMismatch = value.NotifyDdnsMismatch
    };

    private sealed class State
    {
        public OperationsAlertSettings Settings { get; set; } = new();
        public List<string> ActiveAlertKeys { get; set; } = [];
        public List<OperationsNotification> Notifications { get; set; } = [];
    }
}
