# Agent request/response protocol

Use this when the owner wants Claude and another coding agent to exchange compact, actionable work items. The owner remains the approval point for destructive or externally visible changes.

## One request at a time

Claude should submit one request using this format:

```text
REQUEST <short-id>
Goal: <one sentence>
Scope: <repository files, Linux host, database, or client>
Current evidence: <error/output/commit, sanitized>
Constraints: <no SQL, backup first, no restart, etc.>
Expected result: <what should be true when complete>
```

The receiving agent replies:

```text
RESPONSE <short-id>
Status: complete | blocked | needs-owner-input
Changed: <files/services/data, or none>
Actions: <short summary>
Verification: <tests, health checks, build/deploy result>
Rollback: <release/binary/database rollback, or not applicable>
Next: <one follow-up request, or none>
```

## Practical transport

The simplest transport is copy/paste between Claude and the owner. For same-machine work, use two local files outside Git:

```text
.agent/claude-request.md
.agent/agent-response.md
```

Claude overwrites `claude-request.md` only when the previous request is closed; the responding agent overwrites `agent-response.md` with the matching short-id. Do not put credentials, tokens, private keys, or raw production configuration in either file. Add `.agent/` to `.git/info/exclude` if these files are created locally.

## Rules

- Use a short ID such as `UI-20260830-01`.
- Keep requests under roughly 20 lines and include only sanitized evidence.
- Never have two open requests that can modify the same files or service.
- A response marked `blocked` must state the exact missing input; do not silently guess.
- A response marked `complete` must include verification and rollback information.
- After deployment, Claude should request a health check rather than assuming success.
- Database changes require an explicit backup statement; service restarts require an impact statement.

## Example

```text
REQUEST CAST-01
Goal: Enable caster filler attacks for a real player.
Scope: mod-web-admin, AzerothCompanion addon, Linux worldserver.
Current evidence: module source is changed; worldserver still has the previous binary.
Constraints: no database changes; do not rebuild Windows core.
Expected result: right-click hostile target starts/stops learned filler spell safely.
```

```text
RESPONSE CAST-01
Status: complete
Changed: module source, addon Lua/TOC, published Linux UI release.
Actions: built and installed Linux worldserver; restarted world service only.
Verification: worldserver active; API/Web tests passed; UI release health checks passed.
Rollback: restore the timestamped worldserver binary and previous UI release.
Next: install the updated addon in the WoW client and test in-game.
```
