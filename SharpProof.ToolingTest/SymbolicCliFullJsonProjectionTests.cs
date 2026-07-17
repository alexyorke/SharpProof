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
        Case("Point", "3F46440200A6778C4FFDD0CACCD3D5F7B5D497B08381CD5A3573183913822835", "--position", "0"),
        Case("Line", "4B18D0EEDB3C17E04B462ABF5CC3571DE98D9A12CE6F2BD76C2DFC9D61290C58", "--line", "1", "--line-invariants"),
        Case("Span", "D46ABB4FD4A7A4B83CA55C87D644A072E949DF9AD7C51950535C7F7D5EC55BCC", "--span-start", "0", "--span-end", Source.Length.ToString()),
        Case("File", "47D4BDE599DCDDE6CDF9FF6AC5106171AEA5C6DE7A3107F5F2969E9B23B431FD", "--all-lines")
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
