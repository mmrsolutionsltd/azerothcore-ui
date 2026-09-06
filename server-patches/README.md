# Core AzerothCore patches

Reference copies of small patches applied directly to the AzerothCore engine
source (`src/server/game/...`), tracked here for review since that source lives
in a separate upstream repository (`azerothcore/azerothcore-wotlk`, forked as
`mod-playerbots/azerothcore-wotlk`) and isn't otherwise vendored in this repo.

Apply from the core repository with `git apply <patch-path>` before rebuilding
the `worldserver` target.

## mount-allowablerace-exemption.patch

`src/server/game/Entities/Player/PlayerStorage.cpp`,
`Player::CanUseItem(ItemTemplate const*)`: exempts mount items/spells
(`ITEM_CLASS_MISC` + `ITEM_SUBCLASS_JUNK_MOUNT`) from the `AllowableRace` half of
the item-use eligibility check, so a mount item can be used regardless of the
character's race. `AllowableClass` and every other check in the function (required
skill, required spell, required level, holiday) are unaffected, as are the two
`ITEM_FLAG2_FACTION_*` checks earlier in the same function.

This alone is **not sufficient** to let a character actually learn a
cross-faction mount through normal play - confirmed live against production, the
WotLK 3.3.5a client checks `AllowableRace`/`AllowableClass` locally and silently
refuses to even send the "use item" request when it decides the character can't
use the item, so this server-side exemption is never reached that way. It's kept
because it's still correct and harmless (any other path that reaches
`CanUseItem` - including `server-modules/mod-web-admin`'s `webadmin mount teach`
command - benefits from it), but the actual cross-faction-mount fix is that new
command, not this patch on its own. See `server-modules/mod-web-admin/README.md`.

A matching pure-logic test lives at
`server-modules/mod-web-admin/tests/CanUseItemMountRaceExemptionTest.cpp` (core's
own `src/test/server/game/Entities/`) - not build/run in this session since the
production CMake configuration has `BUILD_TESTING=OFF`.
