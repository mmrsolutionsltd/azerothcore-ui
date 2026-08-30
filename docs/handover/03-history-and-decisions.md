# History and decisions

The project began as a Windows-hosted administration site for a small family AzerothCore server. An earlier TrinityCore installation was abandoned because it lacked PlayerBots. The active server became AzerothCore + PlayerBots with `mod-arac`, then gained a SOAP/worldserver admin bridge and a Blazor UI.

Major milestones were account/role/scoping and audit support; database backups; shared multi-select character cards; player tools and trainer/teleport helpers; companion parties, questing, diagnostics, logistics and the WoW addon; dungeon assistant/library; profession/training and crafting intelligence; HTTPS/Caddy and Linux deployment; and migration to `azerothmedia`, an HP EliteDesk 800 G4 Mini running Ubuntu Server.

Operating decisions:

- This is a small private family server; avoid unnecessary multi-tenant complexity.
- HTTPS is preferred; expose only TCP 443 publicly.
- Back up before every live SQL change.
- Companions are not real online players; preserve that distinction in authorization and selection.
- Keep AzerothCore source, modules, DBCs, client patches, addon, and database updates on compatible revisions.
- Prefer supported SOAP/worldserver commands over direct live character-row edits.
- Linux is now the production host; do not rebuild the old Windows core for Linux changes.

Known pitfalls include stale DNS, locked Windows apphosts, `Text file busy` during binary replacement, concurrent worldserver builds, and commands that require a genuinely online leader.
