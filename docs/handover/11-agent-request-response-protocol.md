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

These files are tracked in Git so the exchange history travels with the repository:

```text
.agent/claude-request.md   current open request (or "No open request.")
.agent/agent-response.md   matching response (or "No open response.")
.agent/read/                archived, closed exchanges
```

Claude overwrites `claude-request.md` only when the previous request is closed; the responding agent overwrites `agent-response.md` with the matching short-id. Do not put credentials, tokens, private keys, or raw production configuration in either file — they are committed to Git history, so sanitize evidence before writing it, the same as any other tracked file.

## Archiving a closed exchange

Once a `RESPONSE` is `complete` (or the owner has read a `blocked`/`needs-owner-input` response and decided how to proceed), move the pair into `.agent/read/` before opening the next request:

```text
.agent/read/<short-id>-request.md
.agent/read/<short-id>-response.md
```

Then reset `claude-request.md` and `agent-response.md` to their "no open request/response" placeholders. Commit the archive move together with (or immediately before) the next request, so `git log -- .agent/read` is a readable timeline of past agent exchanges. Never edit an archived file in place; if a follow-up is needed, open a new short-id.

## Rules

- Use a short ID such as `UI-20260830-01`.
- Keep requests under roughly 20 lines and include only sanitized evidence.
- Never have two open requests that can modify the same files or service.
- A response marked `blocked` must state the exact missing input; do not silently guess.
- A response marked `complete` must include verification and rollback information.
- After deployment, Claude should request a health check rather than assuming success.
- During an active handoff session, the responding agent should check `claude-request.md` about every two minutes. Outside an active session, the owner must prompt the agent again; no agent is permanently monitoring in the background.

## Token and compute efficiency

Claude should help conserve shared AI compute by:

- Reading only the relevant numbered handover file before a task, not the entire repository.
- Sending one compact request with sanitized evidence instead of a long conversation transcript.
- Asking for targeted commands and bounded output (`rg` matches, `tail`, focused tests) rather than full logs.
- Reusing known host paths, service names, and deployment scripts from the handover.
- Avoiding repeated builds, repeated health checks, and broad repository scans when a focused check is sufficient.
- Summarising results and linking to files instead of pasting large source files.
- Deferring optional enhancements until the requested change is verified.
- Database changes require an explicit backup statement; service restarts require an impact statement.
- Archive a closed exchange to `.agent/read/` before starting the next one; do not let `claude-request.md`/`agent-response.md` accumulate unrelated history.

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
