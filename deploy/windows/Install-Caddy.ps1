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
$caddyfile = @"
{
    email $Email
    admin 127.0.0.1:2019
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

$command = "`"$CaddyExe`" run --environ --config `"$caddyfilePath`""
if (Get-Service Caddy -ErrorAction SilentlyContinue) {
    Stop-Service Caddy -Force -ErrorAction SilentlyContinue
    sc.exe config Caddy binPath= $command start= auto | Out-Null
} else {
    New-Service -Name Caddy -DisplayName "Caddy HTTPS Reverse Proxy" `
        -BinaryPathName $command -StartupType Automatic -DependsOn "AzerothCoreUiWeb" | Out-Null
}
sc.exe failure Caddy reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
New-NetFirewallRule -DisplayName "AzerothCore UI HTTPS" -Direction Inbound `
    -Action Allow -Protocol TCP -LocalPort 80,443 -Profile Private | Out-Null
Start-Service Caddy
Write-Output "Caddy is serving https://$Hostname and proxying only to the local Blazor service."
