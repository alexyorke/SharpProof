using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ImplicitIncrementConversionRegressionTests
{
    [Test]
    public void PrefixPostfixAndHelperIncrementsRemainConservative()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "DirectPostfix",
                     "DirectPrefix",
                     "DirectPostfixDecrement",
                     "DirectPrefixDecrement",
                     "Helper"
                 })
        {
            var method = EffectTestHost.SampleMethod(compilation, methodName);
            var summary = session.Analyze(method).Summary;
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
            Assert.That(
                summary.Uncertainty.HasFlag(
                    EffectUncertainty.UnsupportedOperation),
                Is.True,
                methodName);
            Assert.That(summary.Reads.IsUnknown, Is.True, methodName);
            Assert.That(summary.Writes.IsUnknown, Is.True, methodName);
            Assert.That(
                summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Unknown),
                methodName);
            Assert.That(summary.Throws.IncludesUnknown, Is.True, methodName);
        }

        foreach (var methodName in new[]
                 {
                     "DirectPostfix",
                     "DirectPrefix",
                     "DirectPostfixDecrement",
                     "DirectPrefixDecrement"
                 })
        {
            var method = EffectTestHost.SampleMethod(compilation, methodName);
            var increment = EffectTestHost.RootOperation(compilation, method)
                .DescendantsAndSelf()
                .OfType<IIncrementOrDecrementOperation>()
                .Single();
            var completion = EffectTestHost.CreateCompletionEvaluator(
                compilation,
                method);
            Assert.That(
                completion.CanCompleteNormally(increment),
                Is.False,
                methodName);
            Assert.That(
                SharpProof.Roslyn.RoslynCfgThrowFacts.OperationMayThrow(
                    increment),
                Is.True,
                methodName);
        }
    }

    [Test]
    public void UnknownIncrementKeepsMatchingCatchReachable()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "CatchPostfix",
                     "CatchPrefix",
                     "CatchHelper"
                 })
        {
            var method = EffectTestHost.SampleMethod(compilation, methodName);
            var reachability = EffectTestHost.CreateHandlerReachability(
                compilation,
                method,
                session);
            Assert.That(
                reachability.IsReachable(
                    EffectTestHost.CatchClauseIn(method),
                    inFilter: false),
                Is.True,
                methodName);
            var summary = session.Analyze(method).Summary;
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;

            public struct Wrapper {
                public int Value;

                public static implicit operator int(Wrapper value) {
                    return value.Value;
                }

                public static implicit operator Wrapper(int value) {
                    return new Wrapper { Value = value };
                }
            }

            public static class Sample {
                private static int s_state;

                public static Wrapper DirectPostfix(Wrapper value) {
                    value++;
                    return value;
                }

                public static Wrapper DirectPrefix(Wrapper value) {
                    ++value;
                    return value;
                }

                public static Wrapper DirectPostfixDecrement(Wrapper value) {
                    value--;
                    return value;
                }

                public static Wrapper DirectPrefixDecrement(Wrapper value) {
                    --value;
                    return value;
                }

                public static Wrapper Helper(Wrapper value) {
                    return IncrementHelper(value);
                }

                private static Wrapper IncrementHelper(Wrapper value) {
                    value--;
                    return value;
                }

                public static void CatchPostfix(Wrapper value) {
                    try {
                        value++;
                    }
                    catch (InvalidOperationException) {
                        s_state++;
                    }
                }

                public static void CatchPrefix(Wrapper value) {
                    try {
                        ++value;
                    }
                    catch (InvalidOperationException) {
                        s_state++;
                    }
                }

                public static void CatchHelper(Wrapper value) {
                    try {
                        IncrementHelper(value);
                    }
                    catch (InvalidOperationException) {
                        s_state++;
                    }
                }
            }
            """);
    }
}
