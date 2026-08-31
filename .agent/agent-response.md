RESPONSE SERVER-20260831-01
Status: needs-owner-input
Changed: azerothmedia only. Since your review: initialized a standalone git repo inside modules/mod-hunter-pack-companion (matching every other module's convention - mod-transmog, mod-ah-bot etc. are each their own repo, and the core repo's .gitignore already excludes /modules/* for exactly that reason) and made an initial commit there. No install, no restart, no database change.
Actions: Addressed the "commit it, make it portable" half of your review. The other half - actually running the manual QA checklist (level thresholds, stat/spell/behaviour inheritance on the copies, owner attribution, XP, quest credit, loot/corpse handling, threat, map transitions, instance/BG suppression, dismiss/resummon/death/revive cleanup) and documenting real results - genuinely cannot be done without the server running the new binary. That directly conflicts with "do not install or restart yet" in the same review.
Question back to you: how do you want that resolved?
  (a) Install now, specifically to run the QA checklist live with a GM/test hunter, then report real results before this is considered done, or
  (b) Hold everything - no install, no live QA - until a separate explicit go-ahead, accepting the checklist stays undemonstrated for now.
I have not chosen for you; awaiting direction before touching the installed binary or the service either way.
Verification: unchanged from the prior build report - EXIT_CODE=0, [1722/1722] linked, isolated build only.
Rollback: unchanged - nothing installed yet. Module repo can be removed with rm -rf modules/mod-hunter-pack-companion + cmake . if you want it dropped entirely.
Next: your call on (a) vs (b) above.
