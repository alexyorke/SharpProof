using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ManagedFlowExceptionRegionRegressionTests
{
    [Test]
    public async Task ExceptionHandlerAssignmentsCannotProveThrowingMethodsSafe()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class TX
            {
                public static void Boom() => throw new InvalidOperationException();
                public static void Maybe(int value)
                {
                    if (value > 0) throw new InvalidOperationException();
                }
            }
            public static class Fixture
            {
                [DoesNotThrow]
                public static int CatchAssignment()
                {
                    int[] values = new int[3];
                    int index = 0;
                    try { TX.Boom(); }
                    catch (InvalidOperationException) { index = 7; }
                    return values[index];
                }

                [DoesNotThrow]
                public static int FinallyAssignment(bool shouldThrow)
                {
                    int[] values = new int[3];
                    int index = 0;
                    try
                    {
                        if (shouldThrow)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    finally { index = 7; }
                    return values[index];
                }

                [DoesNotThrow]
                public static int PartialTryAssignment(int value)
                {
                    int[] values = new int[3];
                    int index = 0;
                    try { index = 7; TX.Maybe(value); index = 0; }
                    catch (InvalidOperationException) { }
                    return values[index];
                }

                [DoesNotThrow]
                public static int SafeCatchAssignment()
                {
                    int[] values = new int[3];
                    int index = 0;
                    try { TX.Boom(); }
                    catch (InvalidOperationException) { index = 1; }
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
                "SP0046");
            foreach (var methodName in new[]
                     {
                         "CatchAssignment",
                         "FinallyAssignment",
                         "PartialTryAssignment"
                     })
            {
                Assert.That(
                    factory.Outcomes[methodName],
                    Is.Not.EqualTo(AnalyzerSemanticOutcome.Proven),
                    methodName);
            }

            Assert.That(
                factory.Outcomes["SafeCatchAssignment"],
                Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        }
    }
}
