# Security policy

## Supported versions

Until the first stable release, only the latest commit on the default branch receives security fixes. After `1.0.0`, supported release lines will be documented here.

## Reporting a vulnerability

Report vulnerabilities privately to the repository owner. Do not place credentials, household activity data, deployment addresses, or exploit details in a public issue.

## Deployment boundary

The dashboard is intentionally writable without authentication. Treat its URL as a trusted-household surface and restrict network access accordingly. Place Daybreak behind an HTTPS reverse proxy before exposing it outside the local network. Configuration routes require the deployment-supplied administrator password.
