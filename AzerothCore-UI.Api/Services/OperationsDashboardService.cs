using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Services;

public sealed class OperationsDashboardService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AzerothCoreConnectionFactory connectionFactory,
    AzerothCoreServerManager serverManager,
    DatabaseBackupScheduler backupScheduler,
    OperationsAlertStore alertStore,
    OperationsEmailSender emailSender)
{
    private readonly string publicHostname = configuration["Operations:PublicHostname"]
        ?? "azerothcore.ddnsfree.com";
    private readonly string publicIpUrl = configuration["Operations:PublicIpUrl"]
        ?? "https://api.ipify.org";
    private readonly string monitoredPath = configuration["AzerothCore:Server:RootPath"]
        ?? AppContext.BaseDirectory;

    public async Task<OperationsDashboard> GetAsync(CancellationToken cancellationToken)
    {
        var services = new List<OperationsServiceStatus>();
        var population = new ServerPopulation();
        var recentServerErrors = 0;
        try
        {
            var server = await serverManager.GetStatusAsync(cancellationToken);
            services.Add(ProcessService("Worldserver", server.WorldServer));
            services.Add(ProcessService("Authserver", server.AuthServer));
            services.Add(new("SOAP", server.SoapReachable ? "Healthy" : "Error",
                server.SoapReachable ? "Worldserver administration is reachable."
                    : server.WorldStatus ?? "Worldserver administration is unavailable."));
            population = server.Population;
            recentServerErrors = server.RecentLogs.Count(entry =>
                entry.Message.Contains("error", StringComparison.OrdinalIgnoreCase)
                || entry.Message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || entry.Message.Contains("exception", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            services.Add(new("AzerothCore status", "Error", SafeMessage(exception)));
        }

        var databaseStopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT 1;", cancellationToken: cancellationToken));
            databaseStopwatch.Stop();
            services.Add(new("MySQL", "Healthy",
                $"Connected in {databaseStopwatch.ElapsedMilliseconds:N0} ms."));
        }
        catch (Exception exception)
        {
            services.Add(new("MySQL", "Error", SafeMessage(exception)));
        }

        var failedActions = await GetFailedAdministrationActionsAsync(cancellationToken);
        var machine = ReadMachineStatus();
        var backup = ReadBackupStatus();
        var publicEndpoint = await ReadPublicEndpointAsync(cancellationToken);
        var settings = alertStore.GetSettings();
        var alerts = OperationsAlertEvaluator.Evaluate(
            services, machine, backup, publicEndpoint, AllChecksEnabled(settings));
        var overallStatus = alerts.Any(alert => alert.Severity == "Error") ? "Error"
            : alerts.Count > 0 ? "Warning" : "Healthy";

        return new(
            DateTime.UtcNow,
            overallStatus,
            Environment.MachineName,
            ReadUptime(),
            services,
            population,
            machine,
            backup,
            publicEndpoint,
            failedActions,
            recentServerErrors,
            settings,
            emailSender.IsConfigured,
            emailSender.Status,
            alerts,
            alertStore.GetNotifications());
    }

    public OperationsAlertSettings UpdateAlertSettings(OperationsAlertSettings settings)
    {
        if (settings.Enabled && !emailSender.IsConfigured)
            throw new InvalidOperationException(
                "Email alerts cannot be enabled until SMTP delivery is configured on the server.");
        return alertStore.UpdateSettings(settings);
    }

    internal static OperationsAlertSettings AllChecksEnabled(OperationsAlertSettings settings) => new()
    {
        Enabled = settings.Enabled,
        EmailRecipient = settings.EmailRecipient,
        NotifyServiceDown = true,
        NotifyBackupOverdue = true,
        NotifyLowDiskSpace = true,
        MinimumDiskFreePercent = settings.MinimumDiskFreePercent,
        NotifyCertificateExpiry = true,
        CertificateWarningDays = settings.CertificateWarningDays,
        NotifyDdnsMismatch = true
    };

    private async Task<int> GetFailedAdministrationActionsAsync(CancellationToken token)
    {
        try
        {
            await using var connection = connectionFactory.CreateAdministrationConnection();
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM admin_audit_log
                WHERE occurred_at_utc >= @Since
                  AND outcome NOT IN ('Succeeded', 'Success');
                """, new { Since = DateTime.UtcNow.AddHours(-24) }, cancellationToken: token));
        }
        catch
        {
            return 0;
        }
    }

    private OperationsBackupStatus ReadBackupStatus()
    {
        try
        {
            var value = backupScheduler.GetDashboard();
            return new(value.Schedule.Enabled, value.LastSuccessUtc, value.NextRunUtc,
                value.Overdue, value.LastError, value.BackupCount);
        }
        catch (Exception exception)
        {
            return new(false, null, null, true, SafeMessage(exception), 0);
        }
    }

    private OperationsMachineStatus ReadMachineStatus()
    {
        var (totalMemory, availableMemory) = ReadMemory();
        long totalDisk = 0;
        long freeDisk = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(monitoredPath));
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                totalDisk = drive.TotalSize;
                freeDisk = drive.AvailableFreeSpace;
            }
        }
        catch { }
        return new(totalMemory, availableMemory, totalDisk, freeDisk, ReadCpuTemperature());
    }

    private async Task<OperationsPublicEndpointStatus> ReadPublicEndpointAsync(
        CancellationToken token)
    {
        var addresses = Array.Empty<string>();
        string? publicIp = null;
        bool httpsReachable = false;
        DateTime? expiresAt = null;
        var details = new List<string>();
        try
        {
            addresses = (await Dns.GetHostAddressesAsync(publicHostname, token))
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString()).Distinct().ToArray();
        }
        catch (Exception exception) { details.Add($"DNS: {SafeMessage(exception)}"); }

        try
        {
            var client = httpClientFactory.CreateClient("Operations");
            publicIp = (await client.GetStringAsync(publicIpUrl, token)).Trim();
            if (!IPAddress.TryParse(publicIp, out _)) publicIp = null;
        }
        catch (Exception exception) { details.Add($"Public IP: {SafeMessage(exception)}"); }

        try
        {
            var client = httpClientFactory.CreateClient("Operations");
            using var response = await client.GetAsync(
                $"https://{publicHostname}/health/ready", token);
            httpsReachable = response.IsSuccessStatusCode;
            if (!httpsReachable) details.Add($"HTTPS returned {(int)response.StatusCode}.");
        }
        catch (Exception exception) { details.Add($"HTTPS: {SafeMessage(exception)}"); }

        try
        {
            expiresAt = await ReadCertificateExpiryAsync(publicHostname, token);
        }
        catch (Exception exception) { details.Add($"Certificate: {SafeMessage(exception)}"); }

        bool? matches = publicIp is null || addresses.Length == 0
            ? null : addresses.Contains(publicIp, StringComparer.OrdinalIgnoreCase);
        var daysRemaining = expiresAt.HasValue
            ? (int)Math.Floor((expiresAt.Value - DateTime.UtcNow).TotalDays)
            : (int?)null;
        if (details.Count == 0)
            details.Add(httpsReachable ? "DNS, public IP and HTTPS checks succeeded."
                : "The public HTTPS endpoint did not return a healthy response.");
        return new(publicHostname, publicIp, addresses, matches, httpsReachable,
            expiresAt, daysRemaining, string.Join(" ", details));
    }

    private static async Task<DateTime?> ReadCertificateExpiryAsync(
        string hostname, CancellationToken token)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(hostname, 443, token);
        using var ssl = new SslStream(tcp.GetStream(), false,
            static (_, certificate, _, errors) => certificate is not null
                && errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = hostname,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, token);
        if (ssl.RemoteCertificate is null) return null;
        using var certificate = new X509Certificate2(ssl.RemoteCertificate);
        return certificate.NotAfter.ToUniversalTime();
    }

    private static (long Total, long Available) ReadMemory()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => ParseKilobytes(parts[1]));
            return (values.GetValueOrDefault("MemTotal"),
                values.GetValueOrDefault("MemAvailable"));
        }
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var available = Math.Max(0, total - Environment.WorkingSet);
        return (total, available);
    }

    private static long ParseKilobytes(string value)
    {
        var number = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return long.TryParse(number, out var kilobytes) ? kilobytes * 1024 : 0;
    }

    private static double? ReadCpuTemperature()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/sys/class/hwmon")) return null;
        try
        {
            return Directory.EnumerateFiles("/sys/class/hwmon", "temp*_input",
                    SearchOption.AllDirectories)
                .Select(path => double.TryParse(File.ReadAllText(path).Trim(), out var value)
                    ? value / 1000d : double.NaN)
                .Where(value => value is >= 0 and <= 125)
                .DefaultIfEmpty(double.NaN).Max() is var maximum && !double.IsNaN(maximum)
                    ? maximum : null;
        }
        catch { return null; }
    }

    private static TimeSpan ReadUptime()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/uptime"))
        {
            var first = File.ReadAllText("/proc/uptime")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (double.TryParse(first, System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    private static OperationsServiceStatus ProcessService(
        string name, ManagedProcessStatus process) => new(
            name,
            process.IsRunning ? "Healthy" : "Error",
            process.IsRunning
                ? $"Running (PID {process.ProcessId?.ToString() ?? "unknown"})."
                : "Process is not running.",
            process.StartedAt?.ToUniversalTime());

    private static string SafeMessage(Exception exception) =>
        DiagnosticsReportBuilder.Redact(exception.Message);
}
