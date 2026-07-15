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
        Case("Json", "8449F627643F42EC21E64059827274017DAAF9CB0AFEAF8F20C1D12966D43EA1", "--json"),
        Case("Sarif", "A96337EC9535C6F236774E101207E4401C0C70870B3EA6A325CE299A8ED86C67", "--sarif")
    };

    [TestCaseSource(nameof(FormatCases))]
    public async Task ExplainProjection_PreservesNormalizedBytes(
        string expectedSha256,
        string formatArgument)
    {
        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "explain",
            "--source-text", Source,
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
