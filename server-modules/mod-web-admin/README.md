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
webadmin creature spawn <anchorPlayer> <creatureId> <level> <despawnMinutes>
webadmin companion inspect <leader>
webadmin companion start <leader> <companion>
webadmin companion dismiss <leader> <companion>
```

The leader must be a real online player. Eligible bots are online random PlayerBots that
are ungrouped, on the same faction, and within five levels. Parties are capped at five;
raid, LFG, battleground, and battlefield groups are not modified.

Creature spawns are runtime-only temporary summons. Service NPCs, world bosses, instances,
battlegrounds, transports, combat, flight, and levels outside 1-83 are rejected. An administrator
may override a template's natural level range; the spawned creature's stats are recalculated.

Questing companions are normal characters controlled as PlayerBots. The leader must be
online, the companion offline, and they must be same-faction characters on different game
accounts. Starting a companion assigns the leader as its master and PlayerBots handles
party creation, following, combat, and quest synchronisation. The module mirrors the
leader's eligible accepted quests to active companions, including quests already in the
leader's log when a companion finishes logging in. When the leader talks to a nearby quest
giver, companions may also independently accept eligible quests for their own class or
professions. All normal race, class, level, reputation, prerequisite, proximity, and
quest-log checks still apply.

After the module is already present in the AzerothCore build, source-only changes require
building `ALL_BUILD` and then `INSTALL`; CMake regeneration is only needed when the module
or build structure changes.
