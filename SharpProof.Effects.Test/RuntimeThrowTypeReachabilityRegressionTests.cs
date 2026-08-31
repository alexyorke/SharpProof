namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class RuntimeThrowTypeReachabilityRegressionTests
{
    [Test]
    public void ThrowOperandRetainsRuntimeSubtypeAndNullCatchReachability()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void RuntimeSubtype(
                    Exception error,
                    ref int caught) {
                    try {
                        throw error;
                    }
                    catch (InvalidOperationException) {
                        caught++;
                    }
                }

                public static void NullOperand(
                    InvalidOperationException? error,
                    ref int caught) {
                    try {
                        throw error;
                    }
                    catch (NullReferenceException) {
                        caught++;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Analyze("RuntimeSubtype").Writes.Contains(
                    EffectRegionId.Parameter(1)),
                Is.True,
                "a runtime subtype can reach its matching catch");
            Assert.That(
                Analyze("NullOperand").Writes.Contains(
                    EffectRegionId.Parameter(1)),
                Is.True,
                "throwing a null operand reaches NullReferenceException");
        }

        EffectSummary Analyze(string methodName)
        {
            return session.Analyze(
                EffectTestHost.RequireMethod(
                    compilation,
                    "Sample",
                    methodName)).Summary;
        }
    }
}
