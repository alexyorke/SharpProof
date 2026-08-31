using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class LiftedNullableConversionRegressionTests
{
    [Test]
    public void DefinitelyNullLiftedConversionsSkipUserOperators()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class ConversionFailure : Exception {
            }

            public readonly struct DivergingInput {
            }

            public readonly struct DivergingOutput {
                public static implicit operator DivergingOutput(
                    DivergingInput value) {
                    while (true) {
                    }
                }
            }

            public readonly struct ThrowingInput {
            }

            public readonly struct ThrowingOutput {
                public static implicit operator ThrowingOutput(
                    ThrowingInput value) {
                    _ = new object();
                    throw new ConversionFailure();
                }
            }

            public static class Sample {
                private static int state;

                public static void SkipDiverging() {
                    DivergingInput? input = null;
                    DivergingOutput? output = input;
                    state++;
                }

                public static void SkipThrowing(ref int caught) {
                    ThrowingInput? input = null;
                    try {
                        ThrowingOutput? output = input;
                    }
                    catch (ConversionFailure) {
                        caught++;
                    }
                    state++;
                }
            }
            """);
        var divergingMethod = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "SkipDiverging");
        var throwingMethod = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "SkipThrowing");
        var conversion = Operation(compilation, divergingMethod)
            .DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Single(static operation =>
                operation.OperatorMethod != null);
        var session = new EffectAnalysisSession(compilation);
        var diverging = session.Analyze(divergingMethod);
        var throwing = session.Analyze(throwingMethod);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsNullable(conversion.Operand.Type), Is.True);
            Assert.That(IsNullable(conversion.Type), Is.True);
            Assert.That(
                IsNullable(
                    conversion.OperatorMethod!.Parameters[0].Type),
                Is.False);
            Assert.That(
                diverging.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True,
                "the skipped divergent operator cannot hide the suffix");
            Assert.That(
                throwing.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.None),
                "the skipped operator cannot allocate");
            Assert.That(
                throwing.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.False,
                "the skipped operator cannot reach its catch handler");
            Assert.That(
                throwing.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True,
                "the normally completing null path must retain the suffix");
        }
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

    private static bool IsNullable(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType:
                SpecialType.System_Nullable_T
        };
    }
}
