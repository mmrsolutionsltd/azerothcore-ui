using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;
using MySqlConnector;

namespace AzerothCore_UI.Api.Services;

public sealed class SecurityDashboardService(IConfiguration configuration)
{
    private const string DefaultHostname = "azerothcore.ddnsfree.com";
    private const string DefaultAccessLog =
        @"C:\ProgramData\Caddy\logs\access.log";
    private const string DefaultCertificateRoot =
        @"C:\Windows\System32\config\systemprofile\AppData\Roaming\Caddy\certificates";
    private readonly string connectionString =
        configuration.GetConnectionString("AzerothCoreUi")
        ?? throw new InvalidOperationException(
            "Connection string 'AzerothCoreUi' is not configured.");

    public async Task<SecurityDashboard> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        var since = DateTime.UtcNow.AddHours(-24);
        var counts = await connection.QuerySingleAsync<SecurityCounts>("""
            SELECT
              SUM(action='Login' AND outcome='Succeeded'
                  AND occurred_at_utc >= @Since) SuccessfulLogins24Hours,
              SUM(action='Login' AND outcome<>'Succeeded'
                  AND occurred_at_utc >= @Since) FailedLogins24Hours,
              SUM(outcome='Failed' AND action<>'Login'
                  AND occurred_at_utc >= @Since) DeniedActions24Hours
            FROM admin_audit_log
            """, new { Since = since });
        var users = (await connection.QueryAsync<SecurityUserStatus>("""
            SELECT id, username, role, enabled,
              must_change_password MustChangePassword,
              failed_login_count FailedLoginCount,
              lockout_until_utc LockoutUntilUtc,
              last_login_at_utc LastLoginAtUtc
            FROM admin_user ORDER BY enabled DESC, username
            """)).AsList();
        return new SecurityDashboard(
            counts.SuccessfulLogins24Hours,
            counts.FailedLogins24Hours,
            counts.DeniedActions24Hours,
            users.Count(user => user.Enabled),
            users.Count(user => user.LockoutUntilUtc > DateTime.UtcNow),
            users,
            ReadSuspiciousProbes(since),
            ReadCertificate());
    }

    public static bool IsSuspiciousPath(string path)
    {
        var value = path.ToLowerInvariant();
        return value.Contains(".env")
            || value.Contains("/.git")
            || value.Contains("phpinfo")
            || value.Contains("wp-admin")
            || value.Contains("wp-login")
            || value.Contains("xmlrpc")
            || value.Contains(".aws/")
            || value.Contains("config.json")
            || value.Contains("actuator")
            || value.Contains("vendor/phpunit");
    }

    private IReadOnlyList<SuspiciousProbeSummary> ReadSuspiciousProbes(DateTime sinceUtc)
    {
        var path = configuration["SecurityDashboard:CaddyAccessLog"] ?? DefaultAccessLog;
        if (!File.Exists(path)) return [];
        var recentLines = new Queue<string>(5000);
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (recentLines.Count == 5000) recentLines.Dequeue();
                recentLines.Enqueue(line);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Caddy may briefly hold the log exclusively while rotating it.
            // Security metrics should remain available even when probe data is not.
            return [];
        }
        var probes = new List<Probe>();
        foreach (var line in recentLines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var timestamp = root.GetProperty("ts").GetDouble();
                var occurredAtUtc = DateTimeOffset
                    .FromUnixTimeMilliseconds((long)(timestamp * 1000)).UtcDateTime;
                if (occurredAtUtc < sinceUtc) continue;
                var request = root.GetProperty("request");
                var requestPath = request.GetProperty("uri").GetString() ?? "/";
                if (!IsSuspiciousPath(requestPath)) continue;
                probes.Add(new(
                    request.GetProperty("remote_ip").GetString() ?? "unknown",
                    occurredAtUtc,
                    requestPath,
                    root.TryGetProperty("status", out var status)
                        ? status.GetInt32() : 0));
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException
                    or KeyNotFoundException or FormatException)
            {
                // Ignore incomplete or older-format access-log lines.
            }
        }
        return probes
            .GroupBy(probe => probe.RemoteAddress)
            .Select(group =>
            {
                var latest = group.MaxBy(probe => probe.OccurredAtUtc)!;
                return new SuspiciousProbeSummary(
                    group.Key, group.Count(), latest.OccurredAtUtc,
                    latest.Path, latest.StatusCode);
            })
            .OrderByDescending(probe => probe.LastSeenUtc)
            .Take(50)
            .ToArray();
    }

    private CertificateStatus ReadCertificate()
    {
        var hostname = configuration["PublicSite:Hostname"] ?? DefaultHostname;
        var root = configuration["SecurityDashboard:CaddyCertificateRoot"]
            ?? DefaultCertificateRoot;
        try
        {
            var file = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, $"{hostname}.crt",
                        SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(value => value.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (file is null)
                return new(hostname, false, false, null, null,
                    "The public certificate was not found in Caddy storage.");
            var certificate = X509CertificateLoader.LoadCertificateFromFile(file.FullName);
            var now = DateTime.UtcNow;
            var expires = certificate.NotAfter.ToUniversalTime();
            var valid = certificate.NotBefore.ToUniversalTime() <= now && expires > now;
            var days = (int)Math.Floor((expires - now).TotalDays);
            return new(hostname, true, valid, expires, days,
                valid ? $"Valid for {Math.Max(0, days)} more days."
                    : "The certificate is not currently valid.");
        }
        catch (Exception exception)
        {
            return new(hostname, false, false, null, null, exception.Message);
        }
    }

    private sealed class SecurityCounts
    {
        public int SuccessfulLogins24Hours { get; init; }
        public int FailedLogins24Hours { get; init; }
        public int DeniedActions24Hours { get; init; }
    }

    private sealed record Probe(
        string RemoteAddress, DateTime OccurredAtUtc, string Path, int StatusCode);
}
