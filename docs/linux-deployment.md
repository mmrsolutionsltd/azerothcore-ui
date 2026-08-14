# Linux website deployment and Windows development

The live administration API and Blazor website run as private systemd services
on the AzerothCore Linux server. Caddy is the only public web process. It
proxies HTTPS requests to the website, while the API remains bound to Linux
loopback and is never exposed to the LAN or Internet.

The commands below are run from a PowerShell terminal in the repository on the
Windows development PC.

## Validate the installed server

Validate SSH, the release layout, both systemd units, and both private health
endpoints without publishing or restarting anything:

```powershell
.\deploy\linux\Deploy-To-Linux.ps1 -ValidateOnly
```

The defaults describe the home installation:

- SSH host: `azerothmedia`
- SSH user: `mark`
- SSH key: `%USERPROFILE%\.ssh\azerothcore_beelink`
- release root: `/opt/azerothcore/admin`
- public URL: `https://azerothcore.ddnsfree.com`

Each value can be overridden with a script parameter. For example, use
`-Server 192.168.1.77` if local DNS is temporarily unavailable.

## Publish a production update

```powershell
.\deploy\linux\Deploy-To-Linux.ps1
```

The script:

1. validates the existing installation before making changes;
2. publishes framework-dependent `linux-x64` API and Web applications;
3. records the Git commit and dirty-working-tree state in `release.json`;
4. uploads a new timestamped directory below
   `/opt/azerothcore/admin/releases`;
5. atomically changes `/opt/azerothcore/admin/current`;
6. restarts the API and Web systemd services;
7. waits for both private readiness endpoints; and
8. restores and restarts the previous release automatically if activation or
   readiness fails.

The script never replaces the protected configuration below
`/etc/azerothcore-ui`, changes the database, or restarts AzerothCore itself.
Existing releases are retained for manual rollback. Use
`-RequireCleanWorkingTree` when only committed source may be deployed, and
`-SkipPublicHealthCheck` when public DNS or HTTPS is intentionally unavailable.

## Run the website locally against Linux

```powershell
.\deploy\linux\Start-LinuxDevelopment.ps1
```

Then browse to `http://localhost:5311`. The launcher:

- retrieves the current service key over SSH into process memory only;
- forwards Windows `127.0.0.1:5302` to the private Linux API;
- starts only the Blazor Web project in Development; and
- closes the SSH tunnel when the website exits or Ctrl+C is pressed.

The local site controls the live server and live database, so its actions are
real. It has separate browser cookies from the production URL, but uses the same
administration accounts and audit trail. The API, MySQL, and SOAP ports remain
private on Linux.

Check credential retrieval and the tunnel without starting the website:

```powershell
.\deploy\linux\Start-LinuxDevelopment.ps1 -ValidateOnly
```

Use another local port if 5302 is occupied:

```powershell
.\deploy\linux\Start-LinuxDevelopment.ps1 -LocalApiPort 5303
```

Use `-NoBuild` after a successful build when only a quick restart is required.
