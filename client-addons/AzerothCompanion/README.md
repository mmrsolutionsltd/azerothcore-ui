# Azeroth Companion

A small World of Warcraft 3.3.5a addon for the AzerothCore-UI questing companion
feature. Its compact bar shows each active companion's role, colour-coded bag space,
latest maintenance action, and quick Follow, Stay and Regroup controls. Expand
**Details** for gathering, behaviour, bag-logistics, Questing, Dungeon Tank and
Dungeon Healer controls.

When Carbonite is loaded with **Share quest status with party** and its party-quest
display enabled, the addon feeds companion quest progress into Carbonite's existing
party tracker. Shared and companion-only quests therefore appear beside real party
members without duplicating them in the companion panel. If Carbonite is unavailable,
disabled or does not know one of the companion's quests, the panel automatically
falls back to its original side-by-side quest display.
The data bridge continues refreshing while the companion panel is hidden, so the
panel can remain closed when Carbonite is providing the quest display.

The addon exchanges companion data and commands through AzerothCore's authenticated addon-command
channel. `mod-web-admin` must include the companion-inspection permission change in
this repository and the server must then be rebuilt (`ALL_BUILD`, followed by
`INSTALL`) and restarted. The addon itself requires only a client `/reload` or restart.
The module and addon currently use companion protocol version 7. The panel reports a
clear timeout or version-mismatch message when the installed server bridge is missing
or does not match the addon.

An authenticated website user with quest-adventure access can download the packaged
addon from **Adventures > Client addons**. Extract the ZIP into `Interface\AddOns` so
the resulting folder is `Interface\AddOns\AzerothCompanion`.

Commands:

- `/accomp` toggles the panel.
- `/companion` is an alias.

The panel refreshes every five seconds while visible and can also be refreshed using
its button. Compact/details mode is remembered. In details mode, drag the
bottom-right resize handle to adjust the panel. Its position, expanded size, and
visibility are saved per WoW installation.
