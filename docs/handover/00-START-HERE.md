# AzerothCore UI — Handover pack

This folder is the operational handover for the AzerothCore PlayerBots server and its Blazor administration site. Read this file first, then the numbered documents.

## Security boundary

Passwords, private SSH keys, SOAP credentials, database credentials, Dynu credentials, and TLS keys are intentionally excluded. Supply them at runtime from a password manager or interactive prompt. Former Windows password values were shared in conversation and should be rotated before public hosting.

## System at a glance

| Component | Current location |
|---|---|
| Repository | `C:\Users\markr\source\repos\AzerothCore-UI` |
| Linux host | `azerothmedia` / LAN `192.168.1.77` |
| Linux user | `mark` (services use dedicated users) |
| OS | Ubuntu Server 26.04 LTS, x86-64 |
| Hardware | HP EliteDesk 800 G4 Mini, 32 GB DDR4, 1 TB NVMe |
| Core source | `/opt/azerothcore/source/core` |
| Core build | `/opt/azerothcore/build` |
| Installed core | `/opt/azerothcore/server` |
| UI releases | `/opt/azerothcore/admin/releases` |
| API/Web | loopback `127.0.0.1:5202` / `127.0.0.1:5211` |
| Public web | `https://azerothcore.ddnsfree.com` through Caddy |
| DDNS | Dynu updater on `azerothmedia`, hostname `azerothcore.ddnsfree.com` |
| MySQL | local MySQL 8.4, port 3306 |
| WoW client | `C:\TheraWoW wotlk` (3.3.5a) |

## First checks

```powershell
git status --short
dotnet test .\AzerothCore-UI.Api.Tests\AzerothCore-UI.Api.Tests.csproj --no-restore
dotnet test .\AzerothCore-UI.Web.Tests\AzerothCore-UI.Web.Tests.csproj --no-restore
```

```bash
ssh -i ~/.ssh/azerothcore_beelink mark@azerothmedia
systemctl is-active azerothcore-auth azerothcore-world azerothcore-ui-api azerothcore-ui-web caddy
hostname -I
```

Read [01-architecture-and-components.md](01-architecture-and-components.md), then [02-build-deploy-and-recovery.md](02-build-deploy-and-recovery.md). The [operations cookbook](06-operations-cookbook.md), [WoW examples](07-wow-character-and-companion-examples.md), and [Blazor/API guide](08-blazor-and-api.md) cover day-to-day work.
For model/agent context, read [09-llm-and-agent-context.md](09-llm-and-agent-context.md).
GitHub details and safe authentication guidance are in [10-git-and-github.md](10-git-and-github.md).
