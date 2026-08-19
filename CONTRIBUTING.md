# Contributing to Daybreak

Daybreak welcomes focused fixes and improvements that strengthen the shared daily household board.

## Before opening a change

- Keep the recurrence-driven daily dashboard workflow central.
- Discuss broad task-management features before implementing them.
- Do not introduce Node.js or Python tooling.
- Vendor browser assets into the application or a pinned package that is copied into the published image; do not add CDN-hosted CSS, JavaScript, fonts, or icons.
- Prefer MIT or Apache-2.0 dependencies. Explain and review any exception first.
- Never include real household data, credentials, machine-specific paths, or identifying screenshots.

## Verify a change

Run from PowerShell 7:

```powershell
./scripts/Verify.ps1
./scripts/Dependencies.ps1
```

Docker changes also require a successful `docker build .` on a machine with Docker available.

Changes to recurrence, holidays, deadlines, time zones, bleed, expiry, or synchronization should include focused tests. User-interface changes should be checked in a responsive browser layout and with reduced motion enabled.
