[CmdletBinding()]
param(
    [string]$DestinationRoot = "C:\ProgramData\AzerothCore-UI",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$releaseId = Get-Date -Format "yyyyMMdd-HHmmss"
$releaseRoot = Join-Path $DestinationRoot "releases\$releaseId"

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
dotnet publish (Join-Path $repositoryRoot "AzerothCore-UI.Api\AzerothCore-UI.Api.csproj") `
    -c Release -r $Runtime --self-contained false -o (Join-Path $releaseRoot "Api")
if ($LASTEXITCODE -ne 0) { throw "API publish failed." }
dotnet publish (Join-Path $repositoryRoot "AzerothCore-UI.Web\AzerothCore-UI.Web.csproj") `
    -c Release -r $Runtime --self-contained false -o (Join-Path $releaseRoot "Web")
if ($LASTEXITCODE -ne 0) { throw "Web publish failed." }

$manifest = @{
    releaseId = $releaseId
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    commit = (git -C $repositoryRoot rev-parse HEAD)
    runtime = $Runtime
} | ConvertTo-Json
Set-Content -LiteralPath (Join-Path $releaseRoot "release.json") -Value $manifest -Encoding UTF8

Write-Output "Published release: $releaseRoot"
Write-Output "Run Install-Services.ps1 -ReleaseId $releaseId after generating production configuration."
