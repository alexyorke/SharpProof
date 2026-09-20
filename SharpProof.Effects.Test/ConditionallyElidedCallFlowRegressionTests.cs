using Microsoft.CodeAnalysis;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConditionallyElidedCallFlowRegressionTests
{
    [Test]
    public void ElidedThrowArgumentCannotRefineFollowingDivision()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Diagnostics;
            public static class Sample {
                [Conditional("TRACE")] public static void Log(int value) { }
                public static int Scale(int count) {
                    Log(count > 0 ? count : throw new ArgumentOutOfRangeException());
                    return 100 / count;
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Scale");
        var result = new EffectAnalysisSession(compilation).Analyze(method);
        var thrownTypes = result.Summary.Throws.Types
            .Select(static type => type.ToDisplayString())
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrownTypes, Does.Contain("System.DivideByZeroException"));
            Assert.That(thrownTypes, Does.Not.Contain("System.ArgumentOutOfRangeException"));
        }
    }
}
