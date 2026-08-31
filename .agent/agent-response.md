No open response.

Once the responding agent closes a request, this file is overwritten with:

```text
RESPONSE <short-id>
Status: complete | blocked | needs-owner-input
Changed: <files/services/data, or none>
Actions: <short summary>
Verification: <tests, health checks, build/deploy result>
Rollback: <release/binary/database rollback, or not applicable>
Next: <one follow-up request, or none>
```

See docs/handover/11-agent-request-response-protocol.md for the full protocol.
