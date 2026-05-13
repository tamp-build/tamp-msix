using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tamp;
using Tamp.Msix;
using Xunit;

namespace Tamp.Msix.Tests;

public sealed class MsixTests
{
    private static Tool FakeMakeAppx() => new(AbsolutePath.Create("/fake/makeappx"));
    private static Tool FakeSignTool() => new(AbsolutePath.Create("/fake/signtool"));

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- makeappx pack ----

    [Fact]
    public void Pack_From_Directory()
    {
        var plan = Msix.Pack(FakeMakeAppx(), s => s
            .SetSourceDirectory("msix-package")
            .SetOutputPackage("artifacts/DasBook_1.0.6.0_x64.msix"));
        Assert.Equal("pack", plan.Arguments[0]);
        Assert.Equal("msix-package", plan.Arguments[IndexOf(plan.Arguments, "/d") + 1]);
        Assert.Equal("artifacts/DasBook_1.0.6.0_x64.msix", plan.Arguments[IndexOf(plan.Arguments, "/p") + 1]);
        Assert.Contains("/o", plan.Arguments);  // overwrite default-on
    }

    [Fact]
    public void Pack_From_Mapping_File()
    {
        var plan = Msix.Pack(FakeMakeAppx(), s => s
            .SetMappingFile("staging.txt")
            .SetOutputPackage("out.msix"));
        Assert.Equal("staging.txt", plan.Arguments[IndexOf(plan.Arguments, "/f") + 1]);
    }

    [Fact]
    public void Pack_Source_And_Mapping_Are_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Pack(FakeMakeAppx(), s => s
                .SetSourceDirectory("x").SetMappingFile("y").SetOutputPackage("z.msix"))
                .Arguments.ToList());
    }

    [Fact]
    public void Pack_Requires_Output()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Pack(FakeMakeAppx(), s => s.SetSourceDirectory("x")).Arguments.ToList());
    }

    [Fact]
    public void Pack_Requires_Source_Or_Mapping()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Pack(FakeMakeAppx(), s => s.SetOutputPackage("z.msix")).Arguments.ToList());
    }

    [Fact]
    public void Pack_No_Validation_Flag()
    {
        var plan = Msix.Pack(FakeMakeAppx(), s => s
            .SetSourceDirectory("d")
            .SetOutputPackage("p.msix")
            .SetValidationLevel("none"));
        Assert.Contains("/nv", plan.Arguments);
    }

    [Fact]
    public void Pack_Overwrite_Can_Be_Disabled()
    {
        var plan = Msix.Pack(FakeMakeAppx(), s => s
            .SetSourceDirectory("d").SetOutputPackage("p.msix").SetOverwrite(false));
        Assert.DoesNotContain("/o", plan.Arguments);
    }

    // ---- makeappx unpack / bundle ----

    [Fact]
    public void Unpack_Builds_Command()
    {
        var plan = Msix.Unpack(FakeMakeAppx(), s => s
            .SetPackage("DasBook.msix")
            .SetOutputDirectory("extracted/"));
        Assert.Equal("unpack", plan.Arguments[0]);
        Assert.Equal("DasBook.msix", plan.Arguments[IndexOf(plan.Arguments, "/p") + 1]);
        Assert.Equal("extracted/", plan.Arguments[IndexOf(plan.Arguments, "/d") + 1]);
    }

    [Fact]
    public void Bundle_With_Version()
    {
        var plan = Msix.Bundle(FakeMakeAppx(), s => s
            .SetSourceDirectory("pkgs/")
            .SetOutputBundle("out.msixbundle")
            .SetBundleVersion("1.0.6.0"));
        Assert.Equal("bundle", plan.Arguments[0]);
        Assert.Equal("1.0.6.0", plan.Arguments[IndexOf(plan.Arguments, "/bv") + 1]);
    }

    // ---- signtool sign ----

    [Fact]
    public void Sign_With_PFX_File_And_Timestamp()
    {
        var plan = Msix.Sign(FakeSignTool(), s => s
            .AddFile("DasBook.msix")
            .SetCertificateFile("cert.pfx")
            .SetTimestampUrl("http://timestamp.digicert.com"));
        Assert.Equal("sign", plan.Arguments[0]);
        Assert.Equal("cert.pfx", plan.Arguments[IndexOf(plan.Arguments, "/f") + 1]);
        Assert.Equal("http://timestamp.digicert.com", plan.Arguments[IndexOf(plan.Arguments, "/tr") + 1]);
        Assert.Equal("sha256", plan.Arguments[IndexOf(plan.Arguments, "/td") + 1]);
        Assert.Equal("sha256", plan.Arguments[IndexOf(plan.Arguments, "/fd") + 1]);
        Assert.Contains("DasBook.msix", plan.Arguments);
    }

    [Fact]
    public void Sign_With_Thumbprint()
    {
        var plan = Msix.Sign(FakeSignTool(), s => s
            .AddFile("x.msix")
            .SetSha1Thumbprint("ABCDEF1234567890"));
        Assert.Equal("ABCDEF1234567890", plan.Arguments[IndexOf(plan.Arguments, "/sha1") + 1]);
    }

    [Fact]
    public void Sign_Requires_At_Least_One_File()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Sign(FakeSignTool(), s => s.SetCertificateFile("c.pfx")).Arguments.ToList());
    }

    [Fact]
    public void Sign_Requires_A_Certificate_Selector()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Sign(FakeSignTool(), s => s.AddFile("x.msix")).Arguments.ToList());
    }

    [Fact]
    public void Sign_File_And_Thumbprint_Are_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Sign(FakeSignTool(), s => s
                .AddFile("x.msix").SetCertificateFile("c.pfx").SetSha1Thumbprint("ABC"))
                .Arguments.ToList());
    }

    // ─── TAM-191 — password-protected PFX (/p) ───────────────────────────

    [Fact]
    public void Sign_PFX_With_Password_Emits_P_Flag()
    {
        var pwd = new Secret("dasbook-pfx-pwd", "s3cret-pfx-pwd");
        var plan = Msix.Sign(FakeSignTool(), s => s
            .AddFile("DasBook.msix")
            .SetCertificateFile("cert.pfx")
            .SetPassword(pwd));
        Assert.Equal("cert.pfx", plan.Arguments[IndexOf(plan.Arguments, "/f") + 1]);
        Assert.Equal("s3cret-pfx-pwd", plan.Arguments[IndexOf(plan.Arguments, "/p") + 1]);
        // The Secret flows through CommandPlan so Tamp's runner masks the value in printed traces.
        Assert.Contains(pwd, plan.Secrets);
    }

    [Fact]
    public void Sign_Password_Without_CertificateFile_Throws()
    {
        // /p only applies to /f. Store-resident cert paths (/sha1, /n) have no password slot.
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Sign(FakeSignTool(), s => s
                .AddFile("x.msix")
                .SetSha1Thumbprint("ABCDEF")
                .SetPassword(new Secret("p", "pwd"))).Arguments.ToList());
    }

    [Fact]
    public void Sign_Password_With_SubjectName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Msix.Sign(FakeSignTool(), s => s
                .AddFile("x.msix")
                .SetSubjectName("CN=DasBook")
                .SetPassword(new Secret("p", "pwd"))).Arguments.ToList());
    }

    [Fact]
    public void Sign_PFX_Without_Password_Still_Works()
    {
        // Unencrypted PFX should continue working — /p is opt-in.
        var plan = Msix.Sign(FakeSignTool(), s => s
            .AddFile("DasBook.msix")
            .SetCertificateFile("cert.pfx"));
        Assert.DoesNotContain("/p", plan.Arguments);
        Assert.Empty(plan.Secrets);
    }

    [Fact]
    public void Sign_PFX_With_Password_And_Timestamp_Combined()
    {
        var pwd = new Secret("pfx-pwd", "supersecret");
        var plan = Msix.Sign(FakeSignTool(), s => s
            .AddFile("DasBook.msix")
            .SetCertificateFile("cert.pfx")
            .SetPassword(pwd)
            .SetTimestampUrl("http://timestamp.digicert.com"));
        Assert.Equal("supersecret", plan.Arguments[IndexOf(plan.Arguments, "/p") + 1]);
        Assert.Equal("http://timestamp.digicert.com",
            plan.Arguments[IndexOf(plan.Arguments, "/tr") + 1]);
    }

    // ---- signtool verify ----

    [Fact]
    public void Verify_Builds_Command()
    {
        var plan = Msix.Verify(FakeSignTool(), s => s.AddFile("DasBook.msix").SetVerifyAll());
        Assert.Equal("verify", plan.Arguments[0]);
        Assert.Contains("/pa", plan.Arguments);   // default authentication policy default-on
        Assert.Contains("/all", plan.Arguments);
        Assert.Contains("DasBook.msix", plan.Arguments);
    }

    // ---- AppxManifest version helpers ----

    [Fact]
    public void NormalizeMsixVersion_3_Part_Gets_Suffix()
    {
        Assert.Equal("1.0.6.0", Msix.NormalizeMsixVersion("1.0.6"));
    }

    [Fact]
    public void NormalizeMsixVersion_4_Part_Passthrough()
    {
        Assert.Equal("1.0.6.42", Msix.NormalizeMsixVersion("1.0.6.42"));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1")]
    [InlineData("1.0.6.7.8")]
    [InlineData("")]
    [InlineData("v1.0.6")]
    [InlineData("1.0.beta.0")]
    public void NormalizeMsixVersion_Rejects_Invalid(string version)
    {
        Assert.Throws<ArgumentException>(() => Msix.NormalizeMsixVersion(version));
    }

    [Fact]
    public void GetSetAppxManifestVersion_RoundTrips()
    {
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="DasBook" Publisher="CN=test" Version="1.0.4.0" />
              <Properties>
                <DisplayName>DasBook</DisplayName>
              </Properties>
            </Package>
            """;
        var tmp = Path.Combine(Path.GetTempPath(), $"appxmanifest-test-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp, manifestXml);
        try
        {
            var path = AbsolutePath.Create(tmp);
            Assert.Equal("1.0.4.0", Msix.GetAppxManifestVersion(path));

            Msix.SetAppxManifestVersion(path, "1.0.6");
            Assert.Equal("1.0.6.0", Msix.GetAppxManifestVersion(path));

            Msix.SetAppxManifestVersion(path, "1.2.3.4");
            Assert.Equal("1.2.3.4", Msix.GetAppxManifestVersion(path));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void GetAppxManifestVersion_Returns_Null_When_File_Missing()
    {
        var path = AbsolutePath.Create(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.xml"));
        Assert.Null(Msix.GetAppxManifestVersion(path));
    }

    [Fact]
    public void SetAppxManifestVersion_Throws_On_Missing_File()
    {
        var path = AbsolutePath.Create(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.xml"));
        Assert.Throws<FileNotFoundException>(() => Msix.SetAppxManifestVersion(path, "1.0.0"));
    }

    [Fact]
    public void SetAppxManifestVersion_Throws_When_No_Identity_Element()
    {
        var brokenXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <NotIdentity />
            </Package>
            """;
        var tmp = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp, brokenXml);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                Msix.SetAppxManifestVersion(AbsolutePath.Create(tmp), "1.0.0"));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ---- Raw escape hatch ----

    [Fact]
    public void SignToolRaw_Allows_Arbitrary_Args()
    {
        var plan = Msix.SignToolRaw(FakeSignTool(), "timestamp", "/tr", "http://x", "out.msix");
        Assert.Equal(new[] { "timestamp", "/tr", "http://x", "out.msix" }, plan.Arguments);
    }
}
