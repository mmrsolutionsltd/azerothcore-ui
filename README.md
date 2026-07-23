# AzerothCore UI

A local, password-protected Blazor administration website for an AzerothCore PlayerBots server. It combines read-only account and character browsing with a deliberately limited set of server, character, PlayerBots, gameplay, and module administration tools.

The solution targets .NET 10 and contains:

- `AzerothCore-UI.Web` — the Blazor interactive-server user interface.
- `AzerothCore-UI.Api` — the local API, MySQL queries, configuration editing, process control, and SOAP integration.
- `AzerothCore-UI.Api.Tests` — unit tests for configuration and SOAP behaviour.
- `server-modules/mod-web-admin` — the AzerothCore module that supplies console-safe commands required by several UI features.

## Features

### Accounts and characters

- Search, filter, sort, and page through human and random-PlayerBot accounts.
- See character counts, online counts, last-login information, and GM access level.
- Enable or remove GM access with confirmation safeguards.
- Browse account characters and open a detailed character view.
- See character identity, level, race, class, money, played time, location, and live online status.
- Inspect equipped gear, durability, backpack contents, and equipped bags.
- Browse active and completed quests.
- Inspect professions, skill levels, learned recipes, and available training.
- Find missing profession recipes from trainers, vendors, quests, creature or game-object drops, and unclassified item sources.
- Review class training, profession training, costs, requirements, and reputation.

### Server administration

- Start, stop, and restart `authserver.exe` and `worldserver.exe` from the configured server directory.
- Prefer a graceful SOAP shutdown, with explicit confirmation before forced termination.
- Show process state, PID, start time, memory use, SOAP availability, recent logs, and online human/PlayerBot population.
- Refresh live player online/offline state after logins, logouts, and administration commands.

### Player actions

- Select one or more real-player characters as shared action targets.
- Search the world item catalogue and give an item directly to the selected online characters.
- Mail items to selected online or offline characters.
- Send gold, silver, and copper to all selected characters through in-game mail.
- Search saved AzerothCore teleport destinations and teleport all selected characters.
- Move all selected online characters safely to one single online anchor character.
- Apply temporary walk, run, swim, and flight speed multipliers from `0.5x` to `10x` to selected online characters.
- Report success or failure separately for every character in a batch.
- Show real players only in ordinary character pickers.

### PlayerBots and dungeon assistance

- Enable PlayerBots and random-bot autologin.
- Configure minimum and maximum random-bot population from 0 to 5,000.
- Configure random-bot level range, dungeon-finder participation, battleground participation, and trading.
- Inspect a real player's current party and eligible nearby-level PlayerBots.
- Add or remove individual bots, clear all party bots, or auto-fill a five-player role-aware party.
- Search supported dungeon destinations, review level recommendations, and launch the complete party into a dungeon.
- Reject unsafe group, battleground, arena, flight, transport, combat, and cross-instance operations in the worldserver module.

### Gameplay rates

Edit the following `worldserver.conf` multipliers from the website:

- Kill, quest, and exploration XP.
- Reputation gain.
- Money drops and quest money.
- Honor gain.
- Repair cost.

Changed configuration files are backed up before replacement. Rate changes take effect after a worldserver restart.

### Module settings

The UI supports the installed family-server modules below:

- **Auction House Bot:** seller and buyer behaviour, market pricing, item sources, stack behaviour, processing volume, and duplicate limits.
- **AutoBalance:** global enablement, dungeon/heroic/raid minimum players, health and damage multipliers, level scaling, reward scaling, and announcements.
- **Transmog:** collection and portable modes, quality and requirement rules, mixed armour/weapons, costs, and saved sets.
- **AoE Loot:** enablement, messages, group support, and loot range.

Each module configuration is validated, concurrency-checked, and backed up before a changed file is saved. A worldserver restart is required.

### Character services

Run confirmed, allowlisted AzerothCore services for a named character:

- Force rename or appearance customization.
- Enable race or faction change.
- Reset talents and pet talents, or reset learned spells for an online character.
- Revive a dead character.
- Return a character to its bound inn.
- Set character level.
- Select multiple real-player characters and process each service independently with per-character results.

Character pickers throughout the administration UI list real-player characters only. PlayerBots remain available in the dedicated PlayerBots and dungeon-party workflows.

### Weapon training

- Inspect every supported weapon proficiency and current/max skill for an online character.
- Grant a missing proficiency through a confirmed and audited worldserver command.
- Warn that this is an administrative override and may bypass normal class-trainer restrictions.

### Trainer finder

- Find class, profession, weapon, riding, and stable trainers for a selected character.
- Restrict class-trainer results to the character's actual class, including mod-arac combinations.
- Sort trainers on the character's current map by distance before trainers on other maps.
- Search trainer names and disciplines, then teleport to the exact selected NPC spawn.
- Support immediate online teleporting and next-login positioning for offline characters.

### Mounts and companions

- Search collectible mounts and companion pets.
- Compare the catalogue with a selected character's learned collection.
- Filter to missing collectibles.
- Deliver one collectible or mail up to ten selected missing collectibles at once.

### Temporary creature spawner

- Search safe creature templates by name, family, level range, and tameable/exotic status.
- Spawn a selected creature beside an online player at a chosen level for 1–30 minutes.
- Recalculate stats when using an allowed level outside the template's natural range.
- Keep spawns runtime-only; the feature does not insert permanent world-database records.
- Reject service NPCs, world bosses, instances, battlegrounds, transports, combat, flight, and other unsafe targets or states.

### Training dashboard

- Summarise currently available class and profession training across player characters.
- Search by account or character and filter by training type or discipline.
- Sort by character, account, level, option count, total cost, or next requirement.
- Show aggregate training counts and costs with expandable per-character details.

## Safety model

This is an administration application and should not be exposed directly to the public Internet.

- Administrative pages require the configured cookie-authentication password.
- Administration API endpoints accept loopback requests only.
- AzerothCore SOAP must use a loopback endpoint and stored credentials.
- The browser cannot submit arbitrary SOAP, console, SQL, PowerShell, or executable commands.
- Player names, locations, numeric ranges, stale configuration versions, and destructive operations are validated.
- Configuration updates preserve unmanaged settings, create timestamped backups, and replace files atomically.
- Temporary creature spawning and player-relative movement are implemented inside the worldserver rather than by editing live character coordinates in MySQL.
- Administrative actions are written to application logs with the `ADMIN AUDIT` prefix.

See [docs/server-administration.md](docs/server-administration.md) for detailed local security and SOAP setup.

## Configuration

The development defaults expect the deployed server at:

```text
C:\AzerothServer-PlayerBots
```

Set the website administrator password with user secrets:

```powershell
dotnet user-secrets set "Administration:Password" "<strong-password>" --project .\AzerothCore-UI.Web
```

Set the API's AzerothCore database connection and SOAP credentials without committing secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:AzerothCore" "<connection-string>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "AzerothCore:Soap:Username" "<soap-account>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "AzerothCore:Soap:Password" "<soap-password>" --project .\AzerothCore-UI.Api
```

The non-secret SOAP endpoint and server root are configured under `AzerothCore` in `AzerothCore-UI.Api/appsettings.Development.json`. The web project's `ApiBaseUrl` must point to the API.

## Custom worldserver module

Several safe runtime operations depend on `server-modules/mod-web-admin`, including player-relative movement, movement speed, weapon training, PlayerBot party management, dungeon launching, and temporary creature spawning.

Copy the module into the AzerothCore `modules/mod-web-admin` directory, regenerate the CMake build if necessary, rebuild the worldserver, and install the rebuilt executable. See [server-modules/mod-web-admin/README.md](server-modules/mod-web-admin/README.md) for its commands and restrictions.

## Build and test

From the repository root:

```powershell
dotnet restore .\AzerothCore-UI.slnx
dotnet build .\AzerothCore-UI.slnx
dotnet test .\AzerothCore-UI.slnx
```

For local development, start both projects using the solution launch profile or run the API and web projects separately. Keep the API on loopback and confirm that `ApiBaseUrl`, the SOAP endpoint, and the configured AzerothCore server directory match the local installation.
