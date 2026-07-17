using System.Text.Json;
using NUnit.Framework;
using static SharpProof.Test.SourceMarker;

namespace SharpProof.Test;

[TestFixture]
public sealed class StandaloneCompilationProfileTests
{
    [Test]
    public async Task SymbolicCli_CompilationProfile_AppliesEverySetting()
    {
        const string source = """
                              #if PROFILE
                              #nullable enable

                              /// <summary>Profiled unsafe source.</summary>
                              public unsafe static class Profiled
                              {
                                  public static int Read(int* value) => *value;
                              }
                              #endif
                              """;
        using var sourceFile = TemporarySourceFile.Create("StandaloneProfile-", source);
        var sourcePath = sourceFile.Path;

        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "public static int Read").ToString(),
            "--language-version",
            "12",
            "--define",
            "PROFILE",
            "--nullable",
            "enable",
            "--allow-unsafe",
            "--documentation-mode",
            "diagnose",
            "--platform",
            "x64",
            "--optimization",
            "release",
            "--assembly-name",
            "Profiled.Query",
            "--compact-json");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("point"));
        Assert.That(root.GetProperty("programPoints")[0].GetProperty("methodName").GetString(),
            Does.Contain("Read"));
    }

    [Test]
    public async Task SymbolicCli_CompilationProfileHelpAndValidationAreExplicit()
    {
        var help = await SymbolicCliTestHost.RunOutOfProcessAsync("--help");
        Assert.That(help.ExitCode, Is.EqualTo(0));
        foreach (var option in new[]
                 {
                     "--language-version",
                     "--define",
                     "--nullable",
                     "--allow-unsafe",
                     "--documentation-mode",
                     "--platform",
                     "--optimization",
                     "--assembly-name"
                 })
            Assert.That(help.StandardError, Does.Contain(option));

        using var sourceFile = TemporarySourceFile.Create(
            "InvalidStandaloneProfile-",
            "public static class C { public static int M() => 1; }\n");
        var sourcePath = sourceFile.Path;

        var invalid = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "--file",
            sourcePath,
            "--line",
            "1",
            "--nullable",
            "sometimes");

        Assert.That(invalid.ExitCode, Is.EqualTo(64));
        Assert.That(invalid.StandardError,
            Does.Contain("--nullable must be disable, enable, warnings, or annotations"));
    }

}
