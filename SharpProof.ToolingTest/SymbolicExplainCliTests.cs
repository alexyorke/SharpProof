using NUnit.Framework;

namespace SharpProof.Test;

public sealed class SymbolicExplainCliTests
{
    [Test]
    public async Task SymbolicCli_Explain_ComposesProofSurfacesForLine()
    {
        var source = """
                     using System;

                     public static class Example
                     {
                         public static int Work(int divisor, int n)
                         {
                             Console.WriteLine(n);
                             var sum = 0;
                             for (var i = 0; i < n; i++)
                             {
                                 sum += i;
                             }

                             if (divisor == 0)
                             {
                                 return 10 / divisor;
                             }

                             return sum;
                         }
                     }
                     """;
        var filePath = Path.Combine(
            Path.GetTempPath(),
            "SharpProofExplainCli-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(filePath, source).ConfigureAwait(false);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "explain",
                "--file",
                filePath,
                "--line",
                "16",
                "--column",
                "20",
                "--implies",
                "divisor == 0").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("SharpProof explanation"));
            Assert.That(result.StandardOutput, Does.Contain("Invariant proof"));
            Assert.That(result.StandardOutput, Does.Contain("Reachability:"));
            Assert.That(result.StandardOutput, Does.Contain("Proof outcomes:"));
            Assert.That(result.StandardOutput, Does.Contain("Runtime hazards"));
            Assert.That(result.StandardOutput, Does.Contain("DivideByZero"));
            Assert.That(result.StandardOutput, Does.Contain("Capabilities"));
            Assert.That(result.StandardOutput, Does.Contain("Console"));
            Assert.That(result.StandardOutput, Does.Contain("Complexity"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}