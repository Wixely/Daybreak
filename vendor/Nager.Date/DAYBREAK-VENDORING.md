# Daybreak vendoring notes

Daybreak vendors Nager.Date v2.44.0 from upstream commit
`0be62ac62e2176633c04bbaa9b4601b5978e23e8` using `git subtree`.

The vendored build is compiled directly by Daybreak and differs from upstream in
the following ways:

- offline license-key enforcement and the `Nager.LicenseSystem` dependency are removed;
- the assembly identity is `Daybreak.Nager.Date` while public namespaces remain
  `Nager.Date` for source compatibility;
- only `net10.0`, Daybreak's target framework, is built;
- automatic NuGet package generation is disabled.

Upstream remains copyright Tino Hager and contributors and is distributed under
the MIT license in `LICENSE`. Daybreak's changes are also distributed under MIT.

To update, pull a reviewed upstream tag into `vendor/Nager.Date` with
`git subtree pull`, resolve any overlap with the changes above, and run the
complete Daybreak verification suite before committing the update.
