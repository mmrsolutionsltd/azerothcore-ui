# Operations cookbook

These examples use prompts or environment variables for credentials. Never put passwords in command history or committed files.

## Connect and inspect

```powershell
$key = "$env:USERPROFILE\.ssh\azerothcore_beelink"
ssh -i $key mark@azerothmedia
ssh -i $key mark@azerothmedia "systemctl is-active azerothcore-world"
```

```bash
mysql -u root -p -e "SHOW DATABASES;"
mysql -u root -p acore_world -e "SHOW TABLES;"
mysql -u root -p acore_characters -e "SELECT name, level FROM characters ORDER BY level DESC LIMIT 20;"
curl -fsS http://127.0.0.1:5202/health/ready
curl -fsS http://127.0.0.1:5211/health/ready
```

Use `-p` without a value so MySQL prompts. Do not use `-pPASSWORD`.

## Backup

```bash
mysqldump --single-transaction --routines --events -u root -p acore_auth acore_characters acore_world azerothcore_ui > /var/backups/azerothcore-$(date +%Y%m%d-%H%M%S).sql
```

Verify the dump before any restore; use the website backup workflow where possible.

## Server paths and build

```text
/opt/azerothcore/source/core                 source and modules
/opt/azerothcore/build                        CMake/Ninja build tree
/opt/azerothcore/server/bin                   installed authserver/worldserver
/opt/azerothcore/server/etc                   core configuration
/opt/azerothcore/admin/releases               timestamped UI releases
/opt/azerothcore/admin/current                active UI release
/etc/azerothcore-ui                           protected UI configuration
```

```bash
git -C /opt/azerothcore/source/core status --short
git -C /opt/azerothcore/source/core rev-parse --short HEAD
cmake --build /opt/azerothcore/build --target worldserver -j2
```

Install a rebuilt binary only after backing it up and stopping the service:

```bash
sudo cp /opt/azerothcore/server/bin/worldserver /opt/azerothcore/server/bin/worldserver.pre-change-$(date +%Y%m%d-%H%M%S)
sudo systemctl stop azerothcore-world.service
sudo cp /opt/azerothcore/build/src/server/apps/worldserver /opt/azerothcore/server/bin/worldserver
sudo chown azerothcore:azerothcore /opt/azerothcore/server/bin/worldserver
sudo chmod 755 /opt/azerothcore/server/bin/worldserver
sudo systemctl start azerothcore-world.service
```

Never run concurrent builds. `Text file busy` means the service still owns the executable.

## Deploy website/addon

```powershell
.\deploy\linux\Deploy-To-Linux.ps1
.\deploy\windows\Package-ClientAddons.ps1
```

The addon goes under `C:\TheraWoW wotlk\Interface\AddOns\`; use `/reload` or restart WoW.

## Logs and SOAP diagnostics

```bash
systemctl status azerothcore-auth azerothcore-world azerothcore-ui-api azerothcore-ui-web caddy --no-pager
journalctl -u azerothcore-world -n 200 --no-pager
journalctl -u azerothcore-ui-api -n 200 --no-pager
journalctl -u dynu-update.service -n 50 --no-pager
```

SOAP is normally localhost-only and should be used through the API. Supply its URL and credentials at runtime; never expose or print them.
