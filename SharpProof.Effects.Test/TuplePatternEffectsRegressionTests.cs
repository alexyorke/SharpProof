using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class TuplePatternEffectsRegressionTests
{
    [Test]
    public void ITuplePositionalPatternFactsUseRuntimeInterfaceMembers()
    {
        var compilation = CreatePatternCompilation();
        var objectPattern = Pattern(compilation, "ObjectDiscardPair");
        var interfacePattern = Pattern(compilation, "ITupleDiscardPair");
        var valueTuplePattern = Pattern(compilation, "ValueTupleDiscardPair");
        var deconstructedPattern = Pattern(compilation, "DeconstructPair");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SwitchExpressionFacts.IsITuplePattern(objectPattern),
                Is.True);
            Assert.That(
                SwitchExpressionFacts.IsITuplePattern(interfacePattern),
                Is.True);
            Assert.That(
                SwitchExpressionFacts.IsITuplePattern(valueTuplePattern),
                Is.False);
            Assert.That(
                SwitchExpressionFacts.IsITuplePattern(deconstructedPattern),
                Is.False);

            Assert.That(
                SwitchExpressionFacts.GetITupleLengthMember(objectPattern)?.Name,
                Is.EqualTo("get_Length"));
            Assert.That(
                SwitchExpressionFacts.GetITupleIndexerMember(objectPattern)?.Name,
                Is.EqualTo("get_Item"));
            Assert.That(
                SwitchExpressionFacts.GetITupleLengthMember(valueTuplePattern),
                Is.Null);
            Assert.That(
                SwitchExpressionFacts.GetITupleIndexerMember(deconstructedPattern),
                Is.Null);

            Assert.That(
                SwitchExpressionFacts.IsTotalPattern(
                    objectPattern,
                    objectPattern.InputType,
                    inputDefinitelyNonNull: true),
                Is.False,
                "ITuple length is a runtime condition");
            Assert.That(
                SwitchExpressionFacts.IsTotalPattern(
                    valueTuplePattern,
                    valueTuplePattern.InputType,
                    inputDefinitelyNonNull: true),
                Is.True);
        }
    }

    [Test]
    public void ITuplePositionalPatternIntroducesUnknownDispatchEffects()
    {
        var result = EffectTestHost.AnalyzeSample(
            CreatePatternCompilation(),
            "ObjectPair");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsUnknown, Is.True);
            Assert.That(result.Summary.Completeness, Is.EqualTo(
                EffectCompleteness.Incomplete));
        }
    }

    private static CSharpCompilation CreatePatternCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;

            public static class Sample {
                public static bool ObjectPair(object value) => value is (1, 1);
                public static bool ObjectDiscardPair(object value) => value is (_, _);
                public static bool ITupleDiscardPair(ITuple value) => value is (_, _);
                public static bool ValueTupleDiscardPair((int, int) value) => value is (_, _);
                public static bool DeconstructPair(Pair value) => value is (1, 1);

                public sealed class Pair {
                    public void Deconstruct(out int first, out int second) {
                        first = 1;
                        second = 1;
                    }
                }
            }
            """);
    }

    private static IRecursivePatternOperation Pattern(
        Compilation compilation,
        string methodName)
    {
        var method = EffectTestHost.SampleMethod(compilation, methodName);
        return EffectTestHost.RootOperation(compilation, method)
            .DescendantsAndSelf()
            .OfType<IRecursivePatternOperation>()
            .Single();
    }
}
