[CmdletBinding()]
param(
    [string]$Server = "azerothmedia",
    [string]$SshUser = "mark",
    [string]$IdentityFile = "$env:USERPROFILE\.ssh\azerothcore_beelink",
    [ValidateRange(1, 65535)] [int]$SshPort = 22,
    [ValidateRange(1024, 65535)] [int]$LocalApiPort = 5302,
    [ValidateRange(1, 65535)] [int]$RemoteApiPort = 5202,
    [string]$LocalWebUrl = "http://localhost:5311",
    [string]$RemoteWebConfiguration = "/etc/azerothcore-ui/web.production.json",
    [switch]$ValidateOnly,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$webProject = Join-Path $repositoryRoot "AzerothCore-UI.Web\AzerothCore-UI.Web.csproj"
$remoteTarget = "$SshUser@$Server"
$tunnelProcess = $null

function Assert-SafeValue {
    param(
        [Parameter(Mandatory)] [string]$Value,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Pattern
    )

    if ($Value -notmatch $Pattern) {
        throw "$Name contains unsupported characters: $Value"
    }
}

function Invoke-SshCapture {
    param([Parameter(Mandatory)] [string]$Command)

    $output = & ssh `
        -i $IdentityFile `
        -p $SshPort `
        -o IdentitiesOnly=yes `
        -o BatchMode=yes `
        -o ConnectTimeout=10 `
        $remoteTarget `
        $Command
    if ($LASTEXITCODE -ne 0) {
        throw "The remote command failed (exit code $LASTEXITCODE)."
    }
    return $output
}

function Test-LocalPortAvailable {
    param([Parameter(Mandatory)] [int]$Port)

    $listener = [Net.Sockets.TcpListener]::new(
        [Net.IPAddress]::Loopback, $Port)
    try {
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        $listener.Stop()
    }
}

function Restore-EnvironmentValue {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [AllowNull()] [string]$Value
    )

    if ($null -eq $Value) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item "Env:$Name" $Value
    }
}

Assert-SafeValue -Value $Server -Name "Server" `
    -Pattern '^[A-Za-z0-9][A-Za-z0-9.-]*$'
Assert-SafeValue -Value $SshUser -Name "SshUser" `
    -Pattern '^[A-Za-z_][A-Za-z0-9_-]*$'
Assert-SafeValue -Value $RemoteWebConfiguration -Name "RemoteWebConfiguration" `
    -Pattern '^/[A-Za-z0-9._/-]+$'

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "OpenSSH client (ssh) was not found."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK (dotnet) was not found."
}
if (-not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) {
    throw "SSH identity file was not found: $IdentityFile"
}
if (-not (Test-Path -LiteralPath $webProject -PathType Leaf)) {
    throw "Website project was not found: $webProject"
}
if (-not (Test-LocalPortAvailable -Port $LocalApiPort)) {
    throw "Local port $LocalApiPort is already in use. Stop the existing process or select another -LocalApiPort."
}

Write-Host "Reading the API service key over SSH (it will not be saved locally)..."
$apiKey = ((Invoke-SshCapture -Command (
    "sudo -n jq -er '.Security.ApiKey' '$RemoteWebConfiguration'"
)) -join "`n").Trim()
if ($apiKey.Length -lt 32) {
    throw "The remote production service key is missing or invalid."
}

$identityArgument = '"' + $IdentityFile.Replace('"', '\"') + '"'
$tunnelArguments = @(
    "-N",
    "-i", $identityArgument,
    "-p", $SshPort,
    "-o", "IdentitiesOnly=yes",
    "-o", "BatchMode=yes",
    "-o", "ExitOnForwardFailure=yes",
    "-o", "ServerAliveInterval=30",
    "-o", "ServerAliveCountMax=3",
    "-L", "127.0.0.1:${LocalApiPort}:127.0.0.1:${RemoteApiPort}",
    $remoteTarget
) -join " "

$oldApiBaseUrl = $env:ApiBaseUrl
$oldApiKey = $env:Security__ApiKey
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldUrls = $env:ASPNETCORE_URLS

try {
    Write-Host "Opening the private API tunnel on 127.0.0.1:$LocalApiPort..."
    $tunnelProcess = Start-Process `
        -FilePath "ssh" `
        -ArgumentList $tunnelArguments `
        -WindowStyle Hidden `
        -PassThru

    $tunnelReady = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($tunnelProcess.HasExited) {
            throw "The SSH tunnel exited before it became ready."
        }
        try {
            $health = Invoke-WebRequest `
                -Uri "http://127.0.0.1:$LocalApiPort/health/live" `
                -UseBasicParsing -TimeoutSec 2
            if ($health.StatusCode -eq 200) {
                $tunnelReady = $true
                break
            }
        }
        catch {
            # The SSH process can be running briefly before the forwarding socket is ready.
        }
    }
    if (-not $tunnelReady) {
        throw "The SSH tunnel opened, but the remote API health check did not respond."
    }
    if ($ValidateOnly) {
        Write-Host "Development tunnel validation passed. The tunnel will now be closed."
        return
    }

    $env:ApiBaseUrl = "http://127.0.0.1:$LocalApiPort"
    $env:Security__ApiKey = $apiKey
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $LocalWebUrl

    Write-Host ""
    Write-Host "Local website: $LocalWebUrl"
    Write-Host "It is connected to the live AzerothCore API on $Server."
    Write-Host "Press Ctrl+C to stop the website and close the SSH tunnel."
    Write-Host ""

    $dotnetArguments = @(
        "run",
        "--project", $webProject,
        "--no-launch-profile"
    )
    if ($NoBuild) {
        $dotnetArguments += "--no-build"
    }
    & dotnet @dotnetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The local website exited with code $LASTEXITCODE."
    }
}
finally {
    $apiKey = $null
    Restore-EnvironmentValue -Name "ApiBaseUrl" -Value $oldApiBaseUrl
    Restore-EnvironmentValue -Name "Security__ApiKey" -Value $oldApiKey
    Restore-EnvironmentValue -Name "ASPNETCORE_ENVIRONMENT" -Value $oldEnvironment
    Restore-EnvironmentValue -Name "ASPNETCORE_URLS" -Value $oldUrls

    if ($null -ne $tunnelProcess -and -not $tunnelProcess.HasExited) {
        Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
        $tunnelProcess.WaitForExit(5000) | Out-Null
    }
}
