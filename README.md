# Tamp.Msix

> Wrapper for the Windows `makeappx.exe` + `signtool.exe` toolchain plus typed `AppxManifest.xml` version helpers. Pairs with [`Tamp.Tauri.V2`](https://github.com/tamp-build/tamp-tauri) for the canonical Windows desktop ship pipeline.

| Package | Status |
|---|---|
| `Tamp.Msix` | 0.1.0 (initial) |

## Why this exists

Most Windows desktop apps have at least three places where a version number lives: `Cargo.toml`, `package.json`, and `AppxManifest.xml`'s `Identity/@Version`. The first two are SemVer (`1.0.6`). The third is the legacy 4-part Microsoft format (`1.0.6.0`). When the version is bumped, all three need to be updated, and the one that's most often forgotten is the manifest — because nobody reads XML attributes during a release.

There's a second tier of pain just downstream: `makeappx pack` and `signtool sign` have famously crusty argument syntaxes (`/d`, `/p`, `/f`, `/fd`, `/td`, `/tr`, `/pa`). They're documented but reading the docs every release is unpleasant. They get encoded into a `package.ps1` that bit-rots.

`Tamp.Msix` makes both surfaces a typed value in the build graph:

- `Msix.Pack(...)`, `Msix.Sign(...)`, `Msix.Verify(...)` for the CLI verbs (settings DSL, no flag-memorization).
- `Msix.GetAppxManifestVersion(...)` / `Msix.SetAppxManifestVersion(...)` for the manifest — accepts 3-part SemVer and normalizes to MSIX's 4-part format. One call updates `Identity/@Version` from a value the build graph already owns.

## Install

```bash
dotnet add package Tamp.Msix
```

Multi-targets net8 / net9 / net10. Windows-only at runtime (makeappx.exe and signtool.exe ship in the Windows SDK).

## Quick start — the full DasBook-style desktop ship pipeline

```csharp
using Tamp;
using Tamp.Cargo;
using Tamp.Tauri.V2;
using Tamp.Msix;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Parameter] readonly string Version = "1.0.6";

    [FromPath("cargo")] readonly Tool Cargo = null!;
    [FromNodeModules("tauri")] readonly Tool TauriCli = null!;
    [FromPath("makeappx")] readonly Tool MakeAppx = null!;
    [FromPath("signtool")] readonly Tool SignTool = null!;

    AbsolutePath ServiceCrate => RootDirectory / "dasbook-service";
    AbsolutePath SrcTauri => RootDirectory / "src-tauri";
    AbsolutePath AppxManifest => RootDirectory / "msix-package" / "AppxManifest.xml";
    AbsolutePath StagingDir => RootDirectory / "msix-package";
    AbsolutePath ArtifactsDir => RootDirectory / "artifacts";
    AbsolutePath Msix => ArtifactsDir / $"DasBook_{Msix.NormalizeMsixVersion(Version)}_x64.msix";

    const string TargetTriple = "x86_64-pc-windows-msvc";

    Target StampManifestVersion => _ => _
        .Description("[Pack] Sync AppxManifest.xml Identity/@Version with build version")
        .Executes(() => Msix.SetAppxManifestVersion(AppxManifest, Version));
        // 1.0.6 → 1.0.6.0 automatically; no separate "bump the manifest" step lost in tribal memory.

    Target BuildService => _ => _
        .Executes(() => Cargo.Build(s => s
            .SetWorkingDirectory(ServiceCrate)
            .SetRelease().SetTarget(TargetTriple).SetLocked()));

    Target StageSidecar => _ => _
        .DependsOn(nameof(BuildService))
        .Executes(() =>
        {
            var built = ServiceCrate / "target" / TargetTriple / "release" / "dasbook-service.exe";
            var sidecar = Tauri.ExternalBinPath(SrcTauri, "dasbook-service", TargetTriple);
            sidecar.Parent!.CreateDirectory();
            File.Copy(built.Value, sidecar.Value, overwrite: true);
        });

    Target BuildDesktop => _ => _
        .DependsOn(nameof(StageSidecar))
        .Executes(() => Tauri.Build(TauriCli, s => s
            .AddBundles("msi", "nsis")
            .SetTarget(TargetTriple)));

    Target PackMsix => _ => _
        .DependsOn(nameof(StampManifestVersion), nameof(BuildDesktop))
        .Executes(() => Msix.Pack(MakeAppx, s => s
            .SetSourceDirectory(StagingDir)
            .SetOutputPackage(Msix)));

    Target SignMsix => _ => _
        .DependsOn(nameof(PackMsix))
        .Executes(() => Msix.Sign(SignTool, s => s
            .AddFile(Msix)
            .SetSha1Thumbprint("ABCDEF1234567890ABCDEF1234567890ABCDEF12")
            .SetTimestampUrl("http://timestamp.digicert.com")));
        // Or .SetCertificateFile(...) for an unencrypted PFX. Password-protected
        // PFX support is filed as TAM-191 for the 0.2.0 wave — see "Deferred" below.

    Target Verify => _ => _
        .DependsOn(nameof(SignMsix))
        .Executes(() => Msix.Verify(SignTool, s => s.AddFile(Msix).SetVerifyAll()));
}
```

`StampManifestVersion → BuildService → StageSidecar → BuildDesktop → PackMsix → SignMsix → Verify` — one graph, no shell-script glue, no manifest-version-drift bug class.

## Verb surface

### makeappx

| Tamp method | makeappx verb | Notes |
|---|---|---|
| `Msix.Pack(...)` | `makeappx pack` | `/d` source dir **or** `/f` mapping file (mutually exclusive, validated). `/p` output, `/o` overwrite (on by default), `/nv` no-validation. |
| `Msix.Unpack(...)` | `makeappx unpack` | `/p` package, `/d` output dir. |
| `Msix.Bundle(...)` | `makeappx bundle` | `/d` source dir, `/p` output bundle, `/bv` bundle version. |

### signtool

| Tamp method | signtool verb | Notes |
|---|---|---|
| `Msix.Sign(...)` | `signtool sign` | Cert selectors mutually exclusive: `/f cert.pfx`, `/sha1 thumbprint`, `/n subject-name`, or `/a` auto-select. `/fd` defaults to `sha256`; `/tr` + `/td` for RFC3161 timestamping. |
| `Msix.Verify(...)` | `signtool verify` | `/pa` (authentication policy) default-on; `/all` for multi-signature checks. |
| `Msix.SignToolRaw(...)` | `signtool <anything>` | Escape hatch for `timestamp`, `catdb`, etc. |

### What's deferred

**Password-protected PFX support (`/f cert.pfx /p <password>`)** is intentionally NOT typed in 0.1.0. It needs `Tamp.Msix` on `Tamp.Core`'s `InternalsVisibleTo` list to `Reveal()` the password into the command line safely. Filed as **TAM-191** for the 0.2.0 wave when an adopter actually needs to sign with a password-protected PFX. Until then, use one of the unencrypted paths: `/sha1` (store-resident cert), `/n` (subject name), `/a` (auto-select), or unencrypted `/f cert.pfx`. Adopters who need password-protected signing immediately can fall back to `Msix.SignToolRaw(...)` and manage the password on their own env.

## `AppxManifest.xml` version helpers — the load-bearing part

```csharp
public static string? GetAppxManifestVersion(AbsolutePath appxManifestPath);
public static void    SetAppxManifestVersion(AbsolutePath appxManifestPath, string version);
internal static string NormalizeMsixVersion(string version);
```

- `Get` returns `Identity/@Version` from the manifest, or `null` if the file or attribute is missing.
- `Set` accepts either 3-part SemVer (`1.0.6`) or full 4-part MSIX (`1.0.6.42`); 3-part gets `.0` appended to satisfy the manifest schema. Non-numeric or oddly-shaped versions throw `ArgumentException` with a helpful message.
- `Normalize` is exposed via `internal` for `Tamp.Core` and for tests; in build scripts use `Set` directly.

The whole point is to **stop having a version-bump be three separate hand edits**. Cargo's `[package].version` and `package.json`'s `version` can already be driven by build parameters; the manifest joins them.

## Mutually-exclusive option enforcement

Where `makeappx` or `signtool` accept two flags that contradict each other (e.g. `/d` vs `/f` on pack, `/f` vs `/sha1` vs `/n` on sign), the wrapper throws `InvalidOperationException` at `ToCommandPlan(...)` time — before the slow tool launches. Catches the class of "I added both flags during refactoring and signtool failed 30 seconds in" bug at build-graph compile time.

## Tool resolution

Both tools ship in the Windows SDK:

- `makeappx.exe` — typically resolved via `[FromPath("makeappx")]` after VS or Windows SDK setup adds it to the PATH. For pinned versions, point a `Tool` at `C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\makeappx.exe`.
- `signtool.exe` — same path family. `[FromPath("signtool")]` works once the SDK is on PATH.

CI environments where the SDK isn't on PATH by default: GitHub Actions's `windows-latest` image preinstalls the SDK; add `setup-msbuild` or call the developer command prompt activation script before invoking the build.

## Sibling packages

- [`Tamp.Cargo`](https://github.com/tamp-build/tamp-cargo) — Rust toolchain. Upstream of the externalBin sidecar.
- [`Tamp.Tauri.V2`](https://github.com/tamp-build/tamp-tauri) — Tauri 2.x CLI + `ExternalBinPath` helper. Produces the artifacts that get packaged into MSIX.
- [`Tamp.Npm.V10`](https://github.com/tamp-build/tamp-npm) — frontend tooling driver.

## Releasing

Releases follow the [Tamp dogfood pattern](MAINTAINERS.md): bump `<Version>` in `Directory.Build.props`, tag `v<X.Y.Z>`, GitHub Actions runs `dotnet tamp Ci` then `dotnet tamp Push`.

## License

MIT. See [LICENSE](LICENSE).
