# Third-party notices

Daybreak's direct runtime dependencies are:

| Package | Use | License |
| --- | --- | --- |
| Dapper | Database mapping | Apache-2.0 |
| Microsoft.AspNetCore.App.Internal.Assets | Blazor framework JavaScript copied into the published image | MIT |
| Microsoft.Data.Sqlite | SQLite provider | MIT |

Daybreak uses no third-party CSS or JavaScript from a CDN. Its application CSS and icon are project-owned assets committed under `src/Daybreak/wwwroot`.

Test dependencies are MSTest.TestFramework, MSTest.TestAdapter, Microsoft.NET.Test.Sdk, and Microsoft.AspNetCore.Mvc.Testing, all licensed under MIT.

Transitive dependency versions are locked in each project's `packages.lock.json`. Run `./scripts/Dependencies.ps1` after restore to inspect every resolved NuGet package and its declared license. Release review must investigate any package that does not declare MIT or Apache-2.0.
