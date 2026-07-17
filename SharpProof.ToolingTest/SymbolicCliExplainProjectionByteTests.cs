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
        Case("Json", "25B7F8686ADFA0C86EA46B4F73B48BAC0BDEF79009228EDDEC723AFD13F806C3", "--json"),
        Case("Sarif", "8AED839C1A35376536C73E16444A3ACF655B2D58E19859F50E1139D8D53E7823", "--sarif")
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
