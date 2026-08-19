# Daybreak

Daybreak is a self-hosted household dashboard for recurring activities and one-off tasks. Open it in a web browser, select a large activity button when something is done, and every other open dashboard updates immediately.

> Daybreak is under active development. Build the image from source until the first tagged release is published.

## What it does

- Generates today's board from daily, weekday, interval, and monthly schedules.
- Adds dated one-off tasks alongside recurring activities.
- Sorts unfinished work by overdue state and deadline.
- Provides optional due-soon and overdue pulsing, with a static reduced-motion alternative.
- Keeps near-midnight work available during a configurable bleed window.
- Expires unfinished occurrences into history instead of creating a backlog.
- Synchronizes completion, undo, expiry, rollover, and configuration changes across open browser sessions.
- Adjusts schedules around Nager.Date holidays using a locally cached, replaceable provider.
- Reports completion rate, on-time/late/unfinished results, weekly trends, and per-activity summaries.
- Protects configuration with one Docker-image password while leaving the dashboard URL open.

## Run with Docker Compose

Daybreak supports Linux containers only. Docker development and production builds use the same `Dockerfile`.

1. Start Daybreak:

   ```console
   docker compose up --build -d
   ```

2. Open <http://localhost:8080> for the dashboard.
3. Open <http://localhost:8080/admin> and enter `daybreak`.

The Compose file stores SQLite data in the named `daybreak-data` volume. Removing or replacing the container does not remove that volume.

### Docker configuration

| Setting | Required | Default | Purpose |
| --- | --- | --- | --- |
| `DAYBREAK_ADMIN_PASSWORD` | No | `daybreak` in the Docker image | Administrator password. Override the image value at runtime if desired. |
| `ConnectionStrings__Daybreak` | No | `Data Source=/data/daybreak.db` | SQLite connection string. |
| `Daybreak__SeedDemoData` | No | `false` | Seeds neutral demonstration activities into an empty database. |
| `Daybreak__DataProtectionKeysPath` | No | `/data/keys` in Docker | Persists administrator cookie-signing keys across container restarts. |
| `ASPNETCORE_HTTP_PORTS` | No | `8080` | Container HTTP port. |

Daybreak serves HTTP inside the container. Use a trusted reverse proxy for TLS before exposing it outside a trusted household network. Anyone who can reach the dashboard can complete or undo activities; only configuration routes require the administrator password.

## Backup and restore

SQLite uses a persistent Docker volume. Stop writes before taking a filesystem copy:

```console
docker compose stop daybreak
docker run --rm -v daybreak_daybreak-data:/source -v "${PWD}:/backup" alpine \
  cp /source/daybreak.db /backup/daybreak-backup.db
docker compose start daybreak
```

To restore, stop Daybreak and replace `/data/daybreak.db` in the volume with a known-good backup. Retain the original file until the restored container starts and `/health` reports success.

Database migrations run automatically at startup and are forward-only. Back up the database before deploying a newer Daybreak version.

## Development

Requirements:

- Windows development environment
- .NET SDK specified by `global.json`
- PowerShell 7
- Optional Docker installation for container verification

Run locally with neutral demonstration data:

```powershell
./scripts/Run.ps1
```

The development administrator password defaults to `daybreak-dev` in the local script and committed VS Code launch profile. The Docker image intentionally uses the separate low-security default `daybreak`.

Build and test:

```powershell
./scripts/Build.ps1
./scripts/Test.ps1
./scripts/Verify.ps1
```

Run pending database migrations without starting the web server, or operate the Compose deployment from PowerShell:

```powershell
./scripts/Migrate.ps1
./scripts/Docker.ps1 -Command Start
./scripts/Docker.ps1 -Command Logs
./scripts/Docker.ps1 -Command Stop
```

`Docker.ps1 -Command Stop` removes containers and the Compose network but deliberately preserves the named data volume.

`Verify.ps1` checks formatting, runs the Release test suite, publishes framework-dependent Linux AMD64 and ARM64 artifacts, audits NuGet licenses, and fails on known vulnerable or deprecated packages. The project uses no Node.js or Python development, test, build, or asset tooling.

### Browser assets

Daybreak does not load CSS, JavaScript, fonts, or icons from a CDN. The application stylesheet and icon live in `wwwroot`; the pinned Blazor framework scripts are restored from NuGet and copied into every published Linux image. `Verify.ps1` rejects remote CSS/JavaScript asset references and fails if any required published browser asset is missing or empty.

VS Code includes a `Daybreak (Blazor Server)` debug profile plus default build and test tasks.

## Architecture

- .NET 10 and Blazor Interactive Server
- SQLite and Dapper
- Project-owned schema manifest and ordered migrations
- Server-authoritative board snapshots and in-process revision broadcasts
- Version-checked, idempotent occurrence transitions
- Locally cached Nager.Date holiday adapter behind `IHolidayProvider`
- One household and one server process per deployment

The server materializes a rolling occurrence horizon. Each occurrence retains nominal and effective dates, deadline and action-window instants, completion state, a concurrency version, and a future-ready nullable actor. Connected dashboards receive a revision notification and then fetch the authoritative snapshot; they do not reconstruct state from events. Disconnected dashboards visibly block actions until Blazor reconnects.

See [daybreak.md](daybreak.md) for the full product and delivery plan.
The latest build, test, and Linux-container evidence is recorded in [docs/verification.md](docs/verification.md).

Architecture decisions:

- [Application and deployment model](docs/decisions/0001-application-and-deployment.md)
- [Server-authoritative dashboard synchronization](docs/decisions/0002-server-authoritative-synchronization.md)
- [Holiday adjustment and collisions](docs/decisions/0003-holiday-adjustment-and-collisions.md)

## Security and privacy

- The dashboard intentionally has no authentication in the MVP.
- The Docker image intentionally includes the administrator password `daybreak`. This is a convenience control for a low-security household product, not a strong security boundary; change the Dockerfile value or override `DAYBREAK_ADMIN_PASSWORD` if desired.
- Administration uses a rate-limited, antiforgery-protected login form and an HTTP-only, same-site cookie.
- Password comparison is constant-time; the password itself is not stored in SQLite.
- Changing the configured password invalidates existing administrator sessions.
- Activity titles and history stay in the self-hosted SQLite database.
- No telemetry, email, web push, browser notification, or mandatory cloud account is used.
- Nager.Date is contacted only when holiday adjustment is configured; cached data is used when refresh fails.

Report security concerns privately to the repository owner rather than opening a public issue containing sensitive details.

## License

Daybreak is licensed under the [MIT License](LICENSE). Direct dependencies use MIT or Apache-2.0 licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
