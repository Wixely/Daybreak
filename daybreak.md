# Daybreak

- Status: Active
- Owner: TBD
- Intended hosting: Public GitHub
- License: MIT
- Primary client: A web browser with an open dashboard
- Development platform: Windows
- Supported deployment: Linux Docker containers only
- Captured: 2026-08-19
- Last planned: 2026-08-19

## Product brief

Daybreak is a self-hostable household dashboard that turns recurring activities and one-off tasks into a focused daily checklist.

A household configures an activity once, including when it occurs and whether it has a deadline. Daybreak presents the applicable items as large, clear buttons in a web browser. Selecting a button records completion immediately and updates every other open dashboard so household work is neither forgotten nor duplicated.

Daybreak is deliberately not a general project-management system. Its primary question is: **what needs doing today, and has it been done?**

## Product principles

1. **Glanceable:** unfinished and urgent work must be obvious from across a room.
2. **Immediate:** completing an item should take one action and every open dashboard should update without a refresh.
3. **Dependable:** disconnected or stale dashboards must identify themselves clearly.
4. **Predictable:** recurrence, holiday adjustment, expiry, and midnight behaviour must be deterministic and explainable.
5. **Private by deployment:** the application has no mandatory cloud service, account, notification provider, or analytics service.
6. **Focused:** additions should strengthen the daily household checklist rather than turn Daybreak into a general task manager.

## Intended deployment and access model

- One household per Daybreak deployment.
- Anyone who can reach the dashboard URL can view and operate the dashboard.
- The MVP has one implicit household user and does not ask who completed an item.
- Completion records retain a nullable actor field so named household members can be introduced later without replacing the occurrence model.
- Administration pages require one deployment-configured password.
- The Docker image and development launch configuration supply the intentionally low-security administrator password `admin` through `DAYBREAK_ADMIN_PASSWORD`; operators may change the committed value or override it at runtime.
- Successful administration login creates a secure, HTTP-only session cookie. Restarting with a changed password invalidates existing administration sessions.
- Daybreak should document use behind HTTPS or a trusted reverse proxy when it is exposed beyond a trusted local network.

## MVP scope

### Dashboard

- A responsive, kiosk-friendly grid of large activity buttons.
- Pending items remain in one ordered grid; tapped items move to a separate completed section below it.
- The grid scrolls when the available items do not fit on one screen.
- Items are ordered by:
  1. overdue status;
  2. deadline, earliest first;
  3. items without a deadline.
- Ordering within otherwise equal items is deterministic. User-controlled ordering may be added later.
- Incomplete, completed, due-soon, overdue, and carried-from-yesterday states have distinct colour-independent treatments.
- The completed section starts at the bottom of the first screen, or after overflowing pending content. Its cards are 75% transparent until the section is scrolled into view.
- Completed cards fade back to 75% transparency when the dashboard returns to the top. After one minute without scrolling, tapping, keyboard, wheel, or touch input, the dashboard returns to the top automatically.
- Cards use restrained entry and exit transitions between pending and completed presentation, including when a different open dashboard initiated the change. Transitions respect reduced-motion preferences, and cards retain a bounded width instead of stretching to fill a sparse row.
- The compact dashboard header places the Daybreak configuration link at the top right. The date begins immediately below it without a redundant “Today” label, and reduced page gutters preserve room for more cards.
- The summary beneath the date is the live household-local clock. During a pending deadline card’s final hour, a full countdown disc is progressively cut away clockwise and is empty at the deadline.
- Page headings and containers must not use `tabindex="-1"`. Pages that need initial focus should place it on an appropriate input, as the login page does.
- Completed buttons show completion time. The MVP does not show a completing member.
- Dashboard operation does not require a keyboard.
- No browser, push, email, or operating-system notifications are used. The live dashboard is the reminder surface.

### Recurring activities

Each activity supports:

- name;
- optional description or notes;
- active, paused, and archived lifecycle states;
- recurrence schedule;
- optional deadline;
- urgency mode;
- optional override of the global midnight bleed window;
- holiday adjustment policy;
- creation and modification timestamps.

MVP recurrence patterns are:

- daily;
- selected weekdays;
- every N days;
- every N weeks;
- monthly on a calendar date;
- monthly on an ordinal weekday, such as the second Tuesday;
- optional start and end dates.

The activity editor previews the next occurrences before saving.

### One-off tasks

- One-off tasks are included in the MVP.
- A one-off task has a scheduled local date and the same optional notes, deadline, urgency, and midnight-bleed behaviour as a recurring occurrence.
- It completes or expires exactly once and remains in history.
- The model reserves source metadata for a future calendar-import feature, but calendar import is not part of the MVP.

### Occurrence lifecycle

An occurrence is the dated, actionable instance of a recurring activity or one-off task.

States:

- `Pending`: visible and actionable.
- `Completed`: completed by a dashboard tap.
- `Expired`: remained unfinished when its action window closed.

There is no user-initiated skip state.

Allowed transitions:

- `Pending -> Completed`
- `Pending -> Expired`
- `Completed -> Pending` through undo while the occurrence remains within its action window

All transitions record timestamps and an audit event. Repeated or simultaneous completion requests are idempotent: one request wins and all clients converge on the stored result.

### Midnight bleed

- The household has a global bleed-window setting, initially defaulted to two hours.
- An activity or one-off task may override the global value.
- At local midnight, today's occurrences appear while unfinished occurrences from yesterday remain actionable until their bleed window ends.
- A carried occurrence is clearly labelled as belonging to yesterday.
- Completing it during the bleed window records completion against yesterday's occurrence.
- When the bleed window closes, a still-pending occurrence becomes `Expired`, is logged as unfinished, and leaves the dashboard.
- Bleed prevents a near-midnight activity from being lost; it does not create an accumulating backlog.

### Deadlines and urgency

A deadline is an optional local time on the occurrence date.

Each item selects one urgency mode:

- `None`: never receives urgent styling or animation.
- `AfterDeadline`: becomes urgent only after its deadline.
- `BeforeAndAfterDeadline`: becomes urgent during its warning window and remains urgent after its deadline.

The warning window is configurable per item and defaults to 30 minutes.

Urgent items use a strong red treatment and briefly flash brighter once every 60 seconds. The animation must stop when the item is completed or expires and must respect `prefers-reduced-motion`. Reduced-motion clients receive an equally prominent static treatment. Colour is never the only urgency indicator. Items configured with no urgency remain non-flashing after their deadline.

### Holidays

- Vendored Nager.Date source is the initial provider of country and first-level subdivision holiday data.
- Daybreak accesses it through `IHolidayProvider`, allowing another local provider to replace it later.
- Holidays are calculated in process. Daybreak has no holiday API, license-key, or runtime network dependency.
- The selected country, optional subdivision, and bundled provider version are visible in administration.
- The MVP consumes Nager.Date's public-holiday calculations as supplied; holiday-type filtering is deferred until provider requirements are confirmed.
- Updating the vendored provider must not change already completed historical occurrences.
- Activity policies support keeping, suppressing, moving earlier, or moving later when an occurrence falls on a selected holiday.
- Holiday suppression means no occurrence is generated; it is distinct from the removed user-facing skip state.
- Adjusted occurrences retain both their nominal and effective dates so the dashboard and history can explain a move.
- Collision and repeated-adjustment rules must be deterministic and previewed. Their detailed policy will be confirmed during the holiday milestone.

### Administration

The responsive administration interface includes:

- administrator login and logout;
- recurring activity list and editor;
- one-off task list and editor;
- schedule preview;
- household time zone;
- global midnight bleed window;
- holiday country, subdivision, and bundled provider status;
- history and analytics;
- archived activities;
- application version and health information.

Configuration changes that affect today's board are pushed to all connected dashboards immediately.

### History and analytics

The MVP includes:

- a recent event and occurrence log;
- completion rate by activity;
- on-time, late, and unfinished breakdowns;
- weekly and monthly trends;
- optional future-ready contribution data, hidden while the deployment has only the implicit user.

Streaks are excluded. Analytics should be factual and operational rather than competitive.

## Real-time synchronization

Real-time synchronization is a release requirement, not an enhancement.

- Every open dashboard receives completion, undo, expiry, one-off creation, activity changes, schedule changes, and relevant settings changes without manual refresh.
- Server-side writes occur in a transaction and use a concurrency token or conditional update.
- After a successful write, the server publishes a board-change notification.
- Clients respond by fetching the authoritative board snapshot rather than attempting to reconstruct state from events alone.
- Two simultaneous completion taps result in a single stored completion. The losing client receives the authoritative completed state rather than an error page.
- Each dashboard shows connection health.
- When disconnected, the dashboard is visibly stale and completion controls are disabled. The MVP does not queue offline mutations.
- Reconnection automatically fetches a full authoritative snapshot before controls are enabled.
- A monotonically increasing board revision lets clients detect missed or out-of-order notifications.
- Day rollover and expiry are server events and are broadcast to every connected dashboard.

For the single-process MVP, a process-local change broadcaster is sufficient. Its interface must allow a distributed broadcaster to be substituted if multi-instance hosting is supported later.

## Technical direction

### Application stack

- .NET 10
- ASP.NET Core
- Blazor with Interactive Server rendering
- Top-level statements in `Program.cs`
- SQLite
- Dapper
- A project-owned database manifest for schema generation and migration planning
- Project-owned, forward-only, ordered database migrations
- Built-in ASP.NET Core/SignalR transport for connected UI and board-change delivery
- PowerShell 7 for repository automation
- No Node.js or Python dependency in development, testing, builds, asset pipelines, or release tooling unless explicitly approved

The administration and kiosk experiences are responsive routes in one web application.

Daybreak is developed on Windows and published only as Linux Docker images. Native Windows, Windows Service, native Linux, systemd, and macOS deployments are not supported deliverables. Native AOT is not a project requirement for this Blazor application; the container uses the conventional .NET runtime publishing model.

### Date and time rules

- The household has one configured IANA time zone.
- Occurrence identity is based on the activity and nominal local date, not a UTC timestamp.
- Instants such as completion time are stored in UTC.
- Nominal dates, effective dates, and local deadlines are stored explicitly.
- Deadline instants are resolved using the household time zone.
- Daylight-saving gaps and ambiguous times use documented deterministic resolution rules and are covered by tests.
- Changing the household time zone must not rewrite completed history silently.

### Suggested domain model

- `HouseholdSettings`: time zone, default bleed duration, holiday configuration, board revision.
- `Activity`: durable recurring definition and presentation data.
- `RecurrenceRule`: recurrence type and normalized parameters.
- `OneOffTask`: standalone scheduled definition plus future source metadata.
- `Occurrence`: nominal/effective date, deadline, action-window end, state, completion instant, nullable completing actor, and concurrency version.
- `OccurrenceEvent`: append-only completion, undo, expiry, and administrative correction audit records.
- `IHolidayProvider`: a process-local boundary over vendored holiday calculation source.

Exact tables are to be fixed in an architecture decision record before the first migration is committed.

### Occurrence generation

- The server materializes occurrences deterministically and idempotently for a rolling horizon.
- A unique key prevents duplicate generation.
- Generation runs at startup, after relevant configuration changes, and from a background service as the horizon advances.
- Schedule edits do not silently rewrite completed occurrences.
- Changes to future pending occurrences are previewed and applied transactionally.
- Expiry is enforced by the server, never solely by a client timer.

## Delivery plan

Each milestone ends with a runnable, demonstrable vertical slice.

### Milestone 0: Repository foundation

- Create solution and project structure.
- Add MIT license, contribution guidance, code style, dependency-license policy, and dependency inventory generation.
- Use top-level statements in the application entry point.
- Add PowerShell 7 commands for formatting, building, testing, migrations, Docker, and release checks.
- Add working repository-local VS Code launch and task configurations.
- Add the database manifest, SQLite migration runner, and an empty initial schema.
- Add Docker development and production builds.
- Add a seeded demonstration mode that never contains real household data.
- Initialize the Git repository with the repository-local Wixely GitHub identity.

Exit: a clean checkout builds and runs on the Windows development environment, passes tests, migrates an empty database, and starts as a Linux container through Docker.

### Milestone 1: First synchronized checklist

- Implement settings, activities, daily recurrence, and occurrences.
- Implement the dashboard grid and administration password flow.
- Complete and undo an occurrence.
- Broadcast changes and reconcile every connected dashboard.
- Add connection and stale-state presentation.

Exit: configure a daily activity, open two browser sessions, complete it in one, and see both display the same result without refreshing.

### Milestone 2: Scheduling and time boundaries

- Add all agreed recurrence patterns and schedule preview.
- Add household time-zone handling.
- Add deadlines, sorting, urgency modes, and reduced-motion treatment.
- Add global and per-item midnight bleed.
- Add server-driven rollover and expiry.
- Cover daylight-saving and month-end edge cases.

Exit: recurrence and expiry behave predictably across midnight, month boundaries, and daylight-saving transitions.

### Milestone 3: One-off tasks

- Create, edit, and remove future one-off tasks.
- Show them in the same dashboard and history model.
- Apply deadline, urgency, expiry, bleed, undo, and real-time rules.
- Reserve import-source metadata without implementing import.

Exit: an administrator creates a one-off task and every relevant dashboard updates immediately.

### Milestone 4: Holiday adjustment

- Implement the holiday-provider boundary and Nager.Date adapter.
- Cache and refresh holiday years.
- Add country/subdivision/type configuration.
- Implement holiday policies and adjustment previews.
- Finalize collision and repeated-adjustment rules.

Exit: an administrator can explain exactly why an occurrence stayed, moved, or was not generated.

### Milestone 5: History and analytics

- Add searchable occurrence/event history.
- Add completion, timing, unfinished, and trend summaries.
- Keep member-comparison presentation dormant until named members exist.
- Verify that archived activities remain understandable in history.

Exit: the recommended operational history is useful without exposing or inventing member attribution.

### Milestone 6: Release hardening

- Accessibility and reduced-motion audit.
- Responsive browser-layout verification.
- Concurrency, reconnect, migration, backup, and restore testing.
- Health endpoints and structured logging without private activity data.
- Windows development workflow verification.
- Linux container runtime verification.
- Docker Compose example with persistent storage and the built-in image password.

Exit: a new operator can deploy, upgrade, back up, restore, and troubleshoot Daybreak from the documentation.

### Milestone 7: Public release

- Publish source under MIT on GitHub.
- Build release artifacts through GitHub Actions.
- Publish `linux/amd64` and `linux/arm64` images to GitHub Container Registry.
- Use semantic versions and immutable version tags.
- Publish stable images only from tagged releases; pull requests build and test without publishing stable images.
- Review the generated third-party dependency and license inventory.
- Complete the full outgoing-range privacy review, including commit metadata, filenames, text, images, binaries, and embedded metadata.

Exit: version `1.0.0` is reproducibly buildable, documented, and deployable from GHCR.

## Test strategy

### Unit tests

- Every recurrence pattern and boundary.
- Occurrence identity and idempotent generation.
- Deadline and urgency calculations.
- Midnight bleed and expiry.
- Holiday adjustment and collisions.
- Time-zone and daylight-saving resolution.
- Dashboard ordering.

### Integration tests

- Migrations from every supported schema version.
- Conditional completion and simultaneous taps.
- Complete, undo, expire, and configuration-change broadcasts.
- Disconnect, reconnect, and authoritative reconciliation.
- Administrator authentication and session invalidation.
- Nager.Date cache refresh and provider failure behaviour.

### End-to-end acceptance tests

- Two dashboards remain synchronized through every board mutation.
- A disconnected dashboard is visibly stale and cannot submit a completion.
- A carried occurrence completes against its nominal day.
- Urgency presentation respects reduced-motion settings.
- The common dashboard and administration flows work in a responsive browser layout.

## Release acceptance criteria

The MVP is ready when:

- a household can deploy it with Docker and persistent SQLite storage;
- administration is password protected while the dashboard remains URL-accessible;
- recurring and one-off items appear on the correct local day;
- one action completes an item and every connected dashboard updates without refresh;
- simultaneous taps cannot produce conflicting completions;
- disconnected dashboards clearly report stale state and recover automatically;
- unfinished items expire into history after the configured bleed window;
- deadlines, urgency, animation, and reduced-motion behaviour match configuration;
- holiday adjustments are predictable and explainable;
- history reports completed, late, and unfinished activity accurately;
- upgrade, backup, restore, Windows development, and Linux Docker instructions are tested;
- all included third-party code has an approved license, preferably MIT or Apache-2.0.

## Deferred capabilities

- Named household members and member switching.
- Authentication or PINs for dashboard actions.
- Manual dashboard ordering and dashboard sections.
- Calendar import for one-off tasks.
- Holiday-type filtering beyond Nager.Date's public-holiday feed.
- Browser notifications, web push, email, and other outbound reminders.
- Offline completion queues.
- Conditional or dependency-based routines.
- Home-automation and task-manager integrations.
- Multi-household or multi-tenant hosting.
- Multi-instance server deployment.

## Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Recurrence rules become surprising | High | Limit the initial rule set, preview dates, and test boundaries exhaustively. |
| Two browser sessions act simultaneously | High | Conditional database writes, idempotent commands, board revisions, and authoritative refresh. |
| A disconnected browser shows stale state | High | Prominent connection state, disabled mutations, automatic reconnect, and full reconciliation. |
| Midnight bleed creates duplicates or backlog | Medium | Preserve nominal occurrence identity, label carried items, and expire after a bounded window. |
| Animation is distracting or inaccessible | Medium | Opt-in urgency, restrained motion, reduced-motion support, and static non-colour cues. |
| Holiday data changes or its provider becomes unsuitable | Medium | Provider abstraction, local cache, explicit refresh, and the ability to maintain a fork. |
| Schedule edits rewrite history | High | Treat occurrences as historical records and preview changes to future pending items only. |
| An exposed dashboard permits unwanted changes | Medium | Clearly document the trusted-network model and reverse-proxy options; add dashboard authentication later if required. |
| SQLite data is lost during container replacement | High | Require a mounted data directory and provide tested backup/restore documentation. |
| The product expands into a generic task manager | Medium | Evaluate features against the daily household-board workflow. |

## Decisions still required during implementation

These do not block repository foundation or the first synchronized slice:

- Long-term Nager.Date refresh horizon and whether the provider needs a maintained fork.
- Duration and presentation of the dashboard undo affordance.
- Retention policy for detailed audit events, if different from permanent occurrence history.
- Whether BSD and similarly permissive licenses are pre-approved alongside MIT and Apache-2.0.

## Immediate next actions

- [x] Record the application/deployment and server-authoritative synchronization decisions. — Owner: Codex — Completed: 2026-08-19
- [x] Implement the responsive dashboard states and administration flows. — Owner: Codex — Completed: 2026-08-19
- [x] Scaffold the .NET 10 solution and test projects with PowerShell and VS Code support. — Owner: Codex — Completed: 2026-08-19
- [x] Define the database manifest and initial ordered migration. — Owner: Codex — Completed: 2026-08-19
- [x] Implement Milestone 1 as a synchronized vertical slice. — Owner: Codex — Completed: 2026-08-19
- [x] Verify two subscribed dashboard clients converge on the same revision in integration tests. — Owner: Codex — Completed: 2026-08-19
- [x] Verify formatting, the 40-test Release suite, Linux AMD64/ARM64 publishes, dependency licenses, vulnerabilities, and deprecations. — Owner: Codex — Completed: 2026-08-19
- [x] Build and run the production image with a Linux OCI runtime; verify fresh-volume ownership, non-root execution, direct-password login, persistence, backup, and restore. — Owner: Codex — Completed: 2026-08-19
- [x] Build and run `docker compose up --build` on Docker Engine; verify the built-in image password, non-root process, health endpoint, and named-volume recreation. — Owner: Codex — Completed: 2026-08-19
- [x] Verify login, completion, undo, and administration-to-dashboard synchronization in live browser sessions. — Owner: Codex — Completed: 2026-08-19
