[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ReleaseId,
    [string]$DestinationRoot = "C:\ProgramData\AzerothCore-UI",
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe"
)

$ErrorActionPreference = "Stop"
$releaseRoot = Join-Path $DestinationRoot "releases\$ReleaseId"
$apiDll = Join-Path $releaseRoot "Api\AzerothCore-UI.Api.dll"
$webDll = Join-Path $releaseRoot "Web\AzerothCore-UI.Web.dll"
$apiConfig = Join-Path $DestinationRoot "config\api.production.json"
$webConfig = Join-Path $DestinationRoot "config\web.production.json"
foreach ($path in @($DotnetPath,$apiDll,$webDll,$apiConfig,$webConfig)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required file not found: $path" }
}

function Set-ServiceDefinition(
    [string]$Name, [string]$DisplayName, [string]$BinaryPath, [string[]]$DependsOn
) {
    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne "Stopped") { Stop-Service -Name $Name -Force }
        & sc.exe config $Name "binPath= $BinaryPath" "start= auto" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not update the $Name Windows service definition."
        }
    } else {
        New-Service -Name $Name -DisplayName $DisplayName -BinaryPathName $BinaryPath `
            -StartupType Automatic -DependsOn $DependsOn | Out-Null
    }
    & sc.exe failure $Name "reset= 86400" `
        "actions= restart/5000/restart/15000/restart/60000" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not configure recovery for the $Name Windows service."
    }
}

$apiCommand = "`"$DotnetPath`" `"$apiDll`" --environment Production --urls http://127.0.0.1:5202 --ExternalConfig=`"$apiConfig`""
$webCommand = "`"$DotnetPath`" `"$webDll`" --environment Production --urls http://127.0.0.1:5211 --ExternalConfig=`"$webConfig`""
Set-ServiceDefinition "AzerothCoreUiApi" "AzerothCore UI API" $apiCommand @("MySQL80")
Set-ServiceDefinition "AzerothCoreUiWeb" "AzerothCore UI Web" $webCommand @("AzerothCoreUiApi")

Set-Content -LiteralPath (Join-Path $DestinationRoot "current-release.txt") `
    -Value $ReleaseId -Encoding ASCII
Start-Service AzerothCoreUiApi
Start-Service AzerothCoreUiWeb
if (Get-Service Caddy -ErrorAction SilentlyContinue) {
    Start-Service Caddy
}
Write-Output "Services installed and started with release $ReleaseId."
