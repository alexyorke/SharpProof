namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class SlicePatternAllocationRegressionTests
{
    [Test]
    public void ArraySlicePatternsHaveConservativeAllocationFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static bool IsDeclaration(int[] values) =>
                    values is [_, .. var rest] && rest.Length > 0;

                public static bool IsNested(int[] values) =>
                    values is [_, .. [1, 2]];

                public static bool IsDiscard(int[] values) =>
                    values is [_, ..];

                public static int SwitchStatementDeclaration(int[] values) {
                    switch (values) {
                        case [_, .. var rest] when rest.Length > 0:
                            return 1;
                        default:
                            return 0;
                    }
                }

                public static int SwitchStatementNested(int[] values) {
                    switch (values) {
                        case [_, .. [1, 2]]:
                            return 1;
                        default:
                            return 0;
                    }
                }

                public static int SwitchStatementDiscard(int[] values) {
                    switch (values) {
                        case [_, ..]:
                            return 1;
                        default:
                            return 0;
                    }
                }

                public static int SwitchExpressionDeclaration(int[] values) =>
                    values switch {
                        [_, .. var rest] when rest.Length > 0 => 1,
                        _ => 0
                    };

                public static int SwitchExpressionNested(int[] values) =>
                    values switch {
                        [_, .. [1, 2]] => 1,
                        _ => 0
                    };

                public static int SwitchExpressionDiscard(int[] values) =>
                    values switch {
                        [_, ..] => 1,
                        _ => 0
                    };
            }
            """);

        foreach (var (methodName, expectedAllocation) in new[]
        {
            ("IsDeclaration", true),
            ("IsNested", true),
            ("IsDiscard", false),
            ("SwitchStatementDeclaration", true),
            ("SwitchStatementNested", true),
            ("SwitchStatementDiscard", false),
            ("SwitchExpressionDeclaration", true),
            ("SwitchExpressionNested", true),
            ("SwitchExpressionDiscard", false)
        })
        {
            var result = EffectTestHost.AnalyzeSample(compilation, methodName);

            Assert.That(
                result.Summary.Allocation != EffectAllocationKind.None,
                Is.EqualTo(expectedAllocation),
                methodName);
            if (expectedAllocation)
            {
                Assert.That(
                    result.Projection.Effects.HasFlag(SharpProofEffect.Allocates) ||
                    !result.Projection.IsComplete,
                    Is.True,
                    methodName);
            }
            else
            {
                Assert.That(
                    result.Projection.Effects & SharpProofEffect.Allocates,
                    Is.EqualTo(SharpProofEffect.None),
                    methodName);
                Assert.That(result.Projection.IsComplete, Is.True, methodName);
            }
        }
    }
}
