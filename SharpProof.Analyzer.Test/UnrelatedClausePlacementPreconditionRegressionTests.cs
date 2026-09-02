using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class UnrelatedClausePlacementPreconditionRegressionTests
{
    [Test]
    public async Task InvalidNonrequiresClausesDoNotHideCallSiteViolations()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Subject {
                private static void InvalidEnsures(int value) {
                    Contract.Requires(value > 0);
                    if (value > 0) Contract.Ensures(true);
                }

                private static void InvalidAssume(int value) {
                    Contract.Requires(value > 0);
                    value++;
                    Contract.Assume(true);
                }

                public static void Call() {
                    InvalidEnsures(-1);
                    InvalidAssume(-2);
                }
            }
            """,
            "contracts",
            ["SP0027"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", "SP0027");
    }
}
