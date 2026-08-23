# Agent API and MCP activation

- Status: Accepted
- Date: 2026-08-23

## Context

Daybreak needs optional automation access without making either automation surface available merely because a container was started. Operators need an explicit deployment-level capability gate and a separate in-application activation step. MCP tools use the same server-authoritative application operations as the HTTP API and therefore depend on an active API credential.

## Decision

Daybreak has two deployment settings, `Daybreak__EnableApi` and `Daybreak__EnableMcp`, both disabled by default. A setting makes its administration section visible but does not activate its endpoint.

An administrator must then:

1. generate the single active API key;
2. enable API access with its checkbox;
3. optionally generate the single active MCP key; and
4. enable MCP access with its checkbox.

MCP cannot be enabled unless API access is enabled and an API key exists. An MCP key is optional: when none exists, an explicitly enabled MCP endpoint accepts unauthenticated clients on the trusted network and the administration page displays a prominent warning. Generating a new key immediately revokes the previous key of that type. Removing the MCP key returns MCP to the explicitly unauthenticated mode; the API key cannot be removed while MCP is enabled.

Generated plaintext keys are displayed only in the administration session that created them. Daybreak stores a one-way SHA-256 digest, a non-secret suffix for identification, and creation time. Keys are supplied as bearer credentials and are never embedded in endpoint URLs, request logs, audit details, or copyable links.

The API base link is `/api/v1`; the MCP link is `/mcp` using stateless Streamable HTTP. Links are rendered as absolute URLs from the current administration request so reverse-proxy hosts and path bases are preserved.

Endpoint availability requires both gates:

| Surface | Deployment gate | Application gate | Credential rule |
| --- | --- | --- | --- |
| API | `Daybreak__EnableApi=true` | API checkbox enabled | Active API bearer key required |
| MCP | `Daybreak__EnableMcp=true` | MCP checkbox enabled | API enabled with an active API key; MCP bearer key required only when one exists |

Unknown or unavailable automation endpoints return `404` so disabled features do not advertise themselves. Authenticated requests use existing application services, conditional occurrence versions, board revisions, and notifications. Automation access is audited by credential type and suffix, HTTP method, route, outcome, correlation identifier, and timestamp without recording keys or household content.

## Consequences

- A container configuration change and an authenticated administrator action are both required before automation is reachable.
- API and MCP rotation are simple but support one active credential of each type rather than a multi-client key catalogue.
- Operators who deliberately run MCP without an MCP key accept trusted-network-only access and receive a warning in administration.
- API clients must send `Authorization: Bearer <api-key>`; MCP clients use the same header only when an MCP key has been generated.
- Future per-client credentials and least-privilege scopes require a new decision and schema extension.

## Review triggers

Review before supporting multiple simultaneous keys, externally exposed unauthenticated MCP, OAuth, per-key scopes, multi-household hosting, or a separate MCP companion container.
