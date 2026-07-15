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
        Case("Point", "E59A225A764916139B4E3FD728290906651B55AAA08EF39EB55C9937DEE6FF01", "--position", "0"),
        Case("Line", "DEDABD873917551855A0210FEE2A22DCC201CE2B31CB655CB42AA5FF4745B501", "--line", "1", "--line-invariants"),
        Case("Span", "DDC564D1C44225CA8665782EE84EF5D1014CFA7EA5A8924C3ECB70EEB3A9451A", "--span-start", "0", "--span-end", Source.Length.ToString()),
        Case("File", "E0DC6BACCC1120699CF61FAA7E13FA9306F1A9FC83E2D4E01EB9FF746F5DB3CF", "--all-lines")
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
