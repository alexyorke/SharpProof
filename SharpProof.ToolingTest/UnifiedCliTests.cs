using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class UnifiedCliTests {
    [Test]
    public async Task Analyze_Text_ReportsComposableVerdicts() {
        var file = CreateSource("public static class C { public static object M() => new object(); }");
        try {
            var result = await SymbolicCliTestHost.RunAsync(
                "analyze", "--file", file, "--target", "line:1", "--facets", "effects", "--format", "text");
            Assert.Multiple(() => {
                Assert.That(result.ExitCode, Is.EqualTo(0));
                Assert.That(result.StandardOutput, Does.Contain("Purity: Proven"));
                Assert.That(result.StandardOutput, Does.Contain("Allocation-free: Disproven"));
            });
        }
        finally { File.Delete(file); }
    }

    [Test]
    public async Task Analyze_Json_SerializesUnifiedResult() {
        var file = CreateSource("public static class C { public static int M(int x) => 10 / x; }");
        try {
            var result = await SymbolicCliTestHost.RunAsync(
                "analyze", "--file", file, "--target", "line:1", "--facets", "effects,hazards", "--format", "json");
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Multiple(() => {
                Assert.That(result.ExitCode, Is.EqualTo(0));
                Assert.That(document.RootElement.TryGetProperty("methodEffects", out _), Is.True);
                Assert.That(document.RootElement.TryGetProperty("hazards", out _), Is.True);
                Assert.That(document.RootElement.TryGetProperty("unknownReasons", out _), Is.True);
            });
        }
        finally { File.Delete(file); }
    }

    [Test]
    public async Task Analyze_UsesDocumentedExitGates() {
        var file = CreateSource("public static class C { public static object M() => new object(); }");
        try {
            var result = await SymbolicCliTestHost.RunAsync(
                "analyze", "--file", file, "--target", "line:1", "--facets", "effects",
                "--fail-on-disproven");
            Assert.That(result.ExitCode, Is.EqualTo(5));
        }
        finally { File.Delete(file); }
    }

    [Test]
    public async Task Analyze_RejectsLegacyModes() {
        var result = await SymbolicCliTestHost.RunAsync("--capabilities", "file.cs");
        Assert.That(result.ExitCode, Is.EqualTo(2));
    }

    private static string CreateSource(string source) {
        var path = Path.Combine(Path.GetTempPath(), "SharpProofCli-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(path, source);
        return path;
    }
}
