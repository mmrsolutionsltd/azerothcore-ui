# Runtime secrets checklist (template)

Do not commit a filled copy. Keep values in a password manager or on the host with restrictive permissions.

| Secret | Use | Location/owner |
|---|---|---|
| SSH private key | Windows-to-Linux deployment | `C:\Users\<operator>\.ssh\azerothcore_beelink` |
| SSH user | Deployment | `mark` |
| Linux sudo authorization | Core/service operations | interactive sudo |
| MySQL admin credential | backup/recovery | password manager |
| AzerothCore/UI DB credentials | API external config | `/etc/azerothcore-ui` |
| SOAP credentials | API/module bridge | `/etc/azerothcore-ui` |
| Web-admin API key | module/API authentication | external config/module config |
| Dynu update credential | DDNS client | host service/config |
| Caddy TLS state | HTTPS | service-managed state directory |
| Website owner/admin credentials | UI login | `azerothcore_ui` database/password manager |
| Claude SSH private key | Automation login | `C:\Users\<operator>\.ssh\azerothcore_claude` |
| Claude MySQL credential | Automation SQL login | local secret file/password manager; MySQL user `claude_ops` |

Former credentials shared in chat should be rotated. Give Claude only the minimum secret needed for an operation through an interactive prompt or short-lived environment variable; never place passwords in source, Git history, logs, screenshots, or this handover pack.
