using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ManagedFlowExceptionRegionRegressionTests
{

    [TestCase(250)]
    [TestCase(1_000)]
    public async Task DeepNestedTryClaimsAbstainWithoutCrashing(int nestingDepth)
    {
        var body = string.Concat(
                Enumerable.Repeat("try {\n", nestingDepth)) +
            "return 1 / denominator;\n" +
            string.Concat(
                Enumerable.Repeat(
                    "} catch (DivideByZeroException) { return 0; }\n",
                    nestingDepth));
        var source =
            "using System;\n" +
            "using SharpProof.Attributes;\n" +
            "public static class Fixture {\n" +
            "    [DoesNotThrow]\n" +
            "    public static int Deep(int denominator) {\n" +
            body +
            "    }\n" +
            "}\n";

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            "effects",
            ["SP0046", "SP0047"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
    }

    [Test]
    public async Task DeepNestedUsingClaimsStillAbstainWithoutCrashing()
    {
        const int nestingDepth = 250;
        var body = string.Concat(
                Enumerable.Repeat(
                    "using (new FixtureResource()) {\n",
                    nestingDepth)) +
            string.Concat(
                Enumerable.Repeat("}\n", nestingDepth));
        var source =
            "using System;\n" +
            "using SharpProof.Attributes;\n" +
            "public sealed class FixtureResource : IDisposable {\n" +
            "    public void Dispose() { }\n" +
            "}\n" +
            "public static class Fixture {\n" +
            "    [DoesNotThrow]\n" +
            "    public static void Deep() {\n" +
            body +
            "    }\n" +
            "}\n";

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            "effects",
            ["SP0047"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
    }
}
