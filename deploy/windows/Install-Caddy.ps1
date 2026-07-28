[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Hostname,
    [Parameter(Mandatory)] [string]$Email,
    [string]$CaddyExe = "C:\Caddy\caddy.exe",
    [string]$CaddyRoot = "C:\ProgramData\Caddy"
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $CaddyExe)) { throw "Caddy was not found at $CaddyExe" }
New-Item -ItemType Directory -Force -Path $CaddyRoot,(Join-Path $CaddyRoot "logs") | Out-Null
icacls.exe $CaddyRoot /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" `
    "$($env:USERNAME):(OI)(CI)F" | Out-Null
$caddyfile = @"
{
    email $Email
    admin 127.0.0.1:2019
    # Use the TLS-ALPN certificate challenge on 443. Some Windows hosts,
    # including this one, reserve TCP 80.
    auto_https disable_redirects
    log default {
        output file $($CaddyRoot.Replace('\','/'))/logs/runtime.log {
            roll_size 25MiB
            roll_keep 10
        }
    }
}

$Hostname {
    encode zstd gzip
    reverse_proxy 127.0.0.1:5211
    header {
        -Server
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
    }
    log {
        output file $($CaddyRoot.Replace('\','/'))/logs/access.log {
            roll_size 25MiB
            roll_keep 10
        }
    }
}
"@
$caddyfilePath = Join-Path $CaddyRoot "Caddyfile"
Set-Content -LiteralPath $caddyfilePath -Value $caddyfile -Encoding UTF8
& $CaddyExe validate --config $caddyfilePath
if ($LASTEXITCODE -ne 0) { throw "Caddy configuration validation failed." }

$wrapper = Join-Path $CaddyRoot "caddy-service.exe"
if (-not (Test-Path $wrapper)) {
    $release = Invoke-RestMethod "https://api.github.com/repos/winsw/winsw/releases/latest"
    $asset = $release.assets | Where-Object name -eq "WinSW-x64.exe" | Select-Object -First 1
    if (-not $asset) { throw "The official WinSW x64 service wrapper was not found." }
    Invoke-WebRequest $asset.browser_download_url -OutFile $wrapper
    if ($asset.digest -and $asset.digest.StartsWith("sha256:")) {
        $expected = $asset.digest.Substring(7)
        $actual = (Get-FileHash $wrapper -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "WinSW checksum validation failed." }
    }
}

$escapedCaddyExe = [Security.SecurityElement]::Escape($CaddyExe)
$escapedCaddyfile = [Security.SecurityElement]::Escape($caddyfilePath)
$serviceXml = @"
<service>
  <id>Caddy</id>
  <name>Caddy HTTPS Reverse Proxy</name>
  <description>Caddy HTTPS reverse proxy for AzerothCore UI</description>
  <executable>$escapedCaddyExe</executable>
  <arguments>run --environ --config "$escapedCaddyfile"</arguments>
  <startmode>Automatic</startmode>
  <depend>AzerothCoreUiWeb</depend>
  <stoptimeout>15sec</stoptimeout>
  <onfailure action="restart" delay="5 sec"/>
  <onfailure action="restart" delay="15 sec"/>
  <onfailure action="restart" delay="60 sec"/>
  <logpath>$($CaddyRoot.Replace('\','/'))/logs</logpath>
  <log mode="roll-by-size">
    <sizeThreshold>10240</sizeThreshold>
    <keepFiles>5</keepFiles>
  </log>
</service>
"@
Set-Content (Join-Path $CaddyRoot "caddy-service.xml") $serviceXml -Encoding UTF8

if (Get-Service Caddy -ErrorAction SilentlyContinue) {
    Stop-Service Caddy -Force -ErrorAction SilentlyContinue
    sc.exe delete Caddy | Out-Null
    for ($attempt = 0; $attempt -lt 20 -and
        (Get-Service Caddy -ErrorAction SilentlyContinue); $attempt++) {
        Start-Sleep -Milliseconds 250
    }
}
Push-Location $CaddyRoot
try {
    & $wrapper install
    if ($LASTEXITCODE -ne 0) { throw "WinSW could not install the Caddy service." }
    & $wrapper start
    if ($LASTEXITCODE -ne 0) { throw "WinSW could not start the Caddy service." }
} finally { Pop-Location }

if (-not (Get-NetFirewallRule -DisplayName "AzerothCore UI HTTPS" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "AzerothCore UI HTTPS" -Direction Inbound `
        -Action Allow -Protocol TCP -LocalPort 443 -Profile Any | Out-Null
}
Write-Output "Caddy is serving https://$Hostname and proxying only to the local Blazor service."
