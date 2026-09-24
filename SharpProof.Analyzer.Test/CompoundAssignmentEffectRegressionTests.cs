using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompoundAssignmentEffectRegressionTests
{
    [Test]
    public async Task ConditionalCompoundAssignmentCannotProveThrowingMethodsSafe()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture
            {
                [DoesNotThrow]
                public static int ArrayIndexAfterConditionalAdd()
                {
                    int[] values = new int[3];
                    int index = 0;
                    index += index > -1 ? 7 : 0;
                    return values[index];
                }

                [DoesNotThrow]
                public static int DivideAfterConditionalSubtract()
                {
                    int divisor = 1;
                    divisor -= divisor > 0 ? 1 : 0;
                    return 10 / divisor;
                }

                [DoesNotThrow]
                public static int ArrayIndexAfterBranchingAdd(bool chooseFirst)
                {
                    int[] values = new int[3];
                    int index = 0;
                    index += chooseFirst ? 7 : 8;
                    return values[index];
                }

                [DoesNotThrow]
                public static int ArrayIndexAfterCoalescingAdd(int? value)
                {
                    int[] values = new int[3];
                    int index = 0;
                    index += value ?? 7;
                    return values[index];
                }

                [DoesNotThrow]
                public static int SafeSimpleAssignment()
                {
                    int[] values = new int[3];
                    int index = 0;
                    index = index + (index > -1 ? 1 : 0);
                    return values[index];
                }
            }
            """,
            mode: null,
            ["SP0046"],
            new SharpProofAnalyzer(factory),
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(
                diagnostics,
                "SP0046",
                "SP0046",
                "SP0046",
                "SP0046");
            foreach (var methodName in new[]
                     {
                         "ArrayIndexAfterConditionalAdd",
                         "DivideAfterConditionalSubtract",
                         "ArrayIndexAfterBranchingAdd",
                         "ArrayIndexAfterCoalescingAdd"
                     })
            {
                Assert.That(
                    factory.Outcomes[methodName],
                    Is.Not.EqualTo(AnalyzerSemanticOutcome.Proven),
                    methodName);
            }

            Assert.That(
                factory.Outcomes["SafeSimpleAssignment"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }
}
