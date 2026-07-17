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
        Case("Point", "124A3CAD73C068051EF02B5C9C09CA0B7C527FF21469B9704011FF425DF63C53", "--position", "0"),
        Case("Line", "BD9D76CADCCC17DAD8BCEE854D41061585D238A21D4E72B2AF41D2C6E741500E", "--line", "1", "--line-invariants"),
        Case("Span", "F0B3D5728B109C9600155D459796DC8178B3D23DF30CC7BC4CF06920850F3B96", "--span-start", "0", "--span-end", Source.Length.ToString()),
        Case("File", "06BB4DBBCD3869C6A92932F387786C38E4EE4742124BD6D443446B6695FDC422", "--all-lines")
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
