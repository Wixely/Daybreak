# Third-party notices

Daybreak's direct runtime dependencies are:

| Package | Use | License |
| --- | --- | --- |
| Dapper | Database mapping | Apache-2.0 |
| Microsoft.AspNetCore.App.Internal.Assets | Blazor framework JavaScript copied into the published image | MIT |
| Microsoft.Data.Sqlite | SQLite provider | MIT |
| ModelContextProtocol.AspNetCore v2.2.0 | Stateless Streamable HTTP MCP server | Apache-2.0 |
| Nager.Date v2.44.0 | Vendored public-holiday calculation source, modified for Daybreak's local build | MIT |

Daybreak uses no third-party CSS or JavaScript from a CDN. Its application CSS and icon are project-owned assets committed under `src/Daybreak/wwwroot`.

Nager.Date is vendored under `vendor/Nager.Date` from upstream commit `0be62ac62e2176633c04bbaa9b4601b5978e23e8`. Its original MIT license is retained at `vendor/Nager.Date/LICENSE`; Daybreak's modifications and provenance are documented in `vendor/Nager.Date/DAYBREAK-VENDORING.md`.

Test dependencies are MSTest.TestFramework, MSTest.TestAdapter, Microsoft.NET.Test.Sdk, and Microsoft.AspNetCore.Mvc.Testing, all licensed under MIT.

Transitive dependency versions are locked in each project's `packages.lock.json`. Run `./scripts/Dependencies.ps1` after restore to inspect every resolved NuGet package and its declared license. Release review must investigate any package that does not declare MIT or Apache-2.0.
