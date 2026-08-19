# Verification record

Last verified: 2026-08-19

## Automated gate

`./scripts/Verify.ps1` completed successfully on the Windows development environment:

- formatting verification;
- Release build with zero warnings and zero errors;
- 38 passing Daybreak tests with no failures or skips;
- 669 passing vendored Nager.Date tests, with its network source-validation test skipped;
- framework-dependent `linux-x64` publish;
- framework-dependent `linux-arm64` publish;
- local-only browser-asset validation for both Linux publishes;
- complete NuGet license inventory restricted to MIT and Apache-2.0;
- no known vulnerable packages from the configured NuGet sources;
- no deprecated packages from the configured NuGet sources.

The gate also confirmed that the locally modified Nager.Date project builds from
the vendored source in Release configuration and is included in both Linux
publishes as `Daybreak.Nager.Date.dll`. Holiday calculation does not require a
runtime network service or license key.

`./scripts/Migrate.ps1 -ConnectionString 'Data Source=MigrateOnly;Mode=Memory;Cache=Shared'` also completed successfully without starting the web server.

## Linux container exercise

The production Dockerfile was first exercised with Podman 4.9.3 on Linux under WSL, then the repository's exact Compose workflow was exercised with Docker Engine 28.5.2 and Docker Compose 2.40.3 in an isolated Docker-in-Docker environment.

The exercise proved:

- the pinned .NET 10 SDK and ASP.NET runtime images build successfully;
- the container creates and migrates a fresh named-volume database;
- `/health`, `/`, and `/admin/login` return successfully;
- the direct `DAYBREAK_ADMIN_PASSWORD` setting authenticates the administration interface;
- startup fails clearly if an image or runtime configuration supplies no administrator password, while migration-only mode remains password-free;
- the running `dotnet Daybreak.dll` process uses the unprivileged `app` account (UID 1654);
- replacing the container while retaining its named volume preserves the dashboard data;
- a database copied while the source container is stopped can be restored into a new volume;
- the restored container starts healthy with demo seeding disabled and retains the original data.

The Docker-specific exercise additionally proved:

- `docker compose config --quiet` accepts `compose.yaml`;
- `docker compose up --build --detach` builds and starts the service on Docker Engine;
- the image-supplied `DAYBREAK_ADMIN_PASSWORD=daybreak` satisfies fail-fast password validation without setup;
- the nested Docker service reports healthy and runs `dotnet Daybreak.dll` as UID 1654;
- `docker compose down` removes the container and network while preserving `daybreak-data`;
- a following `docker compose up --no-build --detach` starts healthy and reuses the existing migrated database.

All containers, volumes, image tags, and backup files created for the exercise were removed afterward.

## Browser exercise

The responsive interface was exercised against a locally running Windows development process using Playwright MCP:

- the dashboard loaded its fingerprinted .NET 10 Blazor runtime and established WebSocket connections;
- two independent dashboard tabs started at revision 5;
- completing an item in one tab advanced both tabs to revision 6 without refreshing;
- undoing it in the other tab advanced both tabs to revision 7 without refreshing;
- the built-in `daybreak` password opened the administration interface;
- creating a dated one-off task in administration made it appear on the open dashboard at revision 9 without refreshing;
- no new browser console errors occurred during the passing exercise.

The exercise initially exposed a non-interactive page caused by a missing .NET 10 static-asset endpoint. Daybreak now maps static assets explicitly, resolves CSS, icon, and Blazor script URLs through the generated asset collection, and pins the internal Blazor asset package to the runtime patch. The normal Windows development workflow then served the full Blazor runtime successfully.

Daybreak is treated as an ordinary responsive web page. No device-specific or screenshot acceptance gate is required.
