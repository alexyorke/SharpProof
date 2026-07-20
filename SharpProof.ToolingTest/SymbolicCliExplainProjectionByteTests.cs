using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCliExplainProjectionByteTests
{
    private const string Source = """
                                  public static class C
                                  {
                                      public static int M(int value, int divisor)
                                      {
                                          if (divisor == 0) return 10 / divisor;
                                          return value;
                                      }
                                  }
                                  """;

    private static readonly TestCaseData[] FormatCases =
    {
        Case("Json", "63A8CAC681E688804C253D3A88B23AAB602C59BEF10A8905A9001E75259F5E44", "--json"),
        Case("Sarif", "88FB26CEA0E5D6731641F7B87240D13CF1D0DF67436C1B2B8DAEDA72021AC348", "--sarif")
    };

    [TestCaseSource(nameof(FormatCases))]
    public async Task ExplainProjection_PreservesNormalizedBytes(
        string expectedSha256,
        string formatArgument)
    {
        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "explain",
            "--source-text", Source.Replace("\r\n", "\n", StringComparison.Ordinal),
            "--source-file-name", "ExplainProjection.cs",
            "--line", "5",
            "--column", "20",
            formatArgument);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        var normalized = result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        Assert.That(hash, Is.EqualTo(expectedSha256), "Output length: " + normalized.Length);
    }

    private static TestCaseData Case(string name, string expectedSha256, string formatArgument)
    {
        return new TestCaseData(expectedSha256, formatArgument)
            .SetName("ExplainProjection_" + name + "_PreservesNormalizedBytes");
    }
}
