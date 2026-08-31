REQUEST SERVER-20260831-01
Goal: Design and implement a custom Hunter Pack Companion passive: one additional temporary copy of the hunter's active pet for every 15 hunter levels, capped at five total pets.
Scope: AzerothCore/mod-playerbots or the appropriate custom server module, plus optional companion addon/UI support. No database changes unless a migration is essential; backup first if one is needed.
Current evidence: This is a private outdoor-PvE server and the requested progression is levels 1–14 = 1 pet, 15–29 = 2, 30–44 = 3, 45–59 = 4, and 60+ = 5.
Constraints: Keep the normal persistent hunter pet authoritative. Extra copies should be temporary guardian-style summons. Suppress extras in battlegrounds, raids, and instances; prevent multiplied loot, threat, XP, or quest credit; clean up on death, logout, dismissal, summon/revive, and map transfer. Do not rebuild, deploy, or alter the database until explicitly approved.
Expected result: First provide an implementation design identifying exact core/module touchpoints, then code only after design approval. Include configurable level interval/cap/XP policy, tests for progression, cleanup, kill attribution, and instance suppression, with build/install and rollback instructions.

Once Claude opens a request, this file is overwritten with:

```text
REQUEST <short-id>
Goal: <one sentence>
Scope: <repository files, Linux host, database, or client>
Current evidence: <error/output/commit, sanitized>
Constraints: <no SQL, backup first, no restart, etc.>
Expected result: <what should be true when complete>
```

See docs/handover/11-agent-request-response-protocol.md for the full protocol.
