# ADR 0003: Holiday adjustment and collisions

- Status: Accepted
- Date: 2026-08-19

## Context

Moving a recurring occurrence away from a holiday can cross another holiday or land on the effective date of another occurrence. Daybreak must preserve recurrence identity and make the result explainable without silently dropping household work.

## Decision

Holiday adjustment starts from the nominal local date and moves one calendar day at a time in the configured direction until it reaches a non-holiday date. The search is bounded to 14 days; exceeding that bound fails generation visibly instead of guessing.

Suppressed occurrences are not materialized. Moved occurrences retain both their nominal and effective dates. If multiple nominal occurrences resolve to the same effective date, each remains a separate occurrence and dashboard card. A collision does not merge, suppress, or displace any occurrence. Administration previews moved, suppressed, and colliding dates before the activity is saved.

Completed and expired occurrences are historical records and are never rewritten by holiday-provider updates. Only pending occurrences can be regenerated after a schedule, settings, or bundled-provider change.

## Consequences

- A busy day may contain several cards for one recurring activity after consecutive holidays.
- History can always explain which scheduled day produced each card.
- Provider changes cannot alter completed household history.
- Any future collision-merging policy requires a new decision and migration strategy.

## Review trigger

Review if households need business-day calendars, weekend exclusion, collision merging, or provider-specific observance rules.

The selected-weekday extension and shared explanation/projection model are recorded in [ADR 0005](0005-explainable-schedule-projection.md).
