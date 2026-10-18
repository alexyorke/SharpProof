using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ConditionalAssignmentRequiresRegressionTests
{
    [Test]
    public async Task ExistingLocalConditionalAssignmentsReplaceTheOldManagedFlowValue()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static void Positive(long value) {
                    Contract.Requires(1 <= value);
                }

                private static void Required(bool value) {
                    Contract.Requires(value);
                }

                public static void ConstantArm() {
                    long value = 0;
                    value = true ? 1 : value;
                    Positive(value);
                }

                public static void ConditionalJoin(bool condition) {
                    long value = 0;
                    value = condition ? 1 : 2;
                    Positive(value);
                }

                public static void ConditionalJoinThenRead(bool condition) {
                    long value = 0;
                    value = condition ? 1 : 2;
                    value = value + 0;
                    Positive(value);
                }

                public static void NestedConditional(bool condition) {
                    long value = 0;
                    value = (condition ? 1 : 2) + 0;
                    Positive(value);
                }

                public static void BooleanOr(bool condition) {
                    bool value = false;
                    value = condition || true;
                    Required(value);
                }

                public static void BooleanConditional(bool condition) {
                    bool value = false;
                    value = condition ? true : true;
                    Required(value);
                }

                public static void Violation() => Positive(0);
            }
            """,
            "contracts",
            []);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SP0027"));
        Assert.That(
            diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("Positive"));
    }
}
