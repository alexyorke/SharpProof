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
        Case("CompactPoint", "FB4D005773B524F6DDD1214D8C024EFF0B43C540EFFC31ED845BC519BB886ED2", "--position", "0", "--compact-json"),
        Case("CompactLine", "C04CB1A4D973263A3FFDA995938F1B42CE655A618E1CDA3DDCF5B450D97D80AC", "--line", "1", "--line-invariants", "--compact-json"),
        Case("CompactSpan", "C960C87EEE593CEABFF452E1AEE5FF74F27071B3B76CFE54A41DE4FE6DE74FE3", "--span-start", "0", "--span-end", Source.Length.ToString(), "--compact-json"),
        Case("CompactFile", "C3BDC3B8FB192D1C3BAFA890B918B8815010F1EC0B70BC87E833604524554FEF", "--all-lines", "--compact-json"),
        Case("InvariantPoint", "FB4D005773B524F6DDD1214D8C024EFF0B43C540EFFC31ED845BC519BB886ED2", "--position", "0", "--compact-json"),
        Case("InvariantLine", "C04CB1A4D973263A3FFDA995938F1B42CE655A618E1CDA3DDCF5B450D97D80AC", "--line", "1", "--line-invariants", "--compact-json"),
        Case("InvariantSpan", "C960C87EEE593CEABFF452E1AEE5FF74F27071B3B76CFE54A41DE4FE6DE74FE3", "--span-start", "0", "--span-end", Source.Length.ToString(), "--compact-json"),
        Case("InvariantFile", "C3BDC3B8FB192D1C3BAFA890B918B8815010F1EC0B70BC87E833604524554FEF", "--all-lines", "--compact-json"),
        Case("CompactFilteredTruncated", "48DDD5391C0A7F065DE1EF358B5DA71D42E6369E85D9A9812AF88A9830A684BC", "--line", "1", "--line-invariants", "--compact-json", "--invariant-target", "value", "--max-facts", "0", "--max-conditions", "0", "--max-proofs", "0"),
        Case("InvariantFilteredTruncated", "48DDD5391C0A7F065DE1EF358B5DA71D42E6369E85D9A9812AF88A9830A684BC", "--line", "1", "--line-invariants", "--compact-json", "--invariant-target", "value", "--max-facts", "0", "--max-conditions", "0", "--max-proofs", "0"),
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
