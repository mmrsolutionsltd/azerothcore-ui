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
- Open a real-player character dashboard with live status, class/status filters, current location, money, played time, quest and profession counts, active pet, and hearthstone destination.
- Jump directly from the dashboard to a character's inventory, quests, professions, or available training, or open the related administration tools.
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

- Use the same compact, searchable character picker across administration tools.
- Show online real-player characters by default, with optional offline-character and PlayerBot filters where supported.
- Select one or more characters as shared action targets on batch-capable screens.
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
- Run normal characters as questing companions, mirror eligible quests, enforce quest
  looting, and compare leader/companion item and kill progress with bag-space warnings.
  Inspect their equipment and complete bag contents, monitor durability and recent item
  changes, protect selected equipment for the session, reset stalled AI, and automatically
  sell grey items or repair when beside suitable NPCs. Apply Questing, Dungeon Tank,
  or Dungeon Healer behaviour presets; assign compatible roles; switch between follow
  and stay; choose leader-assist or party-defence combat focus; adjust follow distance;
  and independently toggle loot, gathering, selling, and repair from the website or
  companion addon.
- Use the bundled 3.3.5a `AzerothCompanion` addon for the same shared quest progress,
  bag capacity, loot state, and gathering status directly inside the game client.
- Download its versioned ZIP from the authenticated **Adventures > Client addons**
  page; the addon reports missing, timed-out, older, and newer server bridges clearly.
- Rank supported dungeon destinations for the current party and highlight the three best level matches.
- Review tank, healer, damage, party-size, and level readiness before launch.
- Show active instance lockouts and objective-linked dungeon quests, including which real players have them in progress or completed.
- Launch the complete party into a dungeon after reviewing readiness.
- Reject unsafe group, battleground, arena, flight, transport, combat, and cross-instance operations in the worldserver module.

### Database backups

- Create one verified recovery point containing `acore_auth`, `acore_characters`, `acore_world`, and the website's `azerothcore_ui` administration database.
- Use consistent transactional MySQL dumps and record file sizes and SHA-256 hashes in a manifest.
- Distinguish stronger offline snapshots from backups created while either AzerothCore server was running.
- Retain the latest 20 verified backups in the configured server backup directory.
- Restore only while both servers are stopped, after typing the exact backup identifier.
- Verify every restore file and automatically create a fresh safety backup immediately before restoration.
- Schedule daily or weekly backups at a chosen local time through an API-hosted background worker.
- Optionally defer scheduled backups until both AzerothCore servers are stopped.
- Configure retention from 1 to 100 backups and show the next run, last success, overdue state, failures, and recent activity.
- Prevent scheduled/manual backups and database restoration from overlapping.

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

### Auction House dashboard

- Show live Alliance, Horde, and neutral auction totals, including AHBot/player ownership and auctions expiring within an hour.
- Inspect stock distribution by item category and quality, with low or empty category warnings.
- Search and filter live listings by item, house, category, and quality, and sort by expiry, price, stack, seller, or item.
- Review stack size, current bid, buyout, seller, AHBot status, and time remaining without directly editing auction tables.
- Display the active AHBot configuration and link to its managed settings.
- Safely enable or resume the AHBot seller through its installed worldserver command; the bot then stocks during normal update cycles.

### Family starter presets

- Select up to ten real-player characters and preview every delivery before applying it.
- Use built-in New Character, Level 10, and Returning Player defaults, then adjust bags, money, heirlooms, hearthstones, food, drink, and class supplies.
- Choose three class-appropriate heirlooms: a weapon, shoulders, and chest.
- Supply bow-using hunters with arrows and warlocks with soul shards.
- Skip heirlooms and hearthstones already owned or waiting in mail, and only provide enough silk bags to reach the requested count.
- Give items directly to online characters and mail items to offline characters; starting money is always delivered through safe in-game mail.
- Recheck ownership and live status when the confirmed preset is applied, with per-action and per-character results.

### Server health and diagnostics

- Check authserver/worldserver process state, start time, memory, executable metadata, SOAP configuration, and SOAP reachability.
- Verify MySQL connectivity, all three AzerothCore databases, their update history, pending-update state, and core/database revisions.
- Check installed module source directories and required deployed configuration files.
- Verify the mod-arac client `Patch-A.MPQ`, its three server DBC files, and the compatible `player_totem_model` schema.
- Compare the newest C++ source timestamp with the deployed `worldserver.exe` and clearly report when a rebuild is required.
- Report missing configuration files, invalid PlayerBots ranges, and configuration changes made after the running worldserver started.
- Show the newest SQL/configuration backups and warn about missing or stale database backups.
- Group recent error, failure, and exception lines from the local server logs.
- Generate a downloadable plain-text diagnostic report with connection credentials, passwords, secrets, and tokens redacted.

### Character services

Run confirmed, allowlisted AzerothCore services for a named character:

- Force rename or appearance customization.
- Enable race or faction change.
- Reset talents and pet talents, or reset learned spells for an online character.
- Revive a dead character.
- Return a character to its bound inn.
- Set character level.
- Transfer one character to another human game account after creating a verified
  database backup, with account-scope, capacity, confirmation, and audit checks.
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

### Quest helper

- Show active quests, objective progress, failed quests, and quests ready to turn in for a selected real-player character.
- Recommend uncompleted quests in an appropriate level band while checking race, class, and direct prerequisite compatibility.
- Search eligible quests and prioritize quest givers on the character's current map by distance.
- Teleport an online or offline character to the exact selected quest-giver spawn.
- Add a missing quest or remove a broken quest through confirmed, validated, and logged SOAP commands.

### Mounts and companions

- Search collectible mounts and companion pets.
- Compare the catalogue with a selected character's learned collection.
- Filter to missing collectibles.
- Deliver one collectible or mail up to ten selected missing collectibles at once.

### Temporary creature spawner

- Use the compact creature-spawner tool from Player Actions with the page's
  shared single- or multi-character selection.
- Search safe creature templates by name, family, level range, and tameable/exotic status.
- Spawn 1–25 selected creatures at random positions in a configurable square centred
  on each selected online player, at a chosen level for 1–30 minutes.
- Configure a square from 1–200 metres per side.
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
See [docs/production-deployment.md](docs/production-deployment.md) for the
localhost-only API, Windows service, Caddy HTTPS, DNS, and CGNAT deployment
procedure.

## Configuration

The development defaults expect the deployed server at:

```text
C:\AzerothServer-PlayerBots
```

Set the API's AzerothCore database connection and SOAP credentials without committing secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:AzerothCore" "<connection-string>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "ConnectionStrings:AzerothCoreMaintenance" "<backup-and-restore-connection-string>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "AzerothCore:Soap:Username" "<soap-account>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "AzerothCore:Soap:Password" "<soap-password>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "ConnectionStrings:AzerothCoreUi" "<dedicated-azerothcore-ui-connection-string>" --project .\AzerothCore-UI.Api
```

Install the repeatable `database/azerothcore-ui-schema.sql` script using a MySQL
schema-administration account, then grant a dedicated application login only
`SELECT`, `INSERT`, `UPDATE`, and `DELETE` access to `azerothcore_ui`. On first
run, visit `/admin/setup` to create the initial Owner. That one-time route
automatically closes as soon as an account exists.

Protect web-to-API traffic with the same randomly generated service key in both
projects:

```powershell
$serviceKey = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Security:ApiKey" $serviceKey --project .\AzerothCore-UI.Web
dotnet user-secrets set "Security:ApiKey" $serviceKey --project .\AzerothCore-UI.Api
```

The non-secret SOAP endpoint and server root are configured under `AzerothCore` in `AzerothCore-UI.Api/appsettings.Development.json`. The web project's `ApiBaseUrl` must point to the API.

### Production security

Production startup fails unless both processes receive the same
`Security:ApiKey` containing at least 32 characters. Published applications do
not load development user-secrets,
so supply these through a protected deployment secret store or environment
variables:

```text
Security__ApiKey=<shared-web-to-api-service-key>
Security__DataProtectionKeysPath=<protected-persistent-directory>
AllowedHosts=<public-web-host-name>
```

Expose only the web application through an HTTPS reverse proxy. Bind the API to
loopback and do not expose the API, AzerothCore SOAP endpoint, MySQL, authserver,
or worldserver administration ports to the internet. The web login is limited to
five attempts per client address in each 15-minute window, uses antiforgery
tokens, and issues HTTP-only, same-site, HTTPS-only Production cookies. Give the
account running the web application exclusive access to the data-protection key
directory; those persisted keys keep authenticated sessions valid across normal
application restarts.

Website administrators use individual database-backed accounts with salted
PBKDF2-SHA256 password hashes, temporary lockout after repeated failures, and
revocable sessions. The **Access management** screen provides capability-based
roles covering each player, adventure, world, server, and security area.
The built-in Owner role always has every permission. Administrator has all
player, adventure, world, and access-management permissions but no server
control, settings, diagnostics, or backup permissions.

Each website user also has an AzerothCore account scope: all game accounts,
selected game accounts, or none. Character lists are filtered to that scope and
the API independently rejects account or character operations outside it,
including multi-character commands. Administrators cannot assign permissions or
game-account access broader than their own. Role changes revoke affected
sessions so updated claims take effect at the next sign-in.

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
