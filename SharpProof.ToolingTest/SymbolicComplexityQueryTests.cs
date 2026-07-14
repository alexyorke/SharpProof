using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;
using static SharpProof.Test.SourceMarker;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicComplexityQueryTests
{
    [Test]
    public async Task SymbolicCli_ComplexityCompactJson_EmitsEvidenceSchema()
    {
        const string source = """
                              public sealed class TestClass
                              {
                                  private static int Step(int value) => value + 1;

                                  public int Sum(int[] values)
                                  {
                                      var total = 0;
                                      foreach (var value in values) total += value;
                                      return total;
                                  }
                              }
                              """;
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicComplexity-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                sourcePath,
                "--line",
                "3",
                "--complexity",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("complexity"));
            Assert.That(root.GetProperty("evidenceSchemaVersion").GetInt32(),
                Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
            Assert.That(root.GetProperty("evidenceSchemaCompatibility").GetString(),
                Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_Complexity_RejectsInvalidCombinations()
    {
        const string source = """
                              public sealed class TestClass
                              {
                                  public int TestMethod()
                                  {
                                      return 42;
                                  }
                              }
                              """;
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicComplexityInvalid-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                sourcePath,
                "--complexity",
                "--all-lines");

            Assert.That(result.ExitCode, Is.EqualTo(64));
            Assert.That(result.StandardError,
                Does.Contain("--complexity supports --line, --line with --column, or --position only."));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_ComplexityExitGates_DistinguishExceededWithinAndUnknown()
    {
        const string source = """
                              public sealed class TestClass
                              {
                                  public int Quadratic(int count)
                                  {
                                      var total = 0;
                                      for (var left = 0; left < count; left++)
                                      for (var right = 0; right < count; right++) total += left + right;
                                      return total;
                                  }

                                  public int Unknown(int count)
                                  {
                                      var index = 0;
                                      while (index < count) index = Step(index);
                                      return index;
                                  }

                                  public int Product(int n, int m)
                                  {
                                      var total = 0;
                                      for (var i = 0; i < n; i++)
                                      for (var j = 0; j < m; j++) total += i + j;
                                      return total;
                                  }

                                  public int Max(int n, int m)
                                  {
                                      var total = 0;
                                      for (var i = 0; i < n; i++) total += i;
                                      for (var j = 0; j < m; j++) total += j;
                                      return total;
                                  }
                              }
                              """;
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicComplexityGates-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var exceeded = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "for (var left").ToString(),
                "--complexity",
                "--compact-json",
                "--fail-on-complexity-exceeded",
                "Linear");
            Assert.That(exceeded.ExitCode, Is.EqualTo(1));
            Assert.That(exceeded.StandardError, Does.Contain("CI gate failed [complexity-exceeded]"));
            using (JsonDocument.Parse(exceeded.StandardOutput))
            {
            }

            var within = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "for (var left").ToString(),
                "--complexity",
                "--fail-on-complexity-exceeded",
                "Quadratic");
            Assert.That(within.ExitCode, Is.Zero, within.StandardError);

            var productExceedsConstant = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "public int Product").ToString(),
                "--complexity",
                "--fail-on-complexity-exceeded",
                "Constant");
            Assert.That(productExceedsConstant.ExitCode, Is.EqualTo(1));
            Assert.That(productExceedsConstant.StandardError,
                Does.Contain("CI gate failed [complexity-exceeded]"));

            var maxExceedsConstant = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "public int Max").ToString(),
                "--complexity",
                "--fail-on-complexity-exceeded",
                "Constant");
            Assert.That(maxExceedsConstant.ExitCode, Is.EqualTo(1));
            Assert.That(maxExceedsConstant.StandardError,
                Does.Contain("CI gate failed [complexity-exceeded]"));

            var unknown = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "while (index").ToString(),
                "--complexity",
                "--fail-on-complexity-unknown");
            Assert.That(unknown.ExitCode, Is.EqualTo(1));
            Assert.That(unknown.StandardError, Does.Contain("CI gate failed [complexity-unknown]"));

            var threshold = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "for (var left").ToString(),
                "--complexity",
                "--compact-json",
                "--fail-on-compact-threshold",
                "complexity-drivers=0");
            Assert.That(threshold.ExitCode, Is.EqualTo(1));
            Assert.That(threshold.StandardError,
                Does.Contain("CI gate failed [compact-threshold.complexity-drivers]"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

}
