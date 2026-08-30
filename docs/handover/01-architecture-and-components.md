# Architecture and components

## Repository projects

- `AzerothCore-UI.Api`: ASP.NET Core API for database, SOAP, authorization, audit, backups, character, companion, dungeon, crafting, and server operations.
- `AzerothCore-UI.Web`: Blazor UI with shared character cards, pickers, tools, companion controls, Artisan Gearing Room, and addon download page.
- `AzerothCore-UI.Api.Tests` and `AzerothCore-UI.Web.Tests`: automated tests (currently 201 and 58 passing).
- `server-modules/mod-web-admin`: C++ AzerothCore bridge and worldserver commands, including caster auto-attack.
- `server-modules/mod-playerbots-patches`: optional PlayerBots patches; read each patch's README before applying.
- `client-addons/AzerothCompanion`: WoW 3.3.5a companion UI and caster controls.
- `database`: UI schema and repeatable feature SQL; AzerothCore's auth/characters/world schemas remain owned by AzerothCore updates.
- `deploy/windows` and `deploy/linux`: publishing, services, Caddy, configuration, and addon packaging.

## Runtime topology

```text
Browser -> Caddy :443 -> Web 127.0.0.1:5211 -> API 127.0.0.1:5202 -> MySQL :3306
WoW clients -> AzerothCore authserver/worldserver on azerothmedia
```

Only Caddy should be internet-facing. Keep API, SOAP, MySQL, and server administration ports private.

## Linux services

```text
azerothcore-auth.service       /opt/azerothcore/server/bin/authserver
azerothcore-world.service      /opt/azerothcore/server/bin/worldserver
azerothcore-ui-api.service     dotnet API on 127.0.0.1:5202
azerothcore-ui-web.service     dotnet Web on 127.0.0.1:5211
caddy.service                  HTTPS reverse proxy on :443
```

Worldserver runs as `azerothcore`; UI services run as `azerothcore-ui`. Core configuration is under `/opt/azerothcore/server/etc`; UI external configuration is under `/etc/azerothcore-ui`.

## Features

The site provides account/role management and scoping, audit trail, backups, module/game settings, shared online/offline/bot-aware character cards, multi-target player actions, item/place/NPC pickers, teleports/summons/spawning, training/professions, character services, dungeon assistant/library, quest helpers, companion groups/commands/diagnostics/logistics, auction-house views, collectibles, and the Artisan Gearing Room. The addon mirrors companion status and commands in-game.

## Caster auto-attack

The player-only opt-in command is:

```text
.casterauto toggle <spellId>
.casterauto stop
.casterauto status
```

`CasterAuto.lua` chooses learned filler spells, adds a target-frame control, and can issue the command from a short right-click on a hostile target. It pauses while moving/manual casting and stops when the target changes, dies, or leaves range. Both the addon and rebuilt Linux worldserver module are required.
