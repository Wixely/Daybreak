# ADR 0005: Explainable schedule projection

- Status: Accepted
- Date: 2026-08-24

## Context

Some household services do not move to the nearest non-holiday date. A Monday bin collection, for example, can move to the previous Saturday. The existing move-earlier rule stops on Sunday and cannot represent that instruction. Separately implemented administration, API, and occurrence-generation previews could also drift from the timestamps that actually reach the dashboard.

Show-early, urgency, deadline, bleed, holiday movement, and recurrence overlap need to be understandable before an activity is saved. Historical moved occurrences must remain explainable after their activity is edited.

## Decision

Daybreak adds two holiday policies: move to the previous selected weekday and move to the next selected weekday. The activity stores a nullable target weekday. These policies trigger only when the nominal date is a configured public holiday. The selected weekday is authoritative; if it is also a holiday, Daybreak keeps that target and warns in the preview instead of silently jumping another week.

A shared schedule projector calculates nominal and effective dates, visibility start, urgency start, deadline, effective-day end, action-window end, collisions, and a concrete adjustment explanation. Occurrence generation, administration, HTTP API preview, and MCP preview consume that projection. Generated occurrences snapshot their adjustment explanation alongside their nominal and effective dates.

Administration renders generated plain-English recurrence, holiday, show-early, bleed, and urgency statements. It also renders a three-local-day time simulation. Each recurrence identity receives a separate lane and simulated card, so bleed and holiday collisions can intentionally display multiple copies of the same activity at once. Day boundaries are resolved in the household time zone and may contain 23 or 25 elapsed hours across daylight-saving changes.

Existing `Keep`, `Suppress`, `MoveEarlier`, and `MoveLater` values retain their stored numeric values and behavior. Completed and expired occurrences remain immutable. Pending occurrences continue to be regenerated from the current structured rule.

## Consequences

- “Monday holiday → previous Saturday” is directly representable and recorded in plain English.
- Preview facts and generated occurrence timestamps share one implementation boundary.
- API and MCP clients receive enough timing facts to reproduce the administration preview.
- A selected target weekday that is itself a holiday is visible rather than silently reinterpreted.
- Adding more complex chained exception rules would require a new structured rule model and migration.

## Review trigger

Review if households need several ordered exception rules, provider event names, editable free-form notes attached to rules, or target-weekday fallback behavior.
