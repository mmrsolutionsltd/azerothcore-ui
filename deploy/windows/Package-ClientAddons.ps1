[CmdletBinding()]
param(
    [string]$OutputDirectory = ".artifacts\client-addons"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$addonRoot = Join-Path $repositoryRoot "client-addons\AzerothCompanion"
$toc = Join-Path $addonRoot "AzerothCompanion.toc"
if (-not (Test-Path -LiteralPath $toc)) {
    throw "AzerothCompanion.toc was not found."
}
$versionLine = Get-Content -LiteralPath $toc |
    Where-Object { $_ -match '^## Version:\s*(.+)$' } |
    Select-Object -First 1
if (-not $versionLine -or $versionLine -notmatch '^## Version:\s*(.+)$') {
    throw "The addon version is missing from AzerothCompanion.toc."
}
$version = $Matches[1].Trim()
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$archive = Join-Path $resolvedOutput "AzerothCompanion-$version.zip"
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -LiteralPath $addonRoot -DestinationPath $archive -CompressionLevel Optimal
Write-Output "Packaged addon: $archive"
