namespace Tamp.Msix;

/// <summary>
/// Common knobs for <c>makeappx.exe</c> verbs. Working dir + env overlay + the universal
/// quiet/overwrite flags makeappx's subcommands share.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tool resolution:</b> <c>makeappx.exe</c> ships with the Windows SDK at
/// <c>%ProgramFiles(x86)%\Windows Kits\10\bin\&lt;version&gt;\x64\makeappx.exe</c>. Adopters
/// typically resolve via <c>[FromPath("makeappx")]</c> after a Windows SDK Build Tools install
/// puts the SDK's bin directory on PATH (or hardcode the absolute path via
/// <c>Tool.FromAbsolutePath</c>).
/// </para>
/// <para>
/// <b>Platform note:</b> The MSIX toolchain is Windows-only. Adopters running Tamp on Linux/macOS
/// can still construct plans (the wrapper has no Windows-specific Tamp dependency), but invocation
/// will fail with the usual "not found" error from the runner.
/// </para>
/// </remarks>
public abstract class MakeAppxSettingsBase
{
    /// <summary>Working directory for the spawned makeappx process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables on top of the inherited environment.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Overwrite the output file if it exists (<c>/o</c>). Default true — re-runs in CI shouldn't fail on stale outputs.</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>Hash algorithm for the package signature (<c>/h</c>). Values: <c>SHA256</c> (default), <c>SHA384</c>, <c>SHA512</c>.</summary>
    public string? HashAlgorithm { get; set; }

    /// <summary>Verbose logging (<c>/v</c>).</summary>
    public bool Verbose { get; set; }

    /// <summary>Subclasses produce the verb token + verb-specific arguments.</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        var args = new List<string>();
        args.AddRange(BuildVerbArguments());
        if (Overwrite) args.Add("/o");
        if (Verbose) args.Add("/v");
        if (!string.IsNullOrEmpty(HashAlgorithm)) { args.Add("/h"); args.Add(HashAlgorithm!); }

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = Array.Empty<Secret>(),
        };
    }
}

/// <summary>Fluent setters for the common knobs.</summary>
public static class MakeAppxSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : MakeAppxSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : MakeAppxSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetOverwrite<T>(this T s, bool v = true) where T : MakeAppxSettingsBase { s.Overwrite = v; return s; }
    public static T SetHashAlgorithm<T>(this T s, string? algo) where T : MakeAppxSettingsBase { s.HashAlgorithm = algo; return s; }
    public static T SetVerbose<T>(this T s, bool v = true) where T : MakeAppxSettingsBase { s.Verbose = v; return s; }
}

/// <summary>Settings for <c>makeappx pack</c> — pack a directory of staged files into an MSIX/APPX package.</summary>
public sealed class MakeAppxPackSettings : MakeAppxSettingsBase
{
    /// <summary>Source directory containing the staged content (AppxManifest.xml at root, plus binaries / assets).</summary>
    public string? SourceDirectory { get; set; }

    /// <summary>Output package path (e.g. <c>artifacts/DasBook_1.0.6_x64.msix</c>).</summary>
    public string? OutputPackage { get; set; }

    /// <summary>Pack from a mapping file (<c>/f</c>) instead of a directory. Mutually exclusive with <see cref="SourceDirectory"/>.</summary>
    public string? MappingFile { get; set; }

    /// <summary>Validation level: <c>full</c>, <c>none</c>. Default <c>full</c>.</summary>
    public string? ValidationLevel { get; set; }

    public MakeAppxPackSettings SetSourceDirectory(string dir) { SourceDirectory = dir; return this; }
    public MakeAppxPackSettings SetOutputPackage(string path) { OutputPackage = path; return this; }
    public MakeAppxPackSettings SetMappingFile(string path) { MappingFile = path; return this; }
    public MakeAppxPackSettings SetValidationLevel(string level) { ValidationLevel = level; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(OutputPackage))
            throw new InvalidOperationException("OutputPackage is required for makeappx pack (set via SetOutputPackage).");
        if (string.IsNullOrEmpty(SourceDirectory) && string.IsNullOrEmpty(MappingFile))
            throw new InvalidOperationException("Either SourceDirectory or MappingFile is required for makeappx pack.");
        if (!string.IsNullOrEmpty(SourceDirectory) && !string.IsNullOrEmpty(MappingFile))
            throw new InvalidOperationException("SourceDirectory and MappingFile are mutually exclusive for makeappx pack.");

        yield return "pack";
        if (!string.IsNullOrEmpty(SourceDirectory)) { yield return "/d"; yield return SourceDirectory!; }
        if (!string.IsNullOrEmpty(MappingFile)) { yield return "/f"; yield return MappingFile!; }
        yield return "/p"; yield return OutputPackage!;
        if (string.Equals(ValidationLevel, "none", StringComparison.OrdinalIgnoreCase)) yield return "/nv";
    }
}

/// <summary>Settings for <c>makeappx unpack</c> — extract a package's contents.</summary>
public sealed class MakeAppxUnpackSettings : MakeAppxSettingsBase
{
    /// <summary>Source package path.</summary>
    public string? Package { get; set; }

    /// <summary>Output directory.</summary>
    public string? OutputDirectory { get; set; }

    public MakeAppxUnpackSettings SetPackage(string path) { Package = path; return this; }
    public MakeAppxUnpackSettings SetOutputDirectory(string path) { OutputDirectory = path; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(Package)) throw new InvalidOperationException("Package required for makeappx unpack.");
        if (string.IsNullOrEmpty(OutputDirectory)) throw new InvalidOperationException("OutputDirectory required for makeappx unpack.");
        yield return "unpack";
        yield return "/p"; yield return Package!;
        yield return "/d"; yield return OutputDirectory!;
    }
}

/// <summary>Settings for <c>makeappx bundle</c> — produce an .msixbundle / .appxbundle from one or more packages.</summary>
public sealed class MakeAppxBundleSettings : MakeAppxSettingsBase
{
    /// <summary>Source directory containing the per-arch .msix packages to bundle.</summary>
    public string? SourceDirectory { get; set; }

    /// <summary>Output bundle path (e.g. <c>artifacts/DasBook_1.0.6.msixbundle</c>).</summary>
    public string? OutputBundle { get; set; }

    /// <summary>Bundle version (<c>/bv</c>). Optional; pulled from packages otherwise.</summary>
    public string? BundleVersion { get; set; }

    public MakeAppxBundleSettings SetSourceDirectory(string dir) { SourceDirectory = dir; return this; }
    public MakeAppxBundleSettings SetOutputBundle(string path) { OutputBundle = path; return this; }
    public MakeAppxBundleSettings SetBundleVersion(string version) { BundleVersion = version; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(SourceDirectory)) throw new InvalidOperationException("SourceDirectory required for makeappx bundle.");
        if (string.IsNullOrEmpty(OutputBundle)) throw new InvalidOperationException("OutputBundle required for makeappx bundle.");
        yield return "bundle";
        yield return "/d"; yield return SourceDirectory!;
        yield return "/p"; yield return OutputBundle!;
        if (!string.IsNullOrEmpty(BundleVersion)) { yield return "/bv"; yield return BundleVersion!; }
    }
}
