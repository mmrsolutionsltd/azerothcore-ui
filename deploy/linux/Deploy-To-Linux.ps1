[CmdletBinding()]
param(
    [string]$Server = "azerothmedia",
    [string]$SshUser = "mark",
    [string]$IdentityFile = "$env:USERPROFILE\.ssh\azerothcore_beelink",
    [ValidateRange(1, 65535)] [int]$SshPort = 22,
    [string]$RemoteRoot = "/opt/azerothcore/admin",
    [string]$PublicUrl = "https://azerothcore.ddnsfree.com",
    [string]$Runtime = "linux-x64",
    [switch]$ValidateOnly,
    [switch]$SkipPublicHealthCheck,
    [switch]$RequireCleanWorkingTree
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiProject = Join-Path $repositoryRoot "AzerothCore-UI.Api\AzerothCore-UI.Api.csproj"
$webProject = Join-Path $repositoryRoot "AzerothCore-UI.Web\AzerothCore-UI.Web.csproj"
$remoteTarget = "$SshUser@$Server"
$apiService = "azerothcore-ui-api.service"
$webService = "azerothcore-ui-web.service"
$publicHost = if ([string]::IsNullOrWhiteSpace($PublicUrl)) {
    "localhost"
}
else {
    ([Uri]$PublicUrl).Host
}

function Assert-SafeRemoteValue {
    param(
        [Parameter(Mandatory)] [string]$Value,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Pattern
    )

    if ($Value -notmatch $Pattern) {
        throw "$Name contains unsupported characters: $Value"
    }
}

function Assert-CommandAvailable {
    param([Parameter(Mandatory)] [string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Invoke-Ssh {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [switch]$CaptureOutput
    )

    $arguments = @(
        "-i", $IdentityFile,
        "-p", $SshPort,
        "-o", "IdentitiesOnly=yes",
        "-o", "BatchMode=yes",
        "-o", "ConnectTimeout=10",
        $remoteTarget,
        $Command
    )
    if ($CaptureOutput) {
        $output = & ssh @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Remote command failed (exit code $LASTEXITCODE)."
        }
        return $output
    }

    Invoke-NativeCommand -FilePath "ssh" -Arguments $arguments `
        -FailureMessage "Remote command failed"
}

function Set-RemoteCurrentRelease {
    param([Parameter(Mandatory)] [string]$ReleasePath)

    $nextLink = "$RemoteRoot/current.next"
    Invoke-Ssh -Command (
        "rm -f '$nextLink' && " +
        "ln -s '$ReleasePath' '$nextLink' && " +
        "mv -Tf '$nextLink' '$RemoteRoot/current'"
    )
}

function Restart-RemoteApplications {
    Invoke-Ssh -Command (
        "sudo -n systemctl restart '$apiService' && " +
        "sudo -n systemctl restart '$webService'"
    )
}

function Test-RemoteReadiness {
    Invoke-Ssh -Command (
        "ready=0; for attempt in `$(seq 1 30); do " +
        "if curl -fsS --max-time 3 http://127.0.0.1:5202/health/ready >/dev/null; " +
        "then ready=1; break; fi; sleep 1; done; " +
        "if [ `"`$ready`" -ne 1 ]; then echo 'API readiness failed' >&2; exit 1; fi; " +
        "ready=0; for attempt in `$(seq 1 30); do " +
        "if curl -fsS --max-time 3 -H 'Host: $publicHost' " +
        "http://127.0.0.1:5211/health/ready >/dev/null; " +
        "then ready=1; break; fi; sleep 1; done; " +
        "if [ `"`$ready`" -ne 1 ]; then echo 'Website readiness failed' >&2; exit 1; fi"
    )
}

Assert-SafeRemoteValue -Value $Server -Name "Server" `
    -Pattern '^[A-Za-z0-9][A-Za-z0-9.-]*$'
Assert-SafeRemoteValue -Value $SshUser -Name "SshUser" `
    -Pattern '^[A-Za-z_][A-Za-z0-9_-]*$'
Assert-SafeRemoteValue -Value $RemoteRoot -Name "RemoteRoot" `
    -Pattern '^/[A-Za-z0-9._/-]+$'
Assert-SafeRemoteValue -Value $Runtime -Name "Runtime" `
    -Pattern '^[A-Za-z0-9._-]+$'
Assert-SafeRemoteValue -Value $publicHost -Name "PublicUrl host" `
    -Pattern '^[A-Za-z0-9][A-Za-z0-9.-]*$'

Assert-CommandAvailable "dotnet"
Assert-CommandAvailable "git"
Assert-CommandAvailable "ssh"
Assert-CommandAvailable "scp"
if (-not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) {
    throw "SSH identity file was not found: $IdentityFile"
}
if (-not (Test-Path -LiteralPath $apiProject -PathType Leaf) -or
    -not (Test-Path -LiteralPath $webProject -PathType Leaf)) {
    throw "Run this script from a complete AzerothCore-UI source checkout."
}

Write-Host "Checking SSH access and the installed Linux service layout..."
Invoke-Ssh -Command (
    "test -d '$RemoteRoot/releases' && " +
    "test -L '$RemoteRoot/current' && " +
    "sudo -n systemctl cat '$apiService' '$webService' >/dev/null"
)
Test-RemoteReadiness

if ($ValidateOnly) {
    Write-Host "Linux deployment validation passed. No files or services were changed."
    return
}

$workingTreeChanges = @(git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the Git working tree."
}
if ($RequireCleanWorkingTree -and $workingTreeChanges.Count -gt 0) {
    throw "The Git working tree is not clean. Commit or stash changes, or omit -RequireCleanWorkingTree."
}
if ($workingTreeChanges.Count -gt 0) {
    Write-Warning "The release will include uncommitted working-tree changes."
}

$releaseId = Get-Date -Format "yyyyMMdd-HHmmss"
$stagingRoot = Join-Path $repositoryRoot ".artifacts\linux-deploy\$releaseId"
$apiOutput = Join-Path $stagingRoot "Api"
$webOutput = Join-Path $stagingRoot "Web"
$remoteRelease = "$RemoteRoot/releases/$releaseId"
$previousRelease = ((Invoke-Ssh -Command "readlink -f '$RemoteRoot/current'" `
    -CaptureOutput) -join "`n").Trim()
$switchedRelease = $false

try {
    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

    Write-Host "Publishing API for $Runtime..."
    Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
        "publish", $apiProject,
        "-c", "Release",
        "-r", $Runtime,
        "--self-contained", "false",
        "-o", $apiOutput
    ) -FailureMessage "API publish failed"

    Write-Host "Publishing website for $Runtime..."
    Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
        "publish", $webProject,
        "-c", "Release",
        "-r", $Runtime,
        "--self-contained", "false",
        "-o", $webOutput
    ) -FailureMessage "Website publish failed"

    $commit = ((git -C $repositoryRoot rev-parse HEAD) -join "`n").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine the Git commit."
    }
    $manifest = @{
        releaseId = $releaseId
        createdAtUtc = [DateTime]::UtcNow.ToString("O")
        commit = $commit
        dirty = ($workingTreeChanges.Count -gt 0)
        runtime = $Runtime
        deployedFrom = $env:COMPUTERNAME
    } | ConvertTo-Json
    [IO.File]::WriteAllText(
        (Join-Path $stagingRoot "release.json"),
        $manifest,
        [Text.UTF8Encoding]::new($false))

    Write-Host "Uploading release $releaseId..."
    Invoke-Ssh -Command (
        "if [ -e '$remoteRelease' ]; then " +
        "echo 'Release already exists: $remoteRelease' >&2; exit 20; fi; " +
        "mkdir -p '$remoteRelease'"
    )
    $scpArguments = @(
        "-i", $IdentityFile,
        "-P", $SshPort,
        "-o", "IdentitiesOnly=yes",
        "-o", "BatchMode=yes",
        "-r",
        $apiOutput,
        $webOutput,
        (Join-Path $stagingRoot "release.json"),
        "${remoteTarget}:$remoteRelease/"
    )
    Invoke-NativeCommand -FilePath "scp" -Arguments $scpArguments `
        -FailureMessage "Release upload failed"
    Invoke-Ssh -Command (
        "test -f '$remoteRelease/Api/AzerothCore-UI.Api.dll' && " +
        "test -f '$remoteRelease/Web/AzerothCore-UI.Web.dll' && " +
        "test -f '$remoteRelease/release.json' && " +
        "chmod -R a+rX '$remoteRelease'"
    )

    Write-Host "Activating release $releaseId..."
    Set-RemoteCurrentRelease -ReleasePath $remoteRelease
    $switchedRelease = $true
    Restart-RemoteApplications
    Test-RemoteReadiness

    if (-not $SkipPublicHealthCheck -and -not [string]::IsNullOrWhiteSpace($PublicUrl)) {
        try {
            $response = Invoke-WebRequest `
                -Uri ($PublicUrl.TrimEnd('/') + "/health/ready") `
                -UseBasicParsing -TimeoutSec 20
            if ($response.StatusCode -ne 200) {
                throw "HTTP $($response.StatusCode)"
            }
        }
        catch {
            Write-Warning "The release is healthy internally, but the public HTTPS check failed: $($_.Exception.Message)"
        }
    }

    Write-Host "Deployment complete: $remoteRelease"
    Write-Host "Previous release retained for rollback: $previousRelease"
}
catch {
    $deploymentError = $_
    if ($switchedRelease -and -not [string]::IsNullOrWhiteSpace($previousRelease)) {
        Write-Warning "Deployment failed after activation. Restoring $previousRelease..."
        try {
            Set-RemoteCurrentRelease -ReleasePath $previousRelease
            Restart-RemoteApplications
            Test-RemoteReadiness
            Write-Warning "Rollback completed successfully."
        }
        catch {
            Write-Error "Automatic rollback also failed: $($_.Exception.Message)"
        }
    }
    throw $deploymentError
}
