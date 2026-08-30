# LLM and agent context

## What the original assistant was

This project was developed with OpenAI Codex, an agent based on the GPT-5 family, operating through a tool-enabled coding environment. The exact model build, context window, usage limits, and tool permissions are runtime/platform settings—not properties of this repository and not safe to assume for Claude.

The important transferable behaviour is procedural: inspect first, preserve unrelated changes, back up before SQL, use least privilege where possible, test after edits, deploy with the existing scripts, and verify services afterwards.

## What Claude needs to know

Claude should treat the repository handover files as the source of operational truth, not conversational memory. It should read `00-START-HERE.md` first and then consult the architecture, build, operations, WoW examples, and Blazor/API documents relevant to the request.

Claude can use the dedicated Linux SSH identity `claude` and the dedicated MySQL identity `claude_ops` when the operator supplies the local private key/password. These credentials are deliberately not included here. The account was requested to have replacement-level access; because that is effectively root/database-admin access, Claude should still announce destructive or service-impacting operations, make backups, and log what changed.

## Agent operating rules

1. Confirm the active host and service before changing anything; production is `azerothmedia`.
2. Keep secrets out of output, files, Git, and shell history.
3. Use read-only checks before live writes.
4. Back up the database before SQL or character data changes.
5. Never run concurrent worldserver builds; stop the service before replacing its executable.
6. Do not rebuild the old Windows core when the active game server is Linux.
7. Preserve unrelated dirty working-tree changes unless the operator explicitly asks for a cleanup.
8. Run focused tests and report their result.
9. For live restarts, state which players/services will be interrupted.
10. Treat model knowledge as replaceable; derive routes, schema, source APIs, and service paths from this repository and host.

## Prompt to start a Claude session

```text
Read docs/handover/00-START-HERE.md and the numbered handover documents before acting. You are operating the AzerothCore-UI repository and the private AzerothCore PlayerBots server on azerothmedia. Preserve unrelated changes, inspect read-only first, back up before SQL, keep credentials out of logs/Git, use the existing build/deploy scripts, test changes, and report exact verification results. Ask for a secret only when required and use it at runtime.
```
