# Daybreak

Daybreak is a self-hosted household dashboard for recurring activities and one-off tasks. Open it in a web browser, select a large activity button when something is done, and every other open dashboard updates immediately.

> Daybreak is under active development. Build the image from source until the first tagged release is published.

## Screenshots

[![Daybreak daily dashboard with seeded household activities, deadlines, urgency states, and completed items](docs/images/dashboard.png)](docs/images/dashboard.png)

| Activity configuration | History and analytics |
| --- | --- |
| [![Daybreak activity configuration screen](docs/images/configuration.png)](docs/images/configuration.png) | [![Daybreak history and analytics screen](docs/images/history.png)](docs/images/history.png) |

## What it does

- Generates today's board from daily, weekday, interval, and monthly schedules.
- Adds dated one-off tasks alongside recurring activities.
- Sorts unfinished work by overdue state and deadline.
- Provides optional due-soon and overdue pulsing, with a static reduced-motion alternative.
- Keeps near-midnight work available during a configurable bleed window.
- Expires unfinished occurrences into history instead of creating a backlog.
- Synchronizes completion, undo, expiry, rollover, and configuration changes across open browser sessions.
- Offers an experimental, per-browser keep-awake preference using locally generated silent audio.
- Adjusts schedules around holidays calculated by a vendored, fully offline Nager.Date provider.
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
| `DAYBREAK_ADMIN_PASSWORD` | No | `admin` | Administrator password. Override the supplied value at runtime if desired. |
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

## Security and privacy

- The dashboard intentionally has no authentication in the MVP.
- Daybreak intentionally supplies the administrator password `admin`. This is a convenience control for a low-security household product, not a strong security boundary; change the committed defaults or override `DAYBREAK_ADMIN_PASSWORD` if desired.
- Administration uses a rate-limited, antiforgery-protected login form and an HTTP-only, same-site cookie.
- Password comparison is constant-time; the password itself is not stored in SQLite.
- Changing the configured password invalidates existing administrator sessions.
- Activity titles and history stay in the self-hosted SQLite database.
- No telemetry, email, web push, browser notification, or mandatory cloud account is used.
- Holiday dates are calculated locally from the vendored Nager.Date source; Daybreak does not contact a holiday API.

Report security concerns privately to the repository owner rather than opening a public issue containing sensitive details.

## License

Daybreak is licensed under the [MIT License](LICENSE). Direct dependencies use MIT or Apache-2.0 licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
