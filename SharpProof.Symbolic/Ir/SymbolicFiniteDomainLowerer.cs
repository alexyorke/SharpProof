using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicForeachDomainPlan(
    ImmutableArray<SymbolicCondition> Conditions);

internal static class SymbolicFiniteDomainLowerer
{
    internal static SymbolicLoweringResult<SymbolicForeachDomainPlan> LowerForeachDomain(
        ExpressionSyntax expression,
        StatementSyntax foreachStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (foreachStatement is not ForEachStatementSyntax forEach ||
            semanticModel.GetDeclaredSymbol(forEach, cancellationToken) is not ILocalSymbol iterationSymbol ||
            !TryCreateSymbolTerm(iterationSymbol.OriginalDefinition, out var iterationTerm))
            return Unsupported(foreachStatement, "iteration");

        if (!SymbolicProgramPointFacts.TryGetFiniteElementExpressions(expression, out var elements) &&
            !SymbolicProgramPointFacts.TryGetPriorAssignedFiniteElementExpressions(
                expression,
                foreachStatement,
                semanticModel,
                cancellationToken,
                out elements))
            return Unsupported(expression, "elements");

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>(2);
        SymbolicCondition? finiteDomain = null;
        var allReferencesNonNull =
            SymbolicFactFactory.GetTrackedSymbolType(iterationSymbol.OriginalDefinition)?.IsReferenceType == true;
        foreach (var element in elements)
        {
            if (SymbolMutationFacts.ExpressionReferencesSymbol(
                    element,
                    iterationSymbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken))
                return Unsupported(element, "self-reference");

            var lowering = SymbolicSemanticPipeline.LowerTerm(element, context);
            if (lowering is { IsExact: true, Value: { } elementTerm } &&
                CanCompareIrTerms(iterationTerm, elementTerm))
            {
                var equality = SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    iterationTerm,
                    elementTerm,
                    element,
                    "ir.path.foreach-entry.finite-domain");
                finiteDomain = finiteDomain == null
                    ? equality
                    : new SymbolicBinaryCondition(
                        SymbolicConditionOperator.Or,
                        finiteDomain,
                        equality);
            }
            else if (!allReferencesNonNull)
            {
                return Unsupported(element, "element");
            }

            allReferencesNonNull = allReferencesNonNull &&
                NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                    element,
                    semanticModel,
                    cancellationToken);
        }

        if (finiteDomain != null)
            conditions.Add(finiteDomain);
        if (allReferencesNonNull &&
            iterationTerm.Kind == SharpProof.ProofCore.Smt.SmtValueKind.Reference)
            conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                iterationTerm,
                new SymbolicNullTerm(),
                foreachStatement,
                "ir.path.foreach-entry.finite-domain-not-null"));

        return conditions.Count == 0
            ? Unsupported(foreachStatement, "empty")
            : SymbolicLoweringResult<SymbolicForeachDomainPlan>.Exact(
                new SymbolicForeachDomainPlan(conditions.ToImmutable()),
                Provenance(foreachStatement, "exact"));
    }

    private static SymbolicLoweringResult<SymbolicForeachDomainPlan> Unsupported(
        SyntaxNode source,
        string detail) =>
        SymbolicLoweringResult<SymbolicForeachDomainPlan>.Unsupported(Provenance(source, detail));

    private static SymbolicLoweringProvenance Provenance(SyntaxNode source, string detail) =>
        new("finite-foreach-domain", source.Span, detail);
}
