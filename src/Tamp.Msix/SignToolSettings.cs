namespace Tamp.Msix;

/// <summary>
/// Settings for <c>signtool sign</c> — sign an MSIX / EXE / DLL with a code-signing certificate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tool resolution:</b> <c>signtool.exe</c> ships with the Windows SDK at
/// <c>%ProgramFiles(x86)%\Windows Kits\10\bin\&lt;version&gt;\x64\signtool.exe</c>.
/// </para>
/// <para>
/// <b>Authentication:</b> 0.1.0 supports the certificate-file path (<c>/f</c>) without a password
/// and the cert-store thumbprint path (<c>/sha1</c>). Cert-file + password (<c>/p</c>) requires
/// <c>Tamp.Msix</c> on <c>Tamp.Core</c>'s <c>InternalsVisibleTo</c> list to <c>Reveal()</c> the
/// password safely — deferred to 0.2.0 (TAM-191). Adopters needing password-protected cert files
/// today can use <see cref="SignToolRaw"/>.
/// </para>
/// </remarks>
public sealed class SignToolSignSettings
{
    /// <summary>Working directory for the spawned signtool process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Files to sign (positional args at end).</summary>
    public List<string> Files { get; } = new();

    /// <summary>Path to the PFX certificate file (<c>/f</c>). Mutually exclusive with <see cref="Sha1Thumbprint"/>.</summary>
    public string? CertificateFile { get; set; }

    /// <summary>SHA1 thumbprint of a cert in the certificate store (<c>/sha1</c>). Mutually exclusive with <see cref="CertificateFile"/>.</summary>
    public string? Sha1Thumbprint { get; set; }

    /// <summary>Use the best matching cert (<c>/a</c>). Default true.</summary>
    public bool AutoSelect { get; set; } = true;

    /// <summary>Timestamp server URL (<c>/tr</c>) — RFC 3161 timestamp. Recommended for distributable artifacts.</summary>
    public string? TimestampUrl { get; set; }

    /// <summary>Timestamp hash algorithm (<c>/td</c>). Default <c>sha256</c>.</summary>
    public string? TimestampDigestAlgorithm { get; set; }

    /// <summary>File digest algorithm (<c>/fd</c>). Default <c>sha256</c>.</summary>
    public string? FileDigestAlgorithm { get; set; }

    /// <summary>Subject name of the cert to use (<c>/n</c>). Useful for store-based selection without thumbprint.</summary>
    public string? SubjectName { get; set; }

    /// <summary>Verbose output (<c>/v</c>).</summary>
    public bool Verbose { get; set; }

    /// <summary>Quiet output (<c>/q</c>).</summary>
    public bool Quiet { get; set; }

    public SignToolSignSettings AddFile(string path) { Files.Add(path); return this; }
    public SignToolSignSettings AddFiles(params string[] paths) { Files.AddRange(paths); return this; }
    public SignToolSignSettings SetCertificateFile(string path) { CertificateFile = path; return this; }
    public SignToolSignSettings SetSha1Thumbprint(string thumbprint) { Sha1Thumbprint = thumbprint; return this; }
    public SignToolSignSettings SetAutoSelect(bool v = true) { AutoSelect = v; return this; }
    public SignToolSignSettings SetTimestampUrl(string url) { TimestampUrl = url; return this; }
    public SignToolSignSettings SetTimestampDigestAlgorithm(string algo) { TimestampDigestAlgorithm = algo; return this; }
    public SignToolSignSettings SetFileDigestAlgorithm(string algo) { FileDigestAlgorithm = algo; return this; }
    public SignToolSignSettings SetSubjectName(string name) { SubjectName = name; return this; }
    public SignToolSignSettings SetVerbose(bool v = true) { Verbose = v; return this; }
    public SignToolSignSettings SetQuiet(bool v = true) { Quiet = v; return this; }
    public SignToolSignSettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }
    public SignToolSignSettings SetEnvironmentVariable(string name, string value) { EnvironmentVariables[name] = value; return this; }

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        if (Files.Count == 0)
            throw new InvalidOperationException("At least one file is required for signtool sign (set via AddFile / AddFiles).");
        if (string.IsNullOrEmpty(CertificateFile) && string.IsNullOrEmpty(Sha1Thumbprint) && string.IsNullOrEmpty(SubjectName))
            throw new InvalidOperationException(
                "signtool sign needs a certificate selector — set one of: CertificateFile (/f), Sha1Thumbprint (/sha1), or SubjectName (/n).");
        if (!string.IsNullOrEmpty(CertificateFile) && !string.IsNullOrEmpty(Sha1Thumbprint))
            throw new InvalidOperationException("CertificateFile and Sha1Thumbprint are mutually exclusive.");

        var args = new List<string> { "sign" };
        if (AutoSelect) args.Add("/a");
        if (!string.IsNullOrEmpty(CertificateFile)) { args.Add("/f"); args.Add(CertificateFile!); }
        if (!string.IsNullOrEmpty(Sha1Thumbprint)) { args.Add("/sha1"); args.Add(Sha1Thumbprint!); }
        if (!string.IsNullOrEmpty(SubjectName)) { args.Add("/n"); args.Add(SubjectName!); }
        if (!string.IsNullOrEmpty(FileDigestAlgorithm)) { args.Add("/fd"); args.Add(FileDigestAlgorithm!); }
        else { args.Add("/fd"); args.Add("sha256"); }
        if (!string.IsNullOrEmpty(TimestampUrl))
        {
            args.Add("/tr"); args.Add(TimestampUrl!);
            args.Add("/td"); args.Add(TimestampDigestAlgorithm ?? "sha256");
        }
        if (Verbose) args.Add("/v");
        if (Quiet) args.Add("/q");
        foreach (var f in Files) args.Add(f);

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

/// <summary>Settings for <c>signtool verify</c>.</summary>
public sealed class SignToolVerifySettings
{
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Files to verify (positional args).</summary>
    public List<string> Files { get; } = new();

    /// <summary>Use the default authentication verification policy (<c>/pa</c>). Default true.</summary>
    public bool DefaultAuthenticationPolicy { get; set; } = true;

    /// <summary>Verify all signatures in a file (<c>/all</c>).</summary>
    public bool VerifyAll { get; set; }

    /// <summary>Verbose output (<c>/v</c>).</summary>
    public bool Verbose { get; set; }

    public SignToolVerifySettings AddFile(string path) { Files.Add(path); return this; }
    public SignToolVerifySettings AddFiles(params string[] paths) { Files.AddRange(paths); return this; }
    public SignToolVerifySettings SetDefaultAuthenticationPolicy(bool v = true) { DefaultAuthenticationPolicy = v; return this; }
    public SignToolVerifySettings SetVerifyAll(bool v = true) { VerifyAll = v; return this; }
    public SignToolVerifySettings SetVerbose(bool v = true) { Verbose = v; return this; }
    public SignToolVerifySettings SetWorkingDirectory(string? cwd) { WorkingDirectory = cwd; return this; }

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        if (Files.Count == 0)
            throw new InvalidOperationException("At least one file is required for signtool verify.");

        var args = new List<string> { "verify" };
        if (DefaultAuthenticationPolicy) args.Add("/pa");
        if (VerifyAll) args.Add("/all");
        if (Verbose) args.Add("/v");
        foreach (var f in Files) args.Add(f);

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
