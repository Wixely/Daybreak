# ADR 0002: Server-authoritative dashboard synchronization

- Status: Accepted
- Date: 2026-08-19

## Context

Several browser sessions may show the same board. Completion, undo, expiry, rollover, and configuration changes must converge promptly, including simultaneous actions.

## Decision

Commit mutations transactionally with idempotent commands and conditional state transitions. Increment a monotonic board revision, notify every connected dashboard, and have each dashboard fetch the authoritative snapshot. A simultaneous losing command accepts the stored result. Disconnected dashboards show a blocking stale overlay and do not queue mutations.

The MVP uses an in-process change broadcaster because one server process is supported. The broadcaster remains behind a service boundary.

## Consequences

- Events announce that state changed; they are not themselves the client state model.
- Reconnection always reconciles from a complete snapshot.
- Day rollover emits an explicit revision even when tomorrow's occurrences were materialized earlier.
- Multi-instance hosting requires a distributed broadcaster before it can be supported.

## Review trigger

Review before adding offline mutation queues, multiple server instances, or boards large enough that snapshot reads become material.
