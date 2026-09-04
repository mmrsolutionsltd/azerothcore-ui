REQUEST SERVER-20260831-02
Goal: Design (not yet implement) a lightweight, snappier way for the Blazor site to get a few live per-character fields (health, alive/dead, current location) from the running worldserver, instead of relying on the characters.health DB column, which only updates on save.
Scope: modules/mod-web-admin/src/mod_web_admin.cpp (new command), AzerothCore-UI.Api/AzerothCore-UI.Web (new client call + faster poll for selected heroes only). Separate from and does not touch the Hunter Pack Companion module/files from SERVER-20260831-01, which is still an open, unrelated thread on the same worldserver.
Current evidence: PlayerSaveInterval is 900000ms (15 min) on this server, so characters.health (what RealmRosterService.cs currently reads) can lag up to 15 minutes behind a real death - the header's Revive button doesn't enable promptly because of this, not a UI bug. mod-web-admin already exposes ~20 custom GM chat commands under .webadmin (Console::Yes, SOAP-callable), each handler reading live in-memory Player state directly and replying via handler->PSendSysMessage("TAG\tfield\tfield...", ...) tab-delimited lines - not JSON. The existing companion/questing status flow already uses this exact pattern.
Constraints: Owner wants it lightweight - health, alive/dead, and location only, nothing else. Owner floated either the Web site polling faster or the server pushing/the site subscribing; asking which you'd recommend given SOAP here is fundamentally request/response, not pub/sub.
Proposal: New .webadmin status <name1,name2,...> command (SEC_PLAYER, Console::Yes), one online-only character per name (skip offline - no live state to read), replying WEBADMIN_STATUS\t{name}\t{alive}\t{health}\t{maxHealth}\t{mapId}\t{zoneId}\t{areaId}\t{x}\t{y}\t{z} per line, reading straight off the live Player object (no DB query at all). Api adds a thin client call; Web polls this every 2-3s but only for the header's currently selected heroes (small N), not the whole realm roster. Keeps the existing 15s full-roster poll as-is for everything else.
Expected result: Your take on this proposal vs. a genuine push/subscribe alternative (e.g. worldserver-initiated WebSocket to the Web app) - recommend one, or a different lightweight approach if you see a better fit given what's already in mod-web-admin. Then I'll write it up as a full request for approval before touching code.

REQUEST UI-20260902-01
Goal: Owner-directed UI cleanup on the Player Actions tools (grid and permanent sidebar) - not something you asked for, flagging for your review since you're the site's original builder.
Changes made (already implemented, tests passing, not yet deployed):
1. Merged GiveItemTool.razor and GiveMoneyTool.razor into one GiveTool.razor (Components/Shared/PlayerActions/). Uses a btn-group (Item / Money) exactly like TeleportTool's Place/NPC/Player selector, conditionally rendering only the active mode's inputs and footer button(s) - same "hide the unneeded UI" pattern Teleport already used. Item mode keeps the item picker + quantity + Give item/Mail item buttons; Money mode keeps the gold/silver/copper inputs + Send button. PlayerActionTools.razor now renders one <GiveTool> instead of the two separate sections, so the tool grid/sidebar has one fewer card. Old files deleted (GiveItemTool.razor, GiveMoneyTool.razor, GiveMoneyTool.razor.css); the coin-icon CSS moved to the new GiveTool.razor.css.
2. Site-wide form-control/form-select padding reduced (was Bootstrap default .375rem .75rem, now .3rem .25rem after two owner-directed rounds) in wwwroot/app.css, applied outside the .content scope so it also reaches the sidebar. Same rule adds color-scheme: dark plus a gold/brown filter tint on native number-input spin buttons, which were plain white/grey before and clashed with the theme.
3. Vertical compactness pass on every Player Actions tool card (PlayerActionTools.razor.css): outer card padding split to .55rem .8rem (vertical reduced, horizontal unchanged), heading padding-bottom/margin-bottom and footer padding-top all trimmed (~30% less), and a scoped override shrinks every tool's internal Bootstrap mb-2/mb-3 spacing so the many stacked rows inside each tool (item picker + quantity, coin inputs, teleport controls, etc.) sit closer together.
Verification: dotnet build clean; PlayerActionToolTests.cs updated for the merge (heading list now has one "Give" entry instead of "Give item"/"Give money", mode-switching helper added, the target-preservation test now toggles Item/Money mode instead of rendering two separate components) - 19/19 in that file, 91/91 across the whole Web test project. Spinner/padding rendering spot-checked in a real Chrome tab against an isolated Bootstrap page using the exact same CSS values (not the live app, which needs the API/DB stack). Not yet deployed to azerothmedia.
Expected result: A look over the approach (component merge shape, the site-wide input padding/spinner rule placement outside .content, the mb-2/mb-3 override scope) - flag anything that looks like it'll collide with something you're aware of elsewhere in the site, otherwise no action needed.
# Request: outdoor companion groups larger than five

Please review and implement support for companion groups larger than five members **only for ordinary outdoor PvE**.

## Requirements

- Keep dungeon, LFG, raid, battleground, arena and battlefield groups on their existing rules. In particular, dungeon/LFG parties must remain capped at five.
- Allow a normal outdoor party led by a real online player to contain more than five PlayerBots/companions, subject to a configurable safe maximum (do not hard-code an unbounded group). Recommend a sensible default and make the limit configurable where appropriate.
- Preserve existing eligibility checks: distinct accounts, level range, faction/availability rules, leader-online requirement, and protection against special groups.
- Update the web-admin companion start/regroup/group-building paths and any UI validation or text that currently assumes five.
- Make the UI clearly show the outdoor-only nature of the larger group option and prevent selecting it for dungeon launches.
- Ensure party inspection/readiness parsing handles the larger member list without truncation or accidental five-member assumptions.
- Do not change database schemas unless strictly necessary. Do not alter dungeon behaviour.

## Review protocol

Before editing, inspect the current source and write your own implementation plan. Compare it with this request and with the existing architecture; call out risks (AzerothCore core group limits, client UI limits, PlayerBots assumptions, combat/loot behaviour). Then implement the smallest safe change.

Add/update focused unit tests for the eligibility/group-size rules and parser/UI validation. Build only the affected projects/modules; do not perform a full server rebuild unless required, and do not start/stop services.

When complete, report:

1. Your proposed plan and any differences from this brief.
2. Files changed and exact behaviour.
3. Test/build results and any remaining server-core limitation.
4. Whether a worldserver rebuild/install is required and the safest deployment steps.
