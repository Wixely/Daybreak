# ADR 0001: Application and deployment model

- Status: Accepted
- Date: 2026-08-19

## Context

Daybreak needs one responsive browser interface, password-protected administration, server-owned persistence, real-time interaction, and a narrow self-hosting path. Development takes place on Windows and deployment is container-only.

## Decision

Use .NET 10 with top-level C#, Blazor Interactive Server, SQLite, Dapper, a project-owned schema manifest, and ordered migrations. Develop on Windows and publish only Linux AMD64 and ARM64 Docker images. Do not require Native AOT and do not produce native service distributions.

## Consequences

- A continuous browser connection is required, so stale and reconnect states are part of the primary experience.
- Docker persistence, upgrade, backup, and restore are release-critical workflows.
- PowerShell scripts and VS Code configuration support the Windows development workflow.
- Docker execution is the only supported production deployment shape.

## Review trigger

Review if operators need a native distribution or if browsers must accept offline actions.
