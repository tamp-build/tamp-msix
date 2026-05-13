# Changelog

All notable changes to **Tamp.Msix** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

## [0.2.0] — pending — password-protected PFX signing (TAM-191) + `Msix.NormalizeMsixVersion` promoted to public

### Added

- **`Msix.NormalizeMsixVersion(string)`** is now `public` (was `internal` in 0.1.0).
  Build scripts that compose MSIX filenames from a `[Parameter] string Version`
  can call it directly. DasBook canary friction: my own README sketch promised
  this helper as `public` but 0.1.0 shipped it `internal` so the sketch failed
  to compile. 0.2.0 makes the helper match the docs.

  ```csharp
  AbsolutePath MsixOut => Artifacts / $"DasBook_{Msix.NormalizeMsixVersion(Version)}_x64.msix";
  ```

- **`SignToolSignSettings.SetPassword(Secret)`** — `signtool sign /f cert.pfx /p <pwd>`.
  Pairs with `SetCertificateFile(path)` to sign with a password-protected PFX file.
  `Secret`-typed so the value is masked in Tamp's printed `CommandPlan` trace.

  **Caveat:** signtool does NOT support env-var-routed passwords for `/p` — the
  cleartext goes on the process argument table for the lifetime of the signtool
  process. The `Secret` wrapper keeps it out of Tamp's logs but cannot hide it
  from the OS process table. This is a signtool design limitation we can't work
  around; documented prominently on the type.

  Validation: `Password` requires `CertificateFile` to be set. Pairing with
  `Sha1Thumbprint` or `SubjectName` (store-resident certs) throws
  `InvalidOperationException` — those paths don't have a password slot.
  Unencrypted PFX continues working (Password is opt-in).

Originally deferred from 0.1.0 because `Secret.Reveal()` required
`InternalsVisibleTo` on Tamp.Core. Tamp.Core 1.6.0 made `Reveal()` public +
TAMP004-analyzer-gated; the `SignToolSignSettings` class-name suffix is the
canonical approved context, so no IVT entry was ever needed. The deferral
became a no-op once 1.6.0 shipped — TAM-191 picked it up cleanly.

### Tests

- 5 new unit tests in `MsixTests`: password emits `/p`, Secret flows through
  `CommandPlan.Secrets`, password without CertificateFile throws, password
  with SubjectName throws, unencrypted PFX still works (`/p` opt-in),
  password combined with timestamp URL.

## [0.1.0] - 2026-05-13

### Added

- Initial release. Wraps Microsoft's `makeappx.exe` + `signtool.exe` MSIX toolchain.
  Filed under TAM-189. Third non-.NET satellite, following `Tamp.Cargo` and
  `Tamp.Tauri.V2`. Together the three cover the canonical Windows desktop ship
  pipeline (Rust core → Tauri bundle → MSIX package → signed artifact).

#### makeappx verb surface

- **`Msix.Pack(...)`** — `makeappx pack`. Source mode is either `/d <dir>` or
  `/f <mapping-file>`, validated mutually exclusive. `/p` output, `/o` overwrite
  (on by default for build-script ergonomics), `/nv` no-validation, `/v` verbose.
- **`Msix.Unpack(...)`** — `makeappx unpack`. `/p` package, `/d` output dir.
- **`Msix.Bundle(...)`** — `makeappx bundle`. `/d` source dir, `/p` output bundle,
  `/bv` bundle version.

#### signtool verb surface

- **`Msix.Sign(...)`** — `signtool sign`. Cert selectors mutually exclusive
  (`/f cert.pfx` unencrypted, `/sha1 thumbprint`, `/n subject-name`, or
  `/a` auto-select). `/fd sha256` default; `/tr <url>` + `/td sha256` for RFC3161
  timestamping. At least one file required.
- **`Msix.Verify(...)`** — `signtool verify`. `/pa` authentication policy
  default-on; `/all` for multi-signature checks.
- **`Msix.SignToolRaw(...)`** — escape hatch for `timestamp`, `catdb`, etc.

#### AppxManifest.xml version helpers — the load-bearing part

- **`Msix.GetAppxManifestVersion(path)`** — reads `Package/Identity/@Version`,
  returns `null` if the file or attribute is missing.
- **`Msix.SetAppxManifestVersion(path, version)`** — writes the attribute.
  Accepts 3-part SemVer (`1.0.6`) or full 4-part MSIX (`1.0.6.42`); 3-part is
  normalized to `1.0.6.0` to satisfy the manifest schema. Throws
  `ArgumentException` for non-numeric or wrong-shape inputs;
  `FileNotFoundException` if the manifest doesn't exist;
  `InvalidOperationException` if the file has no `Identity` element.

  This removes the bug class where the manifest version drifts from
  `Cargo.toml` / `package.json` because nobody remembers to hand-edit the XML
  during a release.

### Deferred

- **Password-protected PFX support (`/f cert.pfx /p <password>`)** is
  intentionally NOT typed in 0.1.0. It needs `Tamp.Msix` on `Tamp.Core`'s
  `InternalsVisibleTo` list to `Reveal()` the password into the command line
  safely. Filed as **TAM-191** for the 0.2.0 wave. Until then adopters use one
  of the unencrypted cert selectors (`/sha1`, `/n`, `/a`, or unencrypted `/f`)
  or fall back to `Msix.SignToolRaw(...)` with adopter-managed env.

### Mutually-exclusive option enforcement

- Pack with both `/d` and `/f` → `InvalidOperationException` at
  `ToCommandPlan(...)` time, not at runtime after the slow tool launches.
- Sign with two cert selectors set → same fail-fast pattern.

### Notes

- Third non-.NET satellite. Continues the toolchain-wrapper template established
  by `Tamp.Cargo` and reinforced by `Tamp.Tauri.V2`: settings derive from a
  shared `MakeAppxSettingsBase` (for the manifest-driven verbs) or per-verb
  classes (for signtool); fluent setters return `this`; `Raw` escape hatch for
  verbs not yet typed; multi-targets net8 / net9 / net10.

- Windows-only at runtime — `makeappx.exe` and `signtool.exe` ship in the
  Windows SDK and don't have non-Windows analogs. The .NET assembly itself
  builds and unit-tests on macOS / Linux (settings classes have no Windows-API
  dependencies); only execution requires Windows.

- 28 unit tests cover the positive paths plus negative cases: pack
  mutual-exclusion, sign cert-selector mutual exclusion, missing-output
  validation, AppxManifest version round-trip (3-part → 4-part normalization,
  4-part passthrough, invalid-input rejection across six shapes), and
  missing-file / missing-element error paths.
