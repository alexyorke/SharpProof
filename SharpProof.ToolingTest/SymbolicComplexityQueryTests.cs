using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;

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
}
