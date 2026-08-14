using System.Diagnostics;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Data;
using Dapper;

namespace AzerothCore_UI.Api.Services;

public sealed class AzerothCoreServerManager(
    IConfiguration configuration,
    AzerothCoreSoapClient soapClient,
    AzerothCoreConnectionFactory connectionFactory,
    AzerothCoreConfigurationManager configurationManager,
    ILogger<AzerothCoreServerManager> logger)
{
    private readonly string rootPath = configuration["AzerothCore:Server:RootPath"]
        ?? @"C:\AzerothServer-PlayerBots";
    private readonly string logPath = configuration["AzerothCore:Server:LogPath"]
        ?? configuration["AzerothCore:Server:RootPath"]
        ?? @"C:\AzerothServer-PlayerBots";
    private readonly int authStartDelaySeconds = configuration.GetValue("AzerothCore:Server:AuthStartDelaySeconds", 30);
    private readonly bool useSystemd = configuration.GetValue(
        "AzerothCore:Server:UseSystemd", OperatingSystem.IsLinux());
    private readonly bool systemctlUseSudo = configuration.GetValue(
        "AzerothCore:Server:SystemctlUseSudo", true);
    private readonly string worldServiceName = configuration[
        "AzerothCore:Server:WorldServiceName"] ?? "azerothcore-world.service";
    private readonly string authServiceName = configuration[
        "AzerothCore:Server:AuthServiceName"] ?? "azerothcore-auth.service";

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var world = GetProcessStatus("worldserver");
        var auth = GetProcessStatus("authserver");
        string? worldStatus = null;
        var reachable = false;
        if (world.IsRunning && soapClient.IsConfigured)
        {
            try
            {
                worldStatus = await soapClient.ExecuteAsync("server info", cancellationToken);
                reachable = true;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                worldStatus = "SOAP is not reachable.";
            }
        }

        var population = await GetPopulationAsync(cancellationToken);
        return new ServerStatus(world, auth, soapClient.IsConfigured, reachable, worldStatus, ReadRecentLogs(),
            population, configurationManager.GetPlayerLimit());
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (GetProcessStatus("worldserver").IsRunning || GetProcessStatus("authserver").IsRunning)
            throw new InvalidOperationException("One or more AzerothCore server processes are already running.");

        if (useSystemd)
            await RunSystemctlAsync("start", worldServiceName, cancellationToken);
        else
            StartExecutable(ExecutableName("worldserver"));
        await Task.Delay(TimeSpan.FromSeconds(authStartDelaySeconds), cancellationToken);
        if (!GetProcessStatus("worldserver").IsRunning)
            throw new InvalidOperationException("Worldserver exited during startup. Check the server logs.");
        if (useSystemd)
            await RunSystemctlAsync("start", authServiceName, cancellationToken);
        else
            StartExecutable(ExecutableName("authserver"));
        logger.LogWarning("ADMIN AUDIT: AzerothCore servers started through the administration API.");
    }

    public async Task StopAsync(bool force, CancellationToken cancellationToken)
    {
        if (useSystemd)
        {
            await StopSystemdServicesAsync(force, cancellationToken);
            logger.LogWarning(
                "ADMIN AUDIT: AzerothCore systemd services stopped through the administration API. Force={Force}",
                force);
            return;
        }

        using var world = Process.GetProcessesByName("worldserver").FirstOrDefault();
        if (world is not null)
        {
            if (soapClient.IsConfigured)
            {
                try
                {
                    await soapClient.ExecuteAsync("server shutdown 1", cancellationToken);
                    await WaitForExitAsync(world, TimeSpan.FromSeconds(60), cancellationToken);
                }
                catch (Exception exception) when (force
                    && exception is (HttpRequestException or InvalidOperationException))
                {
                    logger.LogWarning(exception, "Graceful worldserver shutdown failed; confirmed force stop will be used.");
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        "The graceful worldserver shutdown command could not be delivered through SOAP. Confirm a forced stop or correct the SOAP configuration.",
                        exception);
                }
            }
            else if (!force)
                throw new InvalidOperationException(
                    "Worldserver cannot be stopped gracefully because SOAP credentials are not configured. Configure SOAP or confirm a forced stop.");

            if (!world.HasExited && !force)
                throw new InvalidOperationException(
                    "Worldserver did not exit within 60 seconds. Confirm a forced stop only after checking its console and logs.");
            if (!world.HasExited)
            {
                world.Kill(true);
                await world.WaitForExitAsync(cancellationToken);
            }
        }

        using var auth = Process.GetProcessesByName("authserver").FirstOrDefault();
        if (auth is not null)
        {
            auth.CloseMainWindow();
            await WaitForExitAsync(auth, TimeSpan.FromSeconds(10), cancellationToken);
            if (!auth.HasExited)
            {
                auth.Kill(true);
                await auth.WaitForExitAsync(cancellationToken);
            }
        }
        logger.LogWarning("ADMIN AUDIT: AzerothCore servers stopped through the administration API. Force={Force}", force);
    }

    private void StartExecutable(string name)
    {
        var path = Path.GetFullPath(Path.Combine(rootPath, name));
        if (!path.StartsWith(Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new FileNotFoundException($"The configured {name} executable was not found.");
        Process.Start(new ProcessStartInfo(path) { WorkingDirectory = rootPath, UseShellExecute = true });
    }

    private async Task StopSystemdServicesAsync(bool force, CancellationToken cancellationToken)
    {
        using var world = Process.GetProcessesByName("worldserver").FirstOrDefault();
        if (world is not null)
        {
            if (soapClient.IsConfigured)
            {
                try
                {
                    await soapClient.ExecuteAsync("server shutdown 1", cancellationToken);
                    await WaitForExitAsync(world, TimeSpan.FromSeconds(60), cancellationToken);
                }
                catch (Exception exception) when (force
                    && exception is (HttpRequestException or InvalidOperationException))
                {
                    logger.LogWarning(exception,
                        "Graceful worldserver shutdown failed; confirmed systemd stop will be used.");
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        "The graceful worldserver shutdown command could not be delivered through SOAP. Confirm a forced stop or correct the SOAP configuration.",
                        exception);
                }
            }
            else if (!force)
                throw new InvalidOperationException(
                    "Worldserver cannot be stopped gracefully because SOAP credentials are not configured. Configure SOAP or confirm a forced stop.");

            if (!world.HasExited && !force)
                throw new InvalidOperationException(
                    "Worldserver did not exit within 60 seconds. Confirm a forced stop only after checking its logs.");
        }

        await RunSystemctlAsync("stop", worldServiceName, cancellationToken);
        await RunSystemctlAsync("stop", authServiceName, cancellationToken);
    }

    private async Task RunSystemctlAsync(
        string action, string serviceName, CancellationToken cancellationToken)
    {
        if (serviceName.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('-' or '_' or '.' or '@')))
            throw new InvalidOperationException("The configured systemd service name is invalid.");

        var startInfo = new ProcessStartInfo
        {
            FileName = systemctlUseSudo ? "/usr/bin/sudo" : "/usr/bin/systemctl",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (systemctlUseSudo)
        {
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("/usr/bin/systemctl");
        }
        startInfo.ArgumentList.Add(action);
        startInfo.ArgumentList.Add(serviceName);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start systemctl.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"systemctl {action} {serviceName} failed: {(string.IsNullOrWhiteSpace(error) ? output : error).Trim()}");
    }

    private static string ExecutableName(string name) =>
        OperatingSystem.IsWindows() ? $"{name}.exe" : name;

    private static ManagedProcessStatus GetProcessStatus(string name)
    {
        using var process = Process.GetProcessesByName(name).FirstOrDefault();
        if (process is null) return new ManagedProcessStatus(name, false, null, null, null);
        try { return new ManagedProcessStatus(name, true, process.Id, process.StartTime, process.WorkingSet64); }
        catch { return new ManagedProcessStatus(name, true, process.Id, null, null); }
    }

    private IReadOnlyList<ServerLogEntry> ReadRecentLogs()
    {
        var entries = new List<ServerLogEntry>();
        foreach (var name in new[] { "Errors.log", "Server.log", "Auth.log", "Playerbots.log" })
        {
            var path = Path.Combine(logPath, name);
            if (!File.Exists(path)) continue;
            try { entries.AddRange(File.ReadLines(path).TakeLast(20).Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => new ServerLogEntry(name, line.Length <= 500 ? line : line[..500]))); }
            catch (IOException) { }
        }
        return entries.TakeLast(50).ToArray();
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken token)
    {
        var wait = process.WaitForExitAsync(token);
        await Task.WhenAny(wait, Task.Delay(timeout, token));
    }

    private async Task<ServerPopulation> GetPopulationAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COALESCE(SUM(CASE WHEN account.username LIKE CONCAT(@BotPrefix, '%') THEN 0 ELSE 1 END), 0) AS HumanPlayers,
                COALESCE(SUM(CASE WHEN account.username LIKE CONCAT(@BotPrefix, '%') THEN 1 ELSE 0 END), 0) AS PlayerBots
            FROM acore_characters.characters characterData
            INNER JOIN acore_auth.account account ON account.id = characterData.account
            WHERE characterData.online = 1;
            """;
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<ServerPopulation>(new CommandDefinition(
            sql, new { BotPrefix = "rndbot" }, cancellationToken: cancellationToken));
    }
}
