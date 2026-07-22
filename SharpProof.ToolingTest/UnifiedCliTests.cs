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
                Assert.That(document.RootElement.TryGetProperty("purity", out _), Is.False);
                Assert.That(document.RootElement.TryGetProperty("allocationFree", out _), Is.False);
                Assert.That(document.RootElement.TryGetProperty("doesNotThrow", out _), Is.False);
                Assert.That(document.RootElement.TryGetProperty("evidence", out _), Is.False);
                Assert.That(document.RootElement.TryGetProperty("budget", out _), Is.False);
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
    public async Task Analyze_FailOnDisprovenChecksRequestedProofFact() {
        var file = CreateSource("public static class C { public static int M(int value) => value; }");
        try {
            var result = await SymbolicCliTestHost.RunAsync(
                "analyze", "--file", file, "--target", "line:1:60", "--facets", "proofs",
                "--condition", "false", "--fail-on-disproven");
            Assert.Multiple(() => {
                Assert.That(result.StandardOutput, Does.Contain("ProvenFalse"));
                Assert.That(result.ExitCode, Is.EqualTo(5));
            });
        }
        finally { File.Delete(file); }
    }
    [Test]
    public async Task Analyze_AllLines_AggregatesMethodEffects() {
        var file = CreateSource("""
            public static class C {
                static int[] Allocate(int x) => [x];
                static int state;
                static void Mutate() => state++;
            }
            """);
        try {
            var result = await SymbolicCliTestHost.RunAsync(
                "analyze", "--file", file, "--target", "all-lines", "--facets", "effects", "--format", "text");
            Assert.Multiple(() => {
                Assert.That(result.ExitCode, Is.EqualTo(0));
                Assert.That(result.StandardOutput, Does.Contain("Purity: Disproven"));
                Assert.That(result.StandardOutput, Does.Contain("Allocation-free: Disproven"));
            });
        }
        finally { File.Delete(file); }
    }
    [Test]
    public async Task Analyze_RejectsLegacyModes() {
        var result = await SymbolicCliTestHost.RunAsync("--capabilities", "file.cs");
        Assert.That(result.ExitCode, Is.EqualTo(2));
    }
    [Test]
    public async Task CliHelpAndSmtLifecycleDocumentationStayConsistent() {
        var result = await SymbolicCliTestHost.RunAsync("--help");
        var documentation = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "smt-lifecycle.md"));
        var options = new[] {
            "--file", "--target", "--facets", "--condition", "--format",
            "--fail-on-unknown", "--fail-on-disproven"
        };
        Assert.Multiple(() => {
            Assert.That(result.ExitCode, Is.EqualTo(0));
            foreach (var option in options) {
                Assert.That(result.StandardOutput, Does.Contain(option), "CLI help omitted " + option);
                Assert.That(documentation, Does.Contain(option), "SMT lifecycle documentation omitted " + option);
            }
            Assert.That(documentation, Does.Not.Contain("--smt-transient-retries"));
            Assert.That(documentation, Does.Not.Contain("SmtAnalysisService.Health"));
        });
    }
    private static string CreateSource(string source) {
        var path = Path.Combine(Path.GetTempPath(), "SharpProofCli-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(path, source);
        return path;
    }
    private static string FindRepositoryRoot() {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
