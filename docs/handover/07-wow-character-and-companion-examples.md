# WoW character and companion examples

These are examples of the workflows the owner commonly asks the website to perform. They are intentionally expressed as UI/API operations rather than unchecked SQL.

## Character administration

- Find a character by name, account, level, class, race, or online state.
- Inspect health, location, equipment, bags, quests, professions, reputation, skills, and known recipes.
- Give a character an item, money, recipe, profession tool, ammunition, or starter gear.
- Revive, level, teleport, train, adjust reputation, and move characters through the Player Actions or Character Services screens.
- Before live SQL, create and verify a database backup.

## Companion groups

- Select one real online leader and one or more eligible companion characters.
- Keep leader selection separate from companion selection; a leader must be genuinely online for party SOAP commands.
- Start, regroup, disband, diagnose, and inspect companion logistics from Questing Companions or Companion Commands.
- If a companion becomes stuck after the leader dies, refresh state; disband/recreate the party is the known recovery fallback.
- Companions should loot useful quest items, avoid unusable dungeon items, preserve profession tools, sell junk, mail routed materials to profession characters, and never sell green-or-better equipment without an explicit policy.

## Questing and dungeons

- Inspect quest readiness, prerequisites, level, faction, class, reputation, and questgiver location.
- Offer teleport to the questgiver only after checking the character is online and eligible.
- Dungeon Assistant can form a party, suggest roles/bots, show lockouts, quests, bosses, order, and loot; a leader must be online before readiness calls.

## Professions and gear

- Learn or train primary/secondary professions only when the character and trainer allow it.
- Preserve tools such as skinning knives and mining picks.
- Use Artisan Gearing Room to compare equipped gear, account-wide bags/banks/mail, craftable upgrades, materials, stat gains, and profession skill paths.

## Typical troubleshooting requests

- “The character is online but the UI says offline”: refresh roster and verify worldserver state rather than assuming the cached card is current.
- “The bot is not following/fighting/looting”: inspect companion diagnostics, leader/companion state, death/rebirth state, party membership, inventory capacity, and profession tools.
- “A command returned HTTP 500”: inspect API and worldserver journals, reproduce with the UI, and capture the sanitized SOAP/worldserver error.
- “A field/column is missing”: identify the selected schema and AzerothCore revision before applying any SQL.
