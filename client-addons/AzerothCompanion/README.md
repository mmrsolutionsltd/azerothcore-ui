# Azeroth Companion

A small World of Warcraft 3.3.5a addon for the AzerothCore-UI questing companion
feature. It displays active companion bag capacity, loot/party state, gathering
status, behaviour, and side-by-side progress for quests shared with the logged-in
leader. Each companion also has quick Questing, Dungeon Tank, Dungeon Healer,
Follow, Stay, and Regroup controls.

The addon exchanges companion data and commands through AzerothCore's authenticated addon-command
channel. `mod-web-admin` must include the companion-inspection permission change in
this repository and the server must then be rebuilt (`ALL_BUILD`, followed by
`INSTALL`) and restarted. The addon itself requires only a client `/reload` or restart.
The module and addon currently use companion protocol version 3. The panel reports a
clear timeout or version-mismatch message when the installed server bridge is missing
or does not match the addon.

An authenticated website user with quest-adventure access can download the packaged
addon from **Adventures > Client addons**. Extract the ZIP into `Interface\AddOns` so
the resulting folder is `Interface\AddOns\AzerothCompanion`.

Commands:

- `/accomp` toggles the panel.
- `/companion` is an alias.

The panel refreshes every five seconds while visible and can also be refreshed using
its button. Drag its bottom-right resize handle to adjust the panel. Its position,
size, and visibility are saved per WoW installation.
