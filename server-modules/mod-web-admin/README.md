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
webadmin companion logistics <leader> <companion> <trigger> <target> <auto> [category recipient keep]...
webadmin companion logistics-preview <leader> <companion> [category recipient keep]...
webadmin companion logistics-run <leader> <companion>
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
  professions. Active companions additionally scan nearby quest givers every two seconds,
  so these personal quests do not depend on the leader accepting or opening the quest.
  All normal race, class, level, reputation, prerequisite, proximity, and
quest-log checks still apply. Companion startup enables PlayerBots' non-combat `loot`
and `follow` strategies and clears stale passive/stay state. If the leader dies, the
bridge records that state and restores normal companion activity when the leader becomes
alive again, including when an administrator uses the website's revive service instead
of reclaiming a corpse normally. The inspect command reports the loot strategy,
free/total inventory slots, and per-character item and kill objective progress so the
website can compare a companion's progress with its leader. Active companions also scan
once per second for needed quest-item chests within 40 metres, explicitly move to and
open them, and expose the current collection stage through the inspect command.
While the leader and companion are alive and out of combat, companions also check every
two seconds for interactable nearby vendors and repairers. Each pass can sell up to six
different item types: poor-quality (grey) items, white equipment that is unusable or no
longer an upgrade, and permanently unusable green or blue equipment when no enchanter
route is available. When free bag space is at or below the configured refill target, it
also sells items that a freshly recalculated PlayerBots decision marks as auction or
vendor surplus, unless a material or catch-all mail route protects them. Profession
tools, active quest items, bags, keys and recipes remain protected. The companion also
repairs damaged equipment. Every ten seconds, an out-of-combat companion also checks for an interactable
profession trainer. It automatically buys all currently available training for
professions it already knows, using the normal skill, level, prerequisite, reputation,
and money rules. It never learns a new profession or class abilities this way.
Protocol-v2 inspection also reports equipped items, complete bag contents,
durability, recent equipment changes and session equipment-protection state. A protected
item is restored if PlayerBots tries to replace it while the companion is alive and out
of combat. Protection intentionally lasts only for the current companion session.
The reset command clears transient AI targets and movement, then restores companion
follow, combat, loot and quest synchronisation; it is rejected during combat.
Protocol v3 adds session-scoped behaviour presets and custom controls. Role can be
auto, tank, healer, or damage; tank and healer are rejected unless the companion's
current specialization supports them. Movement can follow or stay, combat focus can
prioritize the leader's selected target or let PlayerBots defend the party naturally,
and loot, quest-object gathering, junk selling, and repair can each be toggled. Junk
selling includes all grey items plus white armour and weapons the companion cannot use
or which are no longer upgrades. Profession tools—including
  mining picks, skinning knives, crafting tools, enchanting rods, and fishing poles—are
  always retained. Higher-level usable gear, trade goods, recipes, consumables, and quest
  requirements are retained.
Follow distance is adjustable from 1 to 20 metres. Regroup clears transient AI state
and resumes following. Behaviour changes are rejected during combat and do not alter
talents, gear, or permanent character data.
Protocol v4 adds companion bag logistics. The website persists routes for cloth,
leather, metal and stone, herbs, enchanting materials, jewelcrafting materials, meat,
elemental materials, and engineering parts. Automatic processing begins only when free
bag slots reach the configured trigger and stops at the target. Manual processing can
route eligible surplus at any time. Both modes require the companion to be alive, out
of combat, and within normal interaction distance of a spawned mailbox. Only complete
tradable stacks above the per-item reserve are sent; active quest requirements,
conjured and timed items, equipment, and non-tradable items are excluded. Recipients
must exist, be same-faction, and have mailbox capacity. Normal postage and configured
cross-account delivery delay apply.
Protocol v5 adds an optional catch-all recipient. Explicit material routes run first;
the catch-all then receives otherwise unrouted, tradable items that PlayerBots classifies
as vendor or auction surplus. Useful equipment, skill and quest items, consumables,
containers, conjured or timed items, non-tradable items, grey trash, and permanently
incompatible white equipment are excluded. The latter two are handled by vendor cleanup.
The logistics preview command is read-only. It reports the same attachment selection
used by a manual run, vendor-cleanup candidates, protected and retained stacks, decision
reasons, destinations, postage, nearby mailbox/vendor availability, and potential free
bag space. It does not move, mail, sell, or otherwise alter an item.
Protocol v6 adds automatic profession routing without website setup. When a companion
starts and has no explicit website policy, the bridge selects the highest-skilled,
same-faction recipient on the leader's game account: cloth goes to a tailor; leather to
a leatherworker; ore, bars, and stone to a blacksmith or jewelcrafter; herbs to an
alchemist or inscriptionist; enchanting materials and permanently unusable green
equipment to an enchanter. Explicit website routes replace these defaults. Automatic
routing starts at eight free slots and works toward twelve; it still requires a nearby
mailbox and obeys the normal item protections and postage rules.
The Gather toggle controls PlayerBots' gathering strategy as well as the bridge's
needed quest-object scan. The bridge scans within 40 metres; normal herb, mineral,
skinning, and loot travel uses `AiPlayerbot.LootDistance`, which should also be set to
`40.0` in `playerbots.conf` for matching behaviour.

Real-player warlocks that know both Summon Imp and Summon Voidwalker may have both
demons active. The demon summoned normally remains the controllable pet-bar pet; the
other is created as an automatically managed guardian. It follows the warlock,
assists the normal pet's target, uses level-appropriate Firebolt or Torment, and
despawns when the normal pet is dismissed, the warlock dies, or the player logs out.
A slain additional guardian returns after 30 seconds once the warlock is out of
combat. Real players and active website questing companions receive the feature;
unrelated random PlayerBots are excluded to avoid changing the balance and load of
the random-bot population. This is entirely server-side and requires no database,
DBC, MPQ, or client-addon change.

The bundled `AzerothCompanion` 3.3.5a client addon uses AzerothCore's authenticated
addon-command channel to request this same snapshot in game. A normal player may run
the inspect command only for their own online character; SOAP/console administrators
retain the ability to inspect a named leader. Starting and dismissing companions remain
administrator-only operations. Inspect responses begin with
`WEBADMIN_COMPANION_PROTOCOL\t7`, allowing the addon and website to identify a missing
or incompatible bridge without guessing from partial response data.
Protocol v7 also reports the latest automatic sale or logistics-mail action for the
compact in-game companion bar.

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

Companion vendor cleanup also relies on
`../mod-playerbots-patches/item-link-parser-position.patch`. It changes the
PlayerBots item-link parser's scan offset from 8-bit to `size_t`, preventing
long item-link command strings from wrapping the offset and locking the world
update thread. Apply it from the `mod-playerbots` repository with
`git apply <patch-path>` before rebuilding.

After the module is already present in the AzerothCore build, source-only changes only
require rebuilding the `worldserver` target and installing that binary; CMake
regeneration is only needed when the module or build structure changes.
