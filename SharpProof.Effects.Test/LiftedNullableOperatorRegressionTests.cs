using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class LiftedNullableOperatorRegressionTests
{
    [Test]
    public void DefinitelyNullLiftedOperatorsSkipUserCalls()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class OperatorFailure : Exception {
            }

            public readonly struct Number {
                public static Number operator +(Number left, Number right) {
                    _ = new object();
                    throw new OperatorFailure();
                }

                public static Number operator -(Number value) {
                    _ = new object();
                    throw new OperatorFailure();
                }

                public static Number operator ++(Number value) {
                    _ = new object();
                    throw new OperatorFailure();
                }
            }

            public static class Sample {
                private static int state;

                public static void SkipBinary() {
                    _ = (Number?)null + new Number();
                    state++;
                }

                public static void SkipUnary() {
                    _ = -(Number?)null;
                    state++;
                }

                public static void SkipIncrement() {
                    Number? value = null;
                    value++;
                    state++;
                }

                public static void SkipCompound() {
                    Number? value = null;
                    value += new Number();
                    state++;
                }
            }
            """);
        var binaryMethod = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "SkipBinary");
        var unaryMethod = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "SkipUnary");
        var incrementMethod = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "SkipIncrement");
        var compoundMethod = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "SkipCompound");
        var binaryOperation = Operation(compilation, binaryMethod)
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .Single(static operation => operation.OperatorMethod != null);
        var unaryOperation = Operation(compilation, unaryMethod)
            .DescendantsAndSelf()
            .OfType<IUnaryOperation>()
            .Single(static operation => operation.OperatorMethod != null);
        var incrementOperation = Operation(compilation, incrementMethod)
            .DescendantsAndSelf()
            .OfType<IIncrementOrDecrementOperation>()
            .Single(static operation => operation.OperatorMethod != null);
        var compoundOperation = Operation(compilation, compoundMethod)
            .DescendantsAndSelf()
            .OfType<ICompoundAssignmentOperation>()
            .Single(static operation => operation.OperatorMethod != null);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(binaryOperation.IsLifted, Is.True);
            Assert.That(unaryOperation.IsLifted, Is.True);
            Assert.That(incrementOperation.IsLifted, Is.True);
            Assert.That(compoundOperation.IsLifted, Is.True);
            AssertSkipped(session.Analyze(binaryMethod).Summary, "binary");
            AssertSkipped(session.Analyze(unaryMethod).Summary, "unary");
            AssertSkipped(
                session.Analyze(incrementMethod).Summary,
                "increment");
            AssertSkipped(
                session.Analyze(compoundMethod).Summary,
                "compound");
        }
    }

    private static void AssertSkipped(EffectSummary summary, string kind)
    {
        Assert.That(
            summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None),
            $"the skipped {kind} operator cannot allocate");
        Assert.That(
            summary.Throws.IsEmpty,
            Is.True,
            $"the skipped {kind} operator cannot throw");
        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.True,
            $"the skipped {kind} operator cannot hide the suffix");
    }

    private static IOperation Operation(
        Compilation compilation,
        IMethodSymbol method)
    {
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        return compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax) ??
            throw new InvalidOperationException(
                $"Operation for '{method.Name}' was not found.");
    }
}
