# Build, deploy, and recovery runbook

## Windows development

```powershell
dotnet restore .\AzerothCore-UI.slnx
dotnet build .\AzerothCore-UI.slnx --no-restore
dotnet test .\AzerothCore-UI.Api.Tests\AzerothCore-UI.Api.Tests.csproj --no-restore
dotnet test .\AzerothCore-UI.Web.Tests\AzerothCore-UI.Web.Tests.csproj --no-restore
```

Run with Visual Studio or `dotnet run`; inspect each project's `launchSettings.json`. Stop running API/Web processes before rebuilding to avoid locked `apphost.exe` errors.

## Secrets/configuration

Do not commit production secrets. Required external configuration values are AzerothCore read/write, maintenance, and UI database connection strings; SOAP endpoint/user/password; and the module/API key. Use ASP.NET user-secrets for development or `deploy/windows/New-ProductionConfiguration.ps1` for external production JSON. On Linux inspect names and permissions without printing contents:

```bash
sudo ls -l /etc/azerothcore-ui
sudo systemctl cat azerothcore-ui-api.service
```

## Linux UI deployment

```powershell
.\deploy\linux\Deploy-To-Linux.ps1
```

Defaults target `mark@azerothmedia` using `$env:USERPROFILE\.ssh\azerothcore_beelink`, upload timestamped releases under `/opt/azerothcore/admin/releases`, activate `/opt/azerothcore/admin/current`, restart UI services, and validate health. `-SkipPublicHealthCheck` is useful when DNS/forwarding is unavailable. The previous release is retained for rollback.

### UI rollback

The deployment script records the active release and previous release. If activation or readiness fails, it automatically switches the symlink back and restarts both UI services. To manually roll back, first list retained releases and identify the desired timestamp:

```bash
ls -1dt /opt/azerothcore/admin/releases/*
readlink -f /opt/azerothcore/admin/current
```

Then atomically switch the `current` symlink and restart the API/Web services:

```bash
release=/opt/azerothcore/admin/releases/YYYYMMDD-HHMMSS
ln -s "$release" /opt/azerothcore/admin/current.next
mv -Tf /opt/azerothcore/admin/current.next /opt/azerothcore/admin/current
sudo systemctl restart azerothcore-ui-api.service azerothcore-ui-web.service
curl -fsS http://127.0.0.1:5202/health/ready
curl -fsS http://127.0.0.1:5211/health/ready
```

Do not delete the known-good release until the replacement has been validated. The game worldserver is independent of UI release rollback; use the separate binary backup procedure for C++ changes.

## Dynu dynamic DNS

The production hostname is `azerothcore.ddnsfree.com`. The updater is installed on Linux as a systemd timer:

```text
dynu-update.service   one update invocation (normally inactive between runs)
dynu-update.timer     enabled periodic trigger
/etc/dynu-update.conf credentials/configuration (secret; never print or commit)
```

Read-only checks:

```bash
systemctl status dynu-update.timer --no-pager
systemctl list-timers dynu-update.timer --no-pager
systemctl show dynu-update.service -p ExecStart -p User -p FragmentPath
sudo stat -c '%a %U %G %n' /etc/dynu-update.conf
```

For failures, inspect `journalctl -u dynu-update.service` but redact URLs/query strings before sharing. Validate DNS and the current public address separately:

```bash
getent hosts azerothcore.ddnsfree.com
curl -sS https://api.ipify.org; echo
```

Dynu only maintains DNS; router TCP 443 forwarding and Caddy HTTPS are separate.

## AzerothCore/module build

Do not rebuild the Windows core when Linux is the active host. For source-only C++ module changes:

```bash
cmake --build /opt/azerothcore/build --target worldserver -j2
```

Check the service path, back up the binary, stop only worldserver, install, and restart:

```bash
systemctl cat azerothcore-world.service
sudo cp /opt/azerothcore/server/bin/worldserver /opt/azerothcore/server/bin/worldserver.pre-change-YYYYMMDD-HHMMSS
sudo systemctl stop azerothcore-world.service
sudo cp /opt/azerothcore/build/src/server/apps/worldserver /opt/azerothcore/server/bin/worldserver
sudo chown azerothcore:azerothcore /opt/azerothcore/server/bin/worldserver
sudo chmod 755 /opt/azerothcore/server/bin/worldserver
sudo systemctl start azerothcore-world.service
systemctl is-active azerothcore-world.service
```

Regenerate CMake only when build structure changes. Never run concurrent builds. Avoid restarts while real players are active.

## Addon packaging

```powershell
.\deploy\windows\Package-ClientAddons.ps1
```

The web package contains `AzerothCompanion.toc`, `AzerothCompanion.lua`, `CasterAuto.lua`, and `README.md`. Install under the WoW client's `Interface\AddOns` and `/reload` or restart the client.

## Database safety

Always back up before live SQL. Verify the dump before applying changes. UI SQL is intended to be repeatable, but AzerothCore auth/characters/world updates must match the exact core revision. Never guess destructive queries.

## Diagnostics

```bash
systemctl status azerothcore-auth azerothcore-world azerothcore-ui-api azerothcore-ui-web caddy --no-pager
journalctl -u azerothcore-world -n 100 --no-pager
journalctl -u azerothcore-ui-api -n 100 --no-pager
curl -fsS http://127.0.0.1:5202/health/ready
curl -fsS http://127.0.0.1:5211/health/ready
```

If an executable reports `Text file busy`, stop its owning service before replacing it.
