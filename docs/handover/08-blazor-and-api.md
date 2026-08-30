# Blazor and API handover

## Projects and launch points

`AzerothCore-UI.Api` is an ASP.NET Core controller API. `AzerothCore-UI.Web` is a Blazor interactive-server application; it calls the API through typed clients and owns the browser session, antiforgery login flow, shared selected-character state, and UI-only preferences.

Development configuration lives in each project's `appsettings*.json` and `Properties/launchSettings.json`. Production configuration is passed with `--ExternalConfig=/etc/azerothcore-ui/<file>.json`; secrets are never committed.

Typical local commands:

```powershell
dotnet run --project .\AzerothCore-UI.Api\AzerothCore-UI.Api.csproj
dotnet run --project .\AzerothCore-UI.Web\AzerothCore-UI.Web.csproj
dotnet test .\AzerothCore-UI.Api.Tests\AzerothCore-UI.Api.Tests.csproj --no-restore
dotnet test .\AzerothCore-UI.Web.Tests\AzerothCore-UI.Web.Tests.csproj --no-restore
```

Production services use:

```text
API: /usr/bin/dotnet .../Api/AzerothCore-UI.Api.dll --urls http://127.0.0.1:5202 --ExternalConfig=...
Web: /usr/bin/dotnet .../Web/AzerothCore-UI.Web.dll --urls http://127.0.0.1:5211 --ExternalConfig=...
```

## API route families

Controllers are under `AzerothCore-UI.Api/Controllers` and use route families such as:

```text
/api/accounts
/api/characters
/api/administration-users
/api/server-administration
/api/database-backups
/api/auction-house
/api/crafting-upgrades
```

Search the controller attributes for the exact action route before scripting against it:

```powershell
rg -n "\[Route|\[Http(Get|Post|Put|Delete)" .\AzerothCore-UI.Api\Controllers
```

Health endpoints are `/health/live` and `/health/ready`. OpenAPI is enabled in development when configured. Authentication and authorization are enforced by the API request authorizer and permission resolver; never bypass them by adding an unprotected endpoint.

## Dependency registration

`AzerothCore-UI.Api/Program.cs` registers the connection factory, administration account store, audit service, SOAP client, server manager, configuration manager, diagnostics, backups, security dashboard, dungeon/crafting/roster services, and hosted workers. Most data/services are singletons because they own connection factories or controlled stores; inspect the registration before changing lifetimes.

The Web `Program.cs` registers typed API clients and scoped stores such as selected-character and recent-picker state. Shared UI components include `RealmRosterHeader`, `CharacterPicker`, companion controls, item/NPC/location dialogs, and command tabs. Reuse these instead of creating a second character-selection implementation.

## Adding a feature

1. Add a model/service/controller in the API with permission and audit checks.
2. Add or extend a typed client in Web.
3. Render the feature through a shared component where it targets characters.
4. Add API and Razor/component tests.
5. Run both test projects and a solution build.
6. Deploy with `deploy/linux/Deploy-To-Linux.ps1`; verify both health endpoints and the relevant UI page.

For database changes, add a reviewed SQL migration under `database/`, take a production backup first, and ensure queries are compatible with the exact AzerothCore revision. For a C++ module change, the web deployment is not enough: rebuild and install the Linux worldserver as described in the operations cookbook.

## Troubleshooting

Inspect sanitized logs with `journalctl -u azerothcore-ui-api` and `journalctl -u azerothcore-ui-web`. A 401/403 generally indicates login, role, permission, or account scope; a 500 should be correlated with the API log and underlying SOAP/MySQL error. Dapper constructor errors mean the selected SQL aliases/types do not match the target record.
