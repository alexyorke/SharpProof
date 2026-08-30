using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CompoundAssignmentConversionRegressionTests
{
    [Test]
    public void CompoundAssignmentExecutesBothUserDefinedConversions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class InConversionFailure : Exception {
            }

            public sealed class OutConversionFailure : Exception {
            }

            public readonly struct InputOperand {
            }

            public readonly struct OperatorResult {
                public OperatorResult(OutConversionFailure? failure) {
                    Failure = failure;
                }

                public OutConversionFailure? Failure { get; }
            }

            public readonly struct EffectfulTarget {
                private static int s_inConversionState;
                private readonly InConversionFailure? _inFailure;
                private readonly OutConversionFailure? _outFailure;

                public EffectfulTarget(
                    InConversionFailure? inFailure,
                    OutConversionFailure? outFailure) {
                    _inFailure = inFailure;
                    _outFailure = outFailure;
                }

                public static implicit operator InputOperand(
                    EffectfulTarget value) {
                    s_inConversionState++;
                    if (value._inFailure != null) {
                        throw value._inFailure;
                    }
                    return default;
                }

                public static implicit operator EffectfulTarget(
                    OperatorResult value) {
                    _ = new object();
                    if (value.Failure != null) {
                        throw value.Failure;
                    }
                    return default;
                }

                public static OperatorResult operator +(
                    InputOperand left,
                    EffectfulTarget right) =>
                    new(right._outFailure);
            }

            public readonly struct DivergingInOperand {
            }

            public readonly struct DivergingInResult {
            }

            public readonly struct DivergingInTarget {
                public static implicit operator DivergingInOperand(
                    DivergingInTarget value) {
                    while (true) {
                    }
                }

                public static implicit operator DivergingInTarget(
                    DivergingInResult value) => default;

                public static DivergingInResult operator +(
                    DivergingInOperand left,
                    DivergingInTarget right) => default;
            }

            public readonly struct DivergingOutOperand {
            }

            public readonly struct DivergingOutResult {
            }

            public readonly struct DivergingOutTarget {
                public static implicit operator DivergingOutOperand(
                    DivergingOutTarget value) => default;

                public static implicit operator DivergingOutTarget(
                    DivergingOutResult value) {
                    while (true) {
                    }
                }

                public static DivergingOutResult operator +(
                    DivergingOutOperand left,
                    DivergingOutTarget right) => default;
            }

            public static class Sample {
                public static void Effects(
                    EffectfulTarget left,
                    EffectfulTarget right) {
                    left += right;
                }

                public static void CatchEffects(
                    EffectfulTarget left,
                    EffectfulTarget right,
                    ref int inCaught,
                    ref int outCaught) {
                    try {
                        left += right;
                    }
                    catch (InConversionFailure) {
                        inCaught++;
                    }
                    catch (OutConversionFailure) {
                        outCaught++;
                    }
                }

                public static void DivergingIn(
                    DivergingInTarget left,
                    DivergingInTarget right) {
                    left += right;
                }

                public static void DivergingOut(
                    DivergingOutTarget left,
                    DivergingOutTarget right) {
                    left += right;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var effectsMethod = Method(compilation, "Effects");
        var catchMethod = Method(compilation, "CatchEffects");
        var effectsOperation = Compound(compilation, effectsMethod);
        var effects = session.Analyze(effectsMethod).Summary;
        var catches = session.Analyze(catchMethod).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                effectsOperation.InConversion.MethodSymbol,
                Is.Not.Null);
            Assert.That(
                effectsOperation.OutConversion.MethodSymbol,
                Is.Not.Null);
            Assert.That(
                effects.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "the in-conversion static write must be scanned");
            Assert.That(
                effects.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed),
                "the out-conversion allocation must be scanned");
            Assert.That(
                catches.Writes.Contains(EffectRegionId.Parameter(2)),
                Is.True,
                "the in-conversion exception must reach its catch");
            Assert.That(
                catches.Writes.Contains(EffectRegionId.Parameter(3)),
                Is.True,
                "the out-conversion exception must reach its catch");
            Assert.That(
                CanCompoundComplete(compilation, "DivergingIn"),
                Is.False,
                "a divergent in-conversion prevents normal completion");
            Assert.That(
                CanCompoundComplete(compilation, "DivergingOut"),
                Is.False,
                "a divergent out-conversion prevents normal completion");
        }
    }

    private static bool CanCompoundComplete(
        CSharpCompilation compilation,
        string methodName)
    {
        var method = Method(compilation, methodName);
        var evaluator = new OperationCompletionEvaluator(
            new EffectAnalysisSession(compilation),
            method,
            static (_, _) => false,
            static (_, _) => false,
            static _ => false);
        return evaluator.CanCompleteNormally(Compound(compilation, method));
    }

    private static ICompoundAssignmentOperation Compound(
        Compilation compilation,
        IMethodSymbol method)
    {
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        return compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax)!
            .DescendantsAndSelf()
            .OfType<ICompoundAssignmentOperation>()
            .Single();
    }

    private static IMethodSymbol Method(
        Compilation compilation,
        string methodName)
    {
        return EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            methodName);
    }
}
