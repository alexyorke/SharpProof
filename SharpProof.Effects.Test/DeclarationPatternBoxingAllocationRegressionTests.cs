namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class DeclarationPatternBoxingAllocationRegressionTests
{
    [Test]
    public void ValueTypeDeclarationPatternsCountBoxingAllocation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public interface IMarker { }
            public struct Marked : IMarker { public int V; }

            public static class Sample {
                public static bool ObjectPattern(int value) =>
                    value is object boxed && boxed is not null;

                public static bool InterfacePattern(int value) =>
                    value is IComparable boxed && boxed is not null;

                public static bool StructInterfacePattern(Marked value) =>
                    value is IMarker boxed && boxed is not null;

                public static int SwitchStatement(Marked value) {
                    switch (value) {
                        case IMarker boxed when boxed is not null:
                            return 1;
                        default:
                            return 0;
                    }
                }

                public static int SwitchExpression(Marked value) =>
                    value switch {
                        IMarker boxed when boxed is not null => 1,
                        _ => 0
                    };

                public static bool NullablePattern(int? value) =>
                    value is IComparable boxed && boxed is not null;

                public static object? ConditionalPattern(Marked value) =>
                    value is IMarker boxed ? boxed : null;

                public static bool RecursivePattern(Marked value) =>
                    value is IMarker { };

                public static bool GenericPattern<T>(T value)
                    where T : struct => value is IComparable boxed;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "ObjectPattern", "InterfacePattern", "StructInterfacePattern",
                     "SwitchStatement", "SwitchExpression", "ConditionalPattern",
                     "RecursivePattern", "GenericPattern"
                 })
        {
            var result = session.Analyze(
                EffectTestHost.SampleMethod(compilation, methodName));

            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed),
                methodName);
            Assert.That(result.Projection.IsComplete, Is.True, methodName);
        }

        var nullable = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "NullablePattern"));
        Assert.That(
            nullable.Summary.Allocation,
            Is.Not.EqualTo(EffectAllocationKind.None));
    }
}
