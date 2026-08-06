# AzerothCore Web Admin module

Adds the console/SOAP-safe administrator command:

```text
webadmin move <movingPlayer> <anchorPlayer>
webadmin speed <onlinePlayer> <0.5-10>
webadmin weapon inspect <onlinePlayer>
webadmin weapon learn <onlinePlayer> <weaponKey>
```

Both characters must be online. The command rejects battleground/arena destinations,
cross-instance movement, transports, self-movement, and characters already teleporting.

Dungeon-party commands:

```text
webadmin group inspect <leader>
webadmin group add <leader> <bot>
webadmin group remove <leader> <bot>
webadmin group clear <leader>
webadmin group fill <leader>
webadmin group launch <leader> <dungeonId>
webadmin dungeon list
webadmin creature spawn <anchorPlayer> <creatureId> <level> <despawnMinutes> [count squareSideLength]
webadmin companion inspect <leader>
webadmin companion start <leader> <companion>
webadmin companion dismiss <leader> <companion>
webadmin companion reset <leader> <companion>
webadmin companion protect <leader> <companion> <equipmentSlot> <on|off>
webadmin companion preset <leader> <companion> <questing|dungeon-tank|dungeon-healer>
webadmin companion behavior <leader> <companion> <preset> <role> <movement> <focus> <distance> <loot> <gather> <sell> <repair>
webadmin companion regroup <leader> <companion>
```

The leader must be a real online player. Eligible bots are online random PlayerBots that
are ungrouped, on the same faction, and within five levels. Parties are capped at five;
raid, LFG, battleground, and battlefield groups are not modified.

Creature spawns are runtime-only temporary summons. A command can spawn 1-25 creatures
at random positions in a square up to 200 metres per side, centred on the anchor.
Service NPCs, world bosses, instances, battlegrounds, transports, combat, flight, and
levels outside 1-83 are rejected. An administrator may override a template's natural
level range; each spawned creature's stats are recalculated. The four-argument form
remains available for the website's single utility-NPC summons.

Questing companions are normal characters controlled as PlayerBots. The leader must be
online, the companion offline, and they must be same-faction characters on different game
accounts. Starting a companion assigns the leader as its master and PlayerBots handles
party creation, following, combat, and quest synchronisation. The module mirrors the
leader's eligible accepted quests to active companions, including quests already in the
leader's log when a companion finishes logging in. When the leader talks to a nearby quest
giver, companions may also independently accept eligible quests for their own class or
professions. All normal race, class, level, reputation, prerequisite, proximity, and
quest-log checks still apply. Companion startup enables PlayerBots' non-combat `loot`
and `follow` strategies and clears stale passive/stay state. If the leader dies, the
bridge records that state and restores normal companion activity when the leader becomes
alive again, including when an administrator uses the website's revive service instead
of reclaiming a corpse normally. The inspect command reports the loot strategy,
free/total inventory slots, and per-character item and kill objective progress so the
website can compare a companion's progress with its leader. Active companions also scan
once per second for needed quest-item chests within 20 metres, explicitly move to and
open them, and expose the current collection stage through the inspect command.
While the leader and companion are alive and out of combat, companions also check every
five seconds for interactable nearby vendors and repairers, automatically sell
poor-quality (grey) items, and repair damaged equipment. White or better items are
retained. Protocol-v2 inspection also reports equipped items, complete bag contents,
durability, recent equipment changes and session equipment-protection state. A protected
item is restored if PlayerBots tries to replace it while the companion is alive and out
of combat. Protection intentionally lasts only for the current companion session.
The reset command clears transient AI targets and movement, then restores companion
follow, combat, loot and quest synchronisation; it is rejected during combat.
Protocol v3 adds session-scoped behaviour presets and custom controls. Role can be
auto, tank, healer, or damage; tank and healer are rejected unless the companion's
current specialization supports them. Movement can follow or stay, combat focus can
prioritize the leader's selected target or let PlayerBots defend the party naturally,
and loot, quest-object gathering, grey-item selling, and repair can each be toggled.
Follow distance is adjustable from 1 to 20 metres. Regroup clears transient AI state
and resumes following. Behaviour changes are rejected during combat and do not alter
talents, gear, or permanent character data.

The bundled `AzerothCompanion` 3.3.5a client addon uses AzerothCore's authenticated
addon-command channel to request this same snapshot in game. A normal player may run
the inspect command only for their own online character; SOAP/console administrators
retain the ability to inspect a named leader. Starting and dismissing companions remain
administrator-only operations. Inspect responses begin with
`WEBADMIN_COMPANION_PROTOCOL\t3`, allowing the addon and website to identify a missing
or incompatible bridge without guessing from partial response data.

For companions to collect their own quest items, use these PlayerBots settings:

```ini
AiPlayerbot.FreeMethodLoot = 1
AiPlayerbot.SyncQuestWithPlayer = 0
```

The first keeps bots looting in Free-for-All parties. The second prevents PlayerBots
from completing a bot's quest automatically when its leader hands in the same quest.

Quest items gathered from world objects also require the companion's PlayerBots build
to recognise generic interaction alternatives on profession-shaped locks. The local
server source includes the tracked
`../mod-playerbots-patches/quest-object-alternative-lock.patch`, which fixes objects
such as Doom Weed by preserving the generic lock alternative, selecting its matching
opening spell, and allowing protected-flag quest objects only when they contain an item
the bot currently needs. Genuine Herbalism or Mining nodes still require the relevant
profession.

The patch is stored without surrounding context so it remains whitespace-clean. Apply
it from the `mod-playerbots` repository with `git apply --unidiff-zero <patch-path>`.

Adjustable per-companion follow distance uses the tracked
`../mod-playerbots-patches/per-bot-follow-distance.patch`. Apply it from the
`mod-playerbots` repository with `git apply <patch-path>` before rebuilding.

After the module is already present in the AzerothCore build, source-only changes require
building `ALL_BUILD` and then `INSTALL`; CMake regeneration is only needed when the module
or build structure changes.
