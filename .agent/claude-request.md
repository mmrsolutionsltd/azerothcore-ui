No open request.

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
