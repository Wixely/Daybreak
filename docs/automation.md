# API and MCP automation

Daybreak automation is an optional trusted-network feature. Read the security model in [ADR 0004](decisions/0004-agent-api-and-mcp-activation.md) before exposing either endpoint through a reverse proxy.

## Activation

Set these container environment values and restart Daybreak:

```text
Daybreak__EnableApi=true
Daybreak__EnableMcp=true
```

Then sign in to **Configure → Settings**. Generate and copy an API key, enable API, and save. MCP additionally requires API to remain enabled; its own MCP key is optional. Daybreak shows plaintext keys only when they are generated.

Disabled or unavailable endpoints return `404`. A surface that requires a key returns `401` with `WWW-Authenticate: Bearer` when the key is absent or invalid.

## HTTP API

The administration page displays the absolute API base URL. Supply the generated API key in the authorization header:

```console
curl -H "Authorization: Bearer <api-key>" https://daybreak.example/api/v1
curl -H "Authorization: Bearer <api-key>" https://daybreak.example/api/v1/board
```

The v1 endpoints are:

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1` | API identity and endpoint catalogue |
| `GET` | `/api/v1/board` | Authoritative current board and revision |
| `POST` | `/api/v1/occurrences/{id}/complete` | Complete with `{ "expectedVersion": number }` |
| `POST` | `/api/v1/occurrences/{id}/undo` | Undo with `{ "expectedVersion": number }` |
| `GET` | `/api/v1/activities?includeArchived=false` | List recurring activities |
| `POST` | `/api/v1/activities` | Create an activity |
| `PUT` | `/api/v1/activities/{id}` | Replace an activity definition |
| `POST` | `/api/v1/activities/{id}/archive` | Archive an activity |
| `POST` | `/api/v1/activities/{id}/restore` | Restore an activity |
| `GET` | `/api/v1/one-off-tasks` | List editable one-off tasks |
| `POST` | `/api/v1/one-off-tasks` | Create a one-off task |
| `PUT` | `/api/v1/one-off-tasks/{id}` | Replace an editable one-off task |
| `DELETE` | `/api/v1/one-off-tasks/{id}` | Delete an editable pending one-off task |

One-off task write requests accept `isPermanent`. When true, an unfinished task carries forward on the board until it is completed.
| `GET` | `/api/v1/settings` | Read household scheduling settings |
| `PUT` | `/api/v1/settings` | Update household scheduling settings |
| `GET` | `/api/v1/history?recentLimit=100` | Read history and summaries; limit is clamped to 1–500 |
| `POST` | `/api/v1/schedule-preview?count=8` | Preview explanations, visibility, urgency, deadlines, bleed, adjusted dates, suppression, and collisions |

Enums use their names, such as `Daily`, `BeforeAndAfterDeadline`, and `MoveLater`. Holiday policies additionally include `MoveToPreviousWeekday` and `MoveToNextWeekday`; these require `holidayTargetWeekday` from `0` (Sunday) through `6` (Saturday). A weekday target is authoritative even when that target is also marked as a holiday, and the preview reports a warning in its adjustment explanation.

Create and update bodies follow the fields returned by the corresponding list endpoint, excluding identifiers and server timestamps. Schedule preview returns the generated plain-English rule record and, for every occurrence, `nominalDate`, `effectiveDate`, `visibleFrom`, `urgentFrom`, `deadline`, `actionWindowEnd`, collision state, and its adjustment explanation. These are calculated by the same projection code used to materialize dashboard occurrences.

Completion and undo are conditional. Send the occurrence's current `version`; the response reports whether the transition was applied and always includes a fresh authoritative board. Clients should converge on that returned snapshot when another client won the race.

## MCP

The administration page displays the absolute stateless Streamable HTTP URL ending in `/mcp`. A typical client entry is:

```json
{
  "mcpServers": {
    "daybreak": {
      "type": "http",
      "url": "https://daybreak.example/mcp",
      "headers": {
        "Authorization": "Bearer <mcp-key>"
      }
    }
  }
}
```

Omit `headers` when the administrator deliberately enables MCP without generating an MCP key. Anyone who can reach that endpoint can then invoke every tool, so keep it on a trusted network.

The server exposes these discoverable tools:

- `get_board`, `complete_occurrence`, and `undo_occurrence`;
- `list_activities`, `save_activity`, `archive_activity`, and `restore_activity`;
- `list_one_off_tasks`, `save_one_off_task`, and `delete_one_off_task`;
- `get_settings`, `update_settings`, `preview_schedule`, and `get_history`.

MCP tools and HTTP endpoints call the same application operations. Mutations retain Daybreak's conditional occurrence versions, board revisions, audit events, and dashboard notifications.

## Rotation and auditing

Generating a replacement key immediately invalidates the old key. API keys cannot be removed while MCP depends on API access. MCP keys can be removed; if MCP remains enabled, removal deliberately changes it to unauthenticated mode.

Successful automation requests record the surface, non-secret key suffix when present, method, path, outcome, correlation identifier, and timestamp. Keys, activity titles, notes, and history content are not written to automation access records.
