[CmdletBinding()]
param(
    [string]$Hostname = "azerothcore.ddnsfree.com",
    [string]$Email = "mmrsolutionsltd@gmail.com",
    [string]$DestinationRoot = "C:\ProgramData\AzerothCore-UI"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $currentPrincipal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" " +
        "-Hostname `"$Hostname`" -Email `"$Email`" -DestinationRoot `"$DestinationRoot`""
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    Write-Output "Elevated deployment started. Approve the Windows UAC prompt."
    exit
}

function Read-UserSecrets([string]$Project) {
    $result = @{}
    dotnet user-secrets list --project $Project | ForEach-Object {
        $separator = $_.IndexOf(" = ")
        if ($separator -gt 0) {
            $result[$_.Substring(0, $separator)] = $_.Substring($separator + 3)
        }
    }
    return $result
}
function Require-Secret($Secrets, [string]$Name) {
    if (-not $Secrets.ContainsKey($Name) -or [string]::IsNullOrWhiteSpace($Secrets[$Name])) {
        throw "Required API user-secret is missing: $Name"
    }
    return $Secrets[$Name]
}

$apiSecrets = Read-UserSecrets (Join-Path $repositoryRoot "AzerothCore-UI.Api")
$coreConnection = Require-Secret $apiSecrets "ConnectionStrings:AzerothCore"
$maintenanceConnection = Require-Secret $apiSecrets "ConnectionStrings:AzerothCoreMaintenance"
$uiConnection = Require-Secret $apiSecrets "ConnectionStrings:AzerothCoreUi"
$soapUser = Require-Secret $apiSecrets "AzerothCore:Soap:Username"
$soapPassword = Require-Secret $apiSecrets "AzerothCore:Soap:Password"

$keyBytes = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
$generator.GetBytes($keyBytes)
$generator.Dispose()
$apiKey = -join ($keyBytes | ForEach-Object { $_.ToString("x2") })

$configRoot = Join-Path $DestinationRoot "config"
$keysRoot = Join-Path $DestinationRoot "keys"
New-Item -ItemType Directory -Force -Path $configRoot,$keysRoot | Out-Null
$apiConfig = @{
    AllowedHosts = "localhost;127.0.0.1"
    Security = @{ ApiKey = $apiKey }
    ConnectionStrings = @{
        AzerothCore = $coreConnection
        AzerothCoreMaintenance = $maintenanceConnection
        AzerothCoreUi = $uiConnection
    }
    AzerothCore = @{
        Server = @{ RootPath = "C:\AzerothServer-PlayerBots"; AuthStartDelaySeconds = 30 }
        Backups = @{ RetentionCount = 20 }
        Soap = @{
            Endpoint = "http://127.0.0.1:7878/"
            Username = $soapUser
            Password = $soapPassword
        }
    }
} | ConvertTo-Json -Depth 8
$webConfig = @{
    AllowedHosts = "$Hostname;localhost;127.0.0.1"
    ApiBaseUrl = "http://127.0.0.1:5202/"
    Security = @{ ApiKey = $apiKey; DataProtectionKeysPath = $keysRoot }
} | ConvertTo-Json -Depth 5
Set-Content (Join-Path $configRoot "api.production.json") $apiConfig -Encoding UTF8
Set-Content (Join-Path $configRoot "web.production.json") $webConfig -Encoding UTF8
icacls.exe $configRoot /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" `
    "$($env:USERNAME):(OI)(CI)F" | Out-Null
icacls.exe $keysRoot /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" `
    "$($env:USERNAME):(OI)(CI)F" | Out-Null

& (Join-Path $PSScriptRoot "Publish-Production.ps1") -DestinationRoot $DestinationRoot
$releaseId = Get-ChildItem (Join-Path $DestinationRoot "releases") -Directory |
    Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name

$caddyDirectory = "C:\Caddy"
$caddyExe = Join-Path $caddyDirectory "caddy.exe"
if (-not (Test-Path $caddyExe)) {
    $release = Invoke-RestMethod "https://api.github.com/repos/caddyserver/caddy/releases/latest"
    $asset = $release.assets | Where-Object name -match "windows_amd64\.zip$" |
        Select-Object -First 1
    $checksums = $release.assets | Where-Object name -match "checksums\.txt$" |
        Select-Object -First 1
    if (-not $asset -or -not $checksums) { throw "Official Caddy Windows asset was not found." }
    $temporaryDirectory = Join-Path $env:TEMP ("caddy-" + [guid]::NewGuid())
    New-Item -ItemType Directory $temporaryDirectory | Out-Null
    try {
        $archive = Join-Path $temporaryDirectory $asset.name
        $checksumFile = Join-Path $temporaryDirectory $checksums.name
        Invoke-WebRequest $asset.browser_download_url -OutFile $archive
        Invoke-WebRequest $checksums.browser_download_url -OutFile $checksumFile
        $expected = (Get-Content $checksumFile |
            Where-Object { $_ -match [regex]::Escape($asset.name) } |
            Select-Object -First 1).Split()[0]
        $algorithm = if ($expected.Length -eq 64) { "SHA256" }
            elseif ($expected.Length -eq 128) { "SHA512" }
            else { throw "Unrecognised checksum format for the Caddy archive." }
        $actual = (Get-FileHash $archive -Algorithm $algorithm).Hash.ToLowerInvariant()
        if (-not $expected -or $actual -ne $expected.ToLowerInvariant()) {
            throw "Caddy archive checksum validation failed."
        }
        Expand-Archive $archive -DestinationPath $temporaryDirectory
        New-Item -ItemType Directory -Force $caddyDirectory | Out-Null
        Copy-Item (Join-Path $temporaryDirectory "caddy.exe") $caddyExe -Force
    } finally {
        Remove-Item $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

& (Join-Path $PSScriptRoot "Install-Services.ps1") `
    -ReleaseId $releaseId -DestinationRoot $DestinationRoot
& (Join-Path $PSScriptRoot "Install-Caddy.ps1") `
    -Hostname $Hostname -Email $Email -CaddyExe $caddyExe

$apiKey = $coreConnection = $maintenanceConnection = $uiConnection = $soapPassword = $null
Write-Output "Local production deployment complete."
Write-Output "Hostname: https://$Hostname"
Write-Output "Router settings have not been changed. Forward TCP 443 only after local validation."
