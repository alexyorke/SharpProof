using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class UnrelatedClausePlacementPreconditionRegressionTests
{
    [TestCase("if (value > 0) Contract.Ensures(true);", "Contract.Ensures")]
    [TestCase("value++; Contract.Assume(true);", "Contract.Assume")]
    public async Task MisplacedUnrelatedClauseDoesNotHideCallSiteRequiresDiagnostic(
        string unrelatedClause,
        string clauseName)
    {
        var source = $$"""
            using SharpProof.Attributes;

            public static class Fixture
            {
                private static int RequiresPositive(int value)
                {
                    Contract.Requires(value > 0);
                    {{unrelatedClause}}
                    return value;
                }

                public static int InvalidCall() => RequiresPositive(0);
                public static int ValidCall() => RequiresPositive(1);
            }
            """;

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            "contracts",
            ["SP0024", "SP0027"],
            new SharpProofAnalyzer());

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(["SP0024", "SP0027"]));
        var placement = diagnostics.Single(static diagnostic => diagnostic.Id == "SP0024");
        var call = diagnostics.Single(static diagnostic => diagnostic.Id == "SP0027");
        AnalyzerTestHost.AssertMessageContains(placement, clauseName);
        AnalyzerTestHost.AssertMessageContains(call, "RequiresPositive");
        AnalyzerTestHost.AssertMessageContains(call, "value > 0");

        const string callText = "RequiresPositive(0)";
        var callStart = source.IndexOf(callText, StringComparison.Ordinal);
        Assert.That(callStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(call.Location.SourceSpan.Start, Is.EqualTo(callStart));
        Assert.That(call.Location.SourceSpan.Length, Is.EqualTo(callText.Length));
    }
}
