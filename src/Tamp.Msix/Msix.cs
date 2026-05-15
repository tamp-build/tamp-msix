using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Tamp.Msix;

/// <summary>Top-level facade for the MSIX toolchain — makeappx + signtool + AppxManifest helpers.</summary>
public static class Msix
{
    /// <summary><c>makeappx pack</c> — pack a directory of staged files into an MSIX/APPX package.</summary>
    public static CommandPlan Pack(Tool makeAppx, Action<MakeAppxPackSettings> configure)
        => Run<MakeAppxPackSettings>(makeAppx, configure);

    /// <summary><c>makeappx unpack</c> — extract a package's contents.</summary>
    public static CommandPlan Unpack(Tool makeAppx, Action<MakeAppxUnpackSettings> configure)
        => Run<MakeAppxUnpackSettings>(makeAppx, configure);

    /// <summary><c>makeappx bundle</c> — produce a .msixbundle / .appxbundle from one or more packages.</summary>
    public static CommandPlan Bundle(Tool makeAppx, Action<MakeAppxBundleSettings> configure)
        => Run<MakeAppxBundleSettings>(makeAppx, configure);

    /// <summary><c>signtool sign</c> — sign an MSIX or other Authenticode-targeted artifact.</summary>
    public static CommandPlan Sign(Tool signTool, Action<SignToolSignSettings> configure)
    {
        if (signTool is null) throw new ArgumentNullException(nameof(signTool));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var s = new SignToolSignSettings();
        configure(s);
        return s.ToCommandPlan(signTool);
    }

    /// <summary><c>signtool verify</c> — verify an Authenticode signature.</summary>
    public static CommandPlan Verify(Tool signTool, Action<SignToolVerifySettings> configure)
    {
        if (signTool is null) throw new ArgumentNullException(nameof(signTool));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var s = new SignToolVerifySettings();
        configure(s);
        return s.ToCommandPlan(signTool);
    }

    /// <summary>Raw escape hatch for verbs not yet typed.</summary>
    public static CommandPlan SignToolRaw(Tool signTool, params string[] arguments)
    {
        if (signTool is null) throw new ArgumentNullException(nameof(signTool));
        if (arguments is null || arguments.Length == 0)
            throw new ArgumentException("Raw requires at least one argument.", nameof(arguments));
        return new CommandPlan
        {
            Executable = signTool.Executable.Value,
            Arguments = arguments.ToList(),
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = signTool.WorkingDirectory,
            Secrets = Array.Empty<Secret>(),
        };
    }

    // ---- Object-init overloads (TAM-161) ----
    // Parallel surface to the fluent verbs above. Both styles produce identical
    // CommandPlans; fluent stays canonical in docs and `tamp init` templates.
    //
    //     Msix.Pack(makeAppx, new() { Directory = stagingDir, Output = msixPath });
    //
    // is equivalent to:
    //
    //     Msix.Pack(makeAppx, s => s.SetDirectory(stagingDir).SetOutput(msixPath));
    public static CommandPlan Pack(Tool makeAppx, MakeAppxPackSettings settings) => PlanMakeAppx(makeAppx, settings);
    public static CommandPlan Unpack(Tool makeAppx, MakeAppxUnpackSettings settings) => PlanMakeAppx(makeAppx, settings);
    public static CommandPlan Bundle(Tool makeAppx, MakeAppxBundleSettings settings) => PlanMakeAppx(makeAppx, settings);

    public static CommandPlan Sign(Tool signTool, SignToolSignSettings settings)
    {
        if (signTool is null) throw new ArgumentNullException(nameof(signTool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(signTool);
    }

    public static CommandPlan Verify(Tool signTool, SignToolVerifySettings settings)
    {
        if (signTool is null) throw new ArgumentNullException(nameof(signTool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(signTool);
    }

    private static CommandPlan Run<T>(Tool tool, Action<T> configure) where T : MakeAppxSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var s = new T();
        configure(s);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan PlanMakeAppx<T>(Tool tool, T settings) where T : MakeAppxSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AppxManifest helpers — make the version-string the build graph's responsibility,
    // not a hand-edited XML attribute that drifts from package.json / Cargo.toml.
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read the <c>Identity/@Version</c> attribute from an <c>AppxManifest.xml</c> file.
    /// Returns null if the attribute or file is missing — caller decides whether that's an error.
    /// </summary>
    public static string? GetAppxManifestVersion(AbsolutePath appxManifestPath)
    {
        if (!File.Exists(appxManifestPath.Value)) return null;
        var doc = XDocument.Load(appxManifestPath.Value);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var identity = doc.Root?.Element(ns + "Identity");
        return identity?.Attribute("Version")?.Value;
    }

    /// <summary>
    /// Set the <c>Identity/@Version</c> attribute on an <c>AppxManifest.xml</c> file. MSIX versions
    /// are 4-part (e.g. <c>1.0.6.0</c>); when a 3-part SemVer like <c>1.0.6</c> is supplied, the
    /// fourth field is appended as <c>.0</c>.
    /// </summary>
    /// <param name="appxManifestPath">Absolute path to the AppxManifest.xml.</param>
    /// <param name="version">Version string. 3 or 4 dotted components.</param>
    /// <exception cref="ArgumentException">If <paramref name="version"/> isn't 3 or 4 dotted numerics.</exception>
    /// <exception cref="FileNotFoundException">If the manifest doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">If the manifest doesn't have an <c>Identity</c> element.</exception>
    public static void SetAppxManifestVersion(AbsolutePath appxManifestPath, string version)
    {
        if (!File.Exists(appxManifestPath.Value))
            throw new FileNotFoundException($"AppxManifest.xml not found at {appxManifestPath.Value}", appxManifestPath.Value);

        var normalized = NormalizeMsixVersion(version);

        var doc = XDocument.Load(appxManifestPath.Value, LoadOptions.PreserveWhitespace);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var identity = doc.Root?.Element(ns + "Identity")
            ?? throw new InvalidOperationException(
                $"AppxManifest.xml at {appxManifestPath.Value} does not contain an Identity element.");

        identity.SetAttributeValue("Version", normalized);
        doc.Save(appxManifestPath.Value);
    }

    /// <summary>
    /// Normalize a 3-part SemVer (<c>1.0.6</c>) to MSIX's 4-part form (<c>1.0.6.0</c>), or pass
    /// through a 4-part value unchanged. Useful for adopter <c>Build.cs</c> code computing the
    /// MSIX filename from a <c>[Parameter] string Version</c>:
    /// <code>
    /// AbsolutePath MsixOut => Artifacts / $"DasBook_{Msix.NormalizeMsixVersion(Version)}_x64.msix";
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentException">If <paramref name="version"/> is empty, has fewer than 3 or more than 4 dotted components, or contains non-numeric components.</exception>
    public static string NormalizeMsixVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("version must not be empty.", nameof(version));
        var parts = version.Split('.');
        if (parts.Length < 3 || parts.Length > 4)
            throw new ArgumentException(
                $"MSIX version must be 3 or 4 dotted numeric components; got '{version}'.", nameof(version));
        foreach (var p in parts)
            if (!int.TryParse(p, out _))
                throw new ArgumentException(
                    $"MSIX version components must be numeric; got '{p}' in '{version}'.", nameof(version));
        return parts.Length == 4 ? version : version + ".0";
    }
}
