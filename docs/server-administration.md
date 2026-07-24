# Local server administration setup

The Server page manages the AzerothCore installation configured under
`AzerothCore:Server:RootPath`. The development default is
`C:\AzerothServer-PlayerBots`.

## Protect the Blazor administration page

Install `database/azerothcore-ui-schema.sql`, configure the API's
`ConnectionStrings:AzerothCoreUi` secret with a dedicated least-privilege MySQL
login, and visit `/admin/setup` once to create the initial Owner. Passwords are
stored only as salted hashes in `azerothcore_ui`. The setup route closes after
the first account is created, and protected routes redirect unauthenticated
users to `/admin/login`.

## Configure local AzerothCore SOAP

In `configs\worldserver.conf`, bind SOAP to loopback and enable it. Do not
expose SOAP to the LAN or Internet. Create a dedicated AzerothCore account with
only the command security required by the allowlisted operations, with realm
access set appropriately.

Store its credentials in the **API project** user secrets:

```powershell
dotnet user-secrets set "AzerothCore:Soap:Username" "<soap-account>" --project .\AzerothCore-UI.Api
dotnet user-secrets set "AzerothCore:Soap:Password" "<soap-password>" --project .\AzerothCore-UI.Api
```

The non-secret loopback endpoint is configured in the API development
settings. Change `AzerothCore:Soap:Endpoint` locally if the configured SOAP
port differs.

## Safety boundaries

- The browser cannot submit arbitrary console, SOAP, SQL, executable, or
  PowerShell commands.
- Administration API endpoints accept requests only from loopback.
- Start targets only `worldserver.exe` and `authserver.exe` in the configured
  root directory.
- Stop requests worldserver shutdown through SOAP before considering a forced
  process termination.
- Player-relative movement is disabled until a purpose-built worldserver
  module is compiled and installed. Character coordinates are never edited
  directly in MySQL.
- Administrative operations are written to structured application logs with
  the `ADMIN AUDIT` prefix.

The lifecycle service deliberately does not invoke `start.ps1`: that script is
interactive, changes `realmlist`, and currently handles a database credential.
Move that credential out of the script before reusing its network-update logic.
