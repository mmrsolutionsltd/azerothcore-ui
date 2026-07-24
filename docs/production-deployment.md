# Production deployment on Windows

The supported home-hosting layout exposes only Caddy on TCP port 443.
Caddy terminates public HTTPS and proxies Blazor Server, including its WebSocket
connection, to `127.0.0.1:5211`. The private API listens only on
`127.0.0.1:5202`. MySQL and AzerothCore SOAP remain local.

## Prerequisites

- A domain or dynamic-DNS hostname controlled by the server owner.
- A DHCP reservation for the Windows server's LAN address.
- A public IPv4 address or working IPv6 inbound routing.
- A router TCP forward for 443 only.
- .NET 10 Hosting Bundle/runtime.
- Caddy for Windows at `C:\Caddy\caddy.exe`.
- An elevated PowerShell session for installing services and firewall rules.

Do not create router forwards until the accounts, local readiness checks,
hostname, and Caddy configuration have all been validated.

For an existing local development installation whose API user-secrets are
complete, the entire local deployment can be performed from one elevated
launcher:

```powershell
.\deploy\windows\Deploy-LocalProduction.ps1 `
  -Hostname azerothcore.ddnsfree.com `
  -Email mmrsolutionsltd@gmail.com
```

The launcher requests UAC elevation, migrates existing secrets into ACL-protected
production files, downloads and verifies the latest official Caddy Windows
release, publishes the applications, and installs all three services. It does
not alter router configuration.

## 1. Publish an immutable release

From the repository root:

```powershell
.\deploy\windows\Publish-Production.ps1
```

The script publishes both applications into a timestamped directory below
`C:\ProgramData\AzerothCore-UI\releases` and writes a release manifest containing
the Git commit. Existing releases remain available for rollback.

## 2. Generate protected configuration

```powershell
.\deploy\windows\New-ProductionConfiguration.ps1 `
  -Hostname wow-admin.example.com
```

The script prompts for database and SOAP secrets without echoing them, generates
a 256-bit web-to-API key, writes separate API and Web configuration files, and
removes inherited filesystem access. Never commit the generated files.

## 3. Install or update Windows services

Use the release identifier printed by the publish command:

```powershell
.\deploy\windows\Install-Services.ps1 -ReleaseId 20260724-180000
```

The API and Web services start automatically, depend on the appropriate
upstream service, restart after failures, and bind only to loopback. Updating is
performed by publishing a new release and rerunning this command with the new
identifier. Rollback uses the same command with the previous identifier.

Confirm local readiness:

```powershell
Invoke-WebRequest http://127.0.0.1:5202/health/ready
Invoke-WebRequest http://127.0.0.1:5211/health/ready
```

## 4. Configure Caddy

After the hostname resolves to the home's public address and TCP port 443 is
forwarded:

```powershell
.\deploy\windows\Install-Caddy.ps1 `
  -Hostname wow-admin.example.com `
  -Email owner@example.com
```

Caddy validates its configuration before installing/updating its Windows
service. The firewall rule permits only web ports on the Private profile. Caddy
automatically obtains and renews the public certificate.

## 5. Validate DNS, HTTPS, and CGNAT

Find the WAN/Internet address displayed by the router, then run:

```powershell
.\deploy\windows\Test-ProductionDeployment.ps1 `
  -Hostname wow-admin.example.com `
  -RouterWanAddress 203.0.113.10
```

If the router's WAN address is private (`10/8`, `172.16/12`, `192.168/16`, or
`100.64/10`) or differs from the address reported by the script, the connection
may be behind CGNAT. Ordinary IPv4 port forwarding will not work until the ISP
provides a public address; an HTTPS tunnel is the non-VPN fallback.

Run the final public HTTPS check from a device outside the home network, such as
a phone with Wi-Fi disabled. Confirm that no router administration page, RDP,
MySQL, SOAP, API port, or AzerothCore administration port is reachable publicly.

## Secret and recovery requirements

- Back up `C:\ProgramData\AzerothCore-UI\config` and `keys` securely.
- Never publish those directories to source control.
- Keep the latest verified four-database backup off the server.
- Test rollback to a prior application release before opening router ports.
- Rotate the service key and administrative passwords after suspected exposure.
