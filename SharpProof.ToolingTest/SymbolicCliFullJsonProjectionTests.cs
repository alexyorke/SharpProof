using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCliFullJsonProjectionTests
{
    private const string Source =
        "public class C { public int M(int value) { if (value > 0) return value; return 0; } }";

    private static readonly TestCaseData[] ScopeCases =
    {
        Case("Point", "4E07F26AC2160B33DA83E74CAB4ACE2E78412ED4A62D9F6856930190670B959B", "--position", "0"),
        Case("Line", "7D934FBBECDBF74CDC3CEAB76D1861726885192D27FEE5AD6054A1515F3B1148", "--line", "1", "--line-invariants"),
        Case("Span", "9ED00C588665664CDD63BEF920B688FAC88BB814CC19DD5E04A88E18069D1703", "--span-start", "0", "--span-end", Source.Length.ToString()),
        Case("File", "DFB2501E18443ADEB5CEC91DE60D332677F90A904F2BC6CEC252F6D6A519013F", "--all-lines")
    };

    [TestCaseSource(nameof(ScopeCases))]
    public async Task FullJsonProjection_PreservesNormalizedBytes(
        string expectedSha256,
        string[] targetArguments)
    {
        var result = await SymbolicCliTestHost.RunAsync(
            new[]
            {
                "--source-text", Source,
                "--source-file-name", "FullProjection.cs"
            }.Concat(targetArguments).Append("--json").ToArray());

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        var normalized = result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        Assert.That(hash, Is.EqualTo(expectedSha256), "Output length: " + normalized.Length);
    }

    private static TestCaseData Case(
        string name,
        string expectedSha256,
        params string[] targetArguments)
    {
        return new TestCaseData(expectedSha256, targetArguments)
            .SetName("FullJsonProjection_" + name + "_PreservesNormalizedBytes");
    }
}
