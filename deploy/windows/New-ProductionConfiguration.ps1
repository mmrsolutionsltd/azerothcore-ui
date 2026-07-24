[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Hostname,
    [string]$DestinationRoot = "C:\ProgramData\AzerothCore-UI",
    [string]$ServerRoot = "C:\AzerothServer-PlayerBots",
    [string]$CoreDatabaseUser = "acore",
    [string]$MaintenanceDatabaseUser = "root",
    [string]$UiDatabaseUser = "azerothcore_ui_app",
    [string]$SoapEndpoint = "http://127.0.0.1:7878/"
)

$ErrorActionPreference = "Stop"
function Read-PlainSecret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$apiKeyBytes = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
$generator.GetBytes($apiKeyBytes)
$generator.Dispose()
$apiKey = -join ($apiKeyBytes | ForEach-Object { $_.ToString("x2") })

$corePassword = Read-PlainSecret "AzerothCore database password"
$maintenancePassword = Read-PlainSecret "Database maintenance password"
$uiPassword = Read-PlainSecret "azerothcore_ui_app password"
$soapUser = Read-Host "AzerothCore SOAP username"
$soapPassword = Read-PlainSecret "AzerothCore SOAP password"

$configRoot = Join-Path $DestinationRoot "config"
$keysRoot = Join-Path $DestinationRoot "keys"
New-Item -ItemType Directory -Force -Path $configRoot,$keysRoot | Out-Null

$apiConfig = @{
    AllowedHosts = "localhost;127.0.0.1"
    Security = @{ ApiKey = $apiKey }
    ConnectionStrings = @{
        AzerothCore = "Server=127.0.0.1;Port=3306;User ID=$CoreDatabaseUser;Password=$corePassword"
        AzerothCoreMaintenance = "Server=127.0.0.1;Port=3306;User ID=$MaintenanceDatabaseUser;Password=$maintenancePassword"
        AzerothCoreUi = "Server=127.0.0.1;Port=3306;Database=azerothcore_ui;User ID=$UiDatabaseUser;Password=$uiPassword"
    }
    AzerothCore = @{
        Server = @{ RootPath = $ServerRoot; AuthStartDelaySeconds = 30 }
        Backups = @{ RetentionCount = 20 }
        Soap = @{ Endpoint = $SoapEndpoint; Username = $soapUser; Password = $soapPassword }
    }
} | ConvertTo-Json -Depth 8

$webConfig = @{
    AllowedHosts = $Hostname
    ApiBaseUrl = "http://127.0.0.1:5202/"
    Security = @{
        ApiKey = $apiKey
        DataProtectionKeysPath = $keysRoot
    }
} | ConvertTo-Json -Depth 5

$apiPath = Join-Path $configRoot "api.production.json"
$webPath = Join-Path $configRoot "web.production.json"
Set-Content -LiteralPath $apiPath -Value $apiConfig -Encoding UTF8
Set-Content -LiteralPath $webPath -Value $webConfig -Encoding UTF8

icacls.exe $configRoot /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" `
    "$($env:USERNAME):(OI)(CI)F" | Out-Null
icacls.exe $keysRoot /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" `
    "$($env:USERNAME):(OI)(CI)F" | Out-Null

$corePassword = $maintenancePassword = $uiPassword = $soapPassword = $apiKey = $null
Write-Output "Protected production configuration created in $configRoot."
