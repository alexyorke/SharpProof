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
        Case("CompactPoint", "6AA93468986BDE40458A5BAC125AD071DFD76B99C09A8C06B801A616758DAD58", "--position", "0", "--json"),
        Case("CompactLine", "8B8436E3920E502FC61EDF2662D93937567874DE1D61011CCFEF197D55EDFF0E", "--line", "1", "--line-invariants", "--json"),
        Case("CompactSpan", "51EDFD8FEA52043174C013505C5ED37CEFBFAEDEE0F8950C4F716A6757303595", "--span-start", "0", "--span-end", Source.Length.ToString(), "--json"),
        Case("CompactFile", "A67D81D8A00DAEB32B23D4A5CD43B99C764F9F337F48931CE205F0DB21CE2871", "--all-lines", "--json"),
        Case("InvariantPoint", "6AA93468986BDE40458A5BAC125AD071DFD76B99C09A8C06B801A616758DAD58", "--position", "0", "--json"),
        Case("InvariantLine", "8B8436E3920E502FC61EDF2662D93937567874DE1D61011CCFEF197D55EDFF0E", "--line", "1", "--line-invariants", "--json"),
        Case("InvariantSpan", "51EDFD8FEA52043174C013505C5ED37CEFBFAEDEE0F8950C4F716A6757303595", "--span-start", "0", "--span-end", Source.Length.ToString(), "--json"),
        Case("InvariantFile", "A67D81D8A00DAEB32B23D4A5CD43B99C764F9F337F48931CE205F0DB21CE2871", "--all-lines", "--json"),
        Case("CompactFilteredTruncated", "8B8436E3920E502FC61EDF2662D93937567874DE1D61011CCFEF197D55EDFF0E", "--line", "1", "--line-invariants", "--json"),
        Case("InvariantFilteredTruncated", "8B8436E3920E502FC61EDF2662D93937567874DE1D61011CCFEF197D55EDFF0E", "--line", "1", "--line-invariants", "--json"),
        Case("RuntimeHazards", "D1E88AA9505155477C76B023E271A0A88373ABB11C355A5E931052DD08D52783", "--line", "1", "--runtime-hazards", "--json"),
        Case("Capabilities", "8BEC2EB69B5B8848788D812476B9B8766CDD3A0330F7A56E6423378D6CF9B261", "--line", "1", "--capabilities", "--json"),
        Case("Complexity", "7C3BA965BBB5ED08972DE8068A427D165B0F979B30E59EA3F570C0C32490BF03", "--line", "1", "--complexity", "--json")
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
