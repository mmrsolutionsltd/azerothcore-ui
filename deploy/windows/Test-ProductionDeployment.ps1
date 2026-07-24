[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Hostname,
    [string]$RouterWanAddress
)

$ErrorActionPreference = "Stop"
$results = [System.Collections.Generic.List[object]]::new()
function Add-Result($Name, $Passed, $Detail) {
    $results.Add([pscustomobject]@{ Check=$Name; Passed=$Passed; Detail=$Detail })
}

try {
    $api = Invoke-WebRequest "http://127.0.0.1:5202/health/ready" -UseBasicParsing -TimeoutSec 10
    Add-Result "Private API readiness" ($api.StatusCode -eq 200) "HTTP $($api.StatusCode)"
} catch { Add-Result "Private API readiness" $false $_.Exception.Message }
try {
    $web = Invoke-WebRequest "http://127.0.0.1:5211/health/ready" -UseBasicParsing -TimeoutSec 10
    Add-Result "Blazor readiness" ($web.StatusCode -eq 200) "HTTP $($web.StatusCode)"
} catch { Add-Result "Blazor readiness" $false $_.Exception.Message }

$addresses = @(Resolve-DnsName $Hostname -Type A -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty IPAddress)
$publicAddress = (Invoke-RestMethod "https://api.ipify.org?format=text" -TimeoutSec 10).Trim()
Add-Result "Public DNS" ($addresses -contains $publicAddress) `
    "DNS: $($addresses -join ', '); public address: $publicAddress"

if ($RouterWanAddress) {
    Add-Result "CGNAT comparison" ($RouterWanAddress -eq $publicAddress) `
        "Router WAN: $RouterWanAddress; public address: $publicAddress"
} else {
    Add-Result "CGNAT comparison" $false `
        "Enter the router's WAN/Internet address with -RouterWanAddress to check for CGNAT."
}

try {
    $https = Invoke-WebRequest "https://$Hostname/health/ready" -UseBasicParsing -TimeoutSec 15
    Add-Result "Public HTTPS" ($https.StatusCode -eq 200) "HTTP $($https.StatusCode)"
    Add-Result "HSTS" ($https.Headers["Strict-Transport-Security"] -ne $null) `
        ($https.Headers["Strict-Transport-Security"] -join ",")
} catch { Add-Result "Public HTTPS" $false $_.Exception.Message }

$results | Format-Table -AutoSize -Wrap
if ($results.Where({ -not $_.Passed }).Count -gt 0) { exit 1 }
