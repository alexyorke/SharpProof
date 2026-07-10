using System.Text.Json;
using NUnit.Framework;

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
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "StandaloneProfile-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
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
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("point"));
            Assert.That(root.GetProperty("methodName").GetString(), Does.Contain("Read"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
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

        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "InvalidStandaloneProfile-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, "public static class C { public static int M() => 1; }\n");
        try
        {
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
        finally
        {
            File.Delete(sourcePath);
        }
    }

    private static int FindLine(string source, string marker)
    {
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found in source.");

        var line = 1;
        for (var index = 0; index < position; index++)
            if (source[index] == '\n')
                line++;

        return line;
    }
}
