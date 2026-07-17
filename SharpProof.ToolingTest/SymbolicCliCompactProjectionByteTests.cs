using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCliCompactProjectionByteTests
{
    private const string Source =
        "public class C { public int M(int value) { if (value > 0) return value; return 0; } }";

    private static readonly TestCaseData[] ProjectionCases =
    {
        Case("CompactPoint", "B687B3CFC9931A51CE8FE3C22F9DCBB2F526F1B36A51296B52787BA5DFF0B343", "--position", "0", "--compact-json"),
        Case("CompactLine", "B19B887580836BE818A73CD935DBB64918F6FCE8BB1216B1BA576E1920914573", "--line", "1", "--line-invariants", "--compact-json"),
        Case("CompactSpan", "BFD78184C00D8CBE4F297003319A890612167856BC0F9329FE185AB7D6A941B9", "--span-start", "0", "--span-end", Source.Length.ToString(), "--compact-json"),
        Case("CompactFile", "F4031832C1BD136A7C0A18EE0C4F4455EBA21B1CBD3998F6F4F6CDCA6B31E477", "--all-lines", "--compact-json"),
        Case("InvariantPoint", "B687B3CFC9931A51CE8FE3C22F9DCBB2F526F1B36A51296B52787BA5DFF0B343", "--position", "0", "--compact-json"),
        Case("InvariantLine", "B19B887580836BE818A73CD935DBB64918F6FCE8BB1216B1BA576E1920914573", "--line", "1", "--line-invariants", "--compact-json"),
        Case("InvariantSpan", "BFD78184C00D8CBE4F297003319A890612167856BC0F9329FE185AB7D6A941B9", "--span-start", "0", "--span-end", Source.Length.ToString(), "--compact-json"),
        Case("InvariantFile", "F4031832C1BD136A7C0A18EE0C4F4455EBA21B1CBD3998F6F4F6CDCA6B31E477", "--all-lines", "--compact-json"),
        Case("CompactFilteredTruncated", "86403C72342743AE867226D3B1B5A92E49CF39F516082A1D62223C7E031491EF", "--line", "1", "--line-invariants", "--compact-json", "--invariant-target", "value", "--max-facts", "0", "--max-conditions", "0", "--max-proofs", "0"),
        Case("InvariantFilteredTruncated", "86403C72342743AE867226D3B1B5A92E49CF39F516082A1D62223C7E031491EF", "--line", "1", "--line-invariants", "--compact-json", "--invariant-target", "value", "--max-facts", "0", "--max-conditions", "0", "--max-proofs", "0"),
        Case("RuntimeHazards", "DE93A095DD1144104C4946BE7D76FF84A7FDF96DDE9279DC1D095D0BC8D54ED7", "--line", "1", "--runtime-hazards", "--compact-json"),
        Case("Capabilities", "939F7007DEC14F73F3309FECBECEE3F5A68A51D1278753F6225D89D2AB829117", "--line", "1", "--capabilities", "--compact-json"),
        Case("Complexity", "68B4002D6B3F9DF85529332209996373981678AD6C5D7A24AD94B11CA1889E21", "--line", "1", "--complexity", "--compact-json")
    };

    [TestCaseSource(nameof(ProjectionCases))]
    public async Task CompactProjection_PreservesNormalizedBytes(
        string expectedSha256,
        string[] arguments)
    {
        var result = await SymbolicCliTestHost.RunAsync(
            new[]
            {
                "--source-text", Source,
                "--source-file-name", "CompactProjection.cs"
            }.Concat(arguments).ToArray());

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        var normalized = result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        Assert.That(hash, Is.EqualTo(expectedSha256), "Output length: " + normalized.Length);
    }

    private static TestCaseData Case(string name, string expectedSha256, params string[] arguments)
    {
        return new TestCaseData(expectedSha256, arguments)
            .SetName("CompactProjection_" + name + "_PreservesNormalizedBytes");
    }
}
