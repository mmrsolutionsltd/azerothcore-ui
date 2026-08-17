using AzerothCore_UI.Web.Models;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class ServerAdministration
{
    private const int MaximumRandomBotCount = 5000;
    private ServerStatus? status;
    private PlayerBotSettings? playerBotSettings;
    private GameplayRateSettings? gameplayRates;
    private bool isLoading = true;
    private bool isWorking;
    private bool forceStop;
    private bool operationSucceeded;
    private string? errorMessage;
    private string? resultMessage;
    private string activeView = "PlayerBots";

    private bool CanStop =>
        !isWorking && (status?.WorldServer.IsRunning != true || status.SoapReachable || forceStop);
    private int PopulationPercent => status is null || status.PlayerLimit <= 0
        ? 0
        : Math.Min(100, (int)Math.Round(status.Population.Total * 100d / status.PlayerLimit));
    private string? PlayerBotValidationMessage => playerBotSettings switch
    {
        { MinRandomBots: < 0 or > MaximumRandomBotCount }
            or { MaxRandomBots: < 0 or > MaximumRandomBotCount }
            => $"Bot counts must be between 0 and {MaximumRandomBotCount:N0}.",
        { } settings when settings.MinRandomBots > settings.MaxRandomBots
            => "Minimum random bots cannot exceed maximum random bots.",
        _ => null
    };
    private bool PlayerBotSettingsValid => PlayerBotValidationMessage is null;

    protected override Task OnInitializedAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        isLoading = status is null;
        try
        {
            status = await AccountsClient.GetServerStatusAsync();
            playerBotSettings ??= await AccountsClient.GetPlayerBotSettingsAsync();
            gameplayRates ??= await AccountsClient.GetGameplayRateSettingsAsync();
            errorMessage = null;
        }
        catch (Exception exception)
        {
            errorMessage = $"Server status refresh failed: {exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private Task StartAsync() => RunAsync(() => AccountsClient.StartServersAsync());
    private Task StopAsync() => RunAsync(() => AccountsClient.StopServersAsync(forceStop));
    private Task RestartAsync() => RunAsync(() => AccountsClient.RestartServersAsync(forceStop));

    private async Task SavePlayerBotSettingsAsync()
    {
        if (playerBotSettings is null || !PlayerBotSettingsValid)
        {
            operationSucceeded = false;
            resultMessage = PlayerBotValidationMessage ?? "PlayerBots settings are unavailable.";
            return;
        }

        isWorking = true;
        resultMessage = null;
        try
        {
            playerBotSettings = await AccountsClient.UpdatePlayerBotSettingsAsync(playerBotSettings);
            operationSucceeded = playerBotSettings is not null;
            resultMessage = operationSucceeded
                ? "PlayerBots settings saved. Restart the worldserver to apply them."
                : "PlayerBots settings were not returned.";
            status = await AccountsClient.GetServerStatusAsync();
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task SaveGameplayRatesAsync()
    {
        if (gameplayRates is null)
            return;

        isWorking = true;
        resultMessage = null;
        try
        {
            gameplayRates = await AccountsClient.UpdateGameplayRateSettingsAsync(gameplayRates);
            operationSucceeded = gameplayRates is not null;
            resultMessage = operationSucceeded
                ? "Gameplay and gathering rates saved. Restart the worldserver to apply them."
                : "Gameplay rate settings were not returned.";
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally
        {
            isWorking = false;
        }
    }

    private bool GatheringRatesValid => gameplayRates is not null
        && gameplayRates.HerbAbundancePercent is >= 25 and <= 500
        && gameplayRates.HerbAbundancePercent % 5 == 0
        && gameplayRates.MiningAbundancePercent is >= 25 and <= 500
        && gameplayRates.MiningAbundancePercent % 5 == 0;

    private async Task RunAsync(Func<Task<AdministrationResult?>> operation)
    {
        isWorking = true;
        try
        {
            var result = await operation();
            operationSucceeded = result?.Success == true;
            resultMessage = result?.Message;
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            resultMessage = exception.Message;
        }
        finally
        {
            isWorking = false;
        }
    }

    private static string FormatBytes(long? bytes) =>
        bytes.HasValue ? $"{bytes.Value / 1024d / 1024d:0.0} MB" : "\u2014";
}
