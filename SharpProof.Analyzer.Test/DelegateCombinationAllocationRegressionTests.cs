using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Effects;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class DelegateCombinationAllocationRegressionTests
{
    [Test]
    public void BuiltInAdditionAndSubtractionViolateZeroAllocations()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture
            {
                [ZeroAllocations]
                public static Action? Add(Action? left, Action? right) =>
                    left + right;

                [ZeroAllocations]
                public static Action? Subtract(Action? left, Action? right) =>
                    left - right;
            }
            """,
            ["SP0045"]);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
        var fixture = compilation.GetTypeByMetadataName("Fixture")!;

        foreach (var methodName in new[] { "Add", "Subtract" })
        {
            var method = fixture.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Single();
            var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
            var operation = compilation.GetSemanticModel(syntax.SyntaxTree)
                .GetOperation(syntax)!;
            var binary = operation.DescendantsAndSelf()
                .OfType<IBinaryOperation>()
                .Single();
            var result = session.AnalyzeEffects(
                method,
                CancellationToken.None);
            var evaluation = EffectContractDiagnostics.Evaluate(
                    method,
                    Location.None,
                    session,
                    static _ => { },
                    CancellationToken.None)
                .Single(static item =>
                    item.Kind ==
                    EffectEvaluationContractKind.ZeroAllocations);
            var shape = methodName + ":" + binary.OperatorKind + "/" +
                binary.Type?.TypeKind + "/" +
                binary.OperatorMethod?.MethodKind;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Allocation,
                    Is.EqualTo(EffectAllocationKind.Managed),
                    shape);
                Assert.That(
                    result.Projection.Effects &
                        EffectContractKind.Allocates,
                    Is.EqualTo(EffectContractKind.Allocates),
                    shape);
                Assert.That(
                    evaluation.Outcome,
                    Is.EqualTo(EffectEvaluationOutcome.Unknown),
                    shape);
                Assert.That(evaluation.Diagnostic?.Id, Is.EqualTo("SP0045"));
                Assert.That(
                    evaluation.Evidence,
                    Does.Contain("actual.allocation=Managed"));
            }
        }
    }
}
