using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicKnownGuardFacts
{
    private const string ArgumentOutOfRangeExceptionType = "System.ArgumentOutOfRangeException";

    internal static bool TryCreateArgumentOutOfRangeGuardConditions(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm subject,
        out SymbolicCondition triggerCondition,
        out SymbolicCondition normalCompletionCondition,
        out string guardKey)
    {
        subject = null!;
        triggerCondition = null!;
        normalCompletionCondition = null!;
        guardKey = string.Empty;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !operation.TargetMethod.IsStatic ||
            !string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(operation.TargetMethod.ContainingType),
                ArgumentOutOfRangeExceptionType,
                StringComparison.Ordinal) ||
            !TryGetGuardRelations(
                operation.TargetMethod.Name,
                out var triggerRelation,
                out var normalRelation,
                out var requiresComparisonValue,
                out guardKey) ||
            !TryGetArgumentExpression(operation, 0, out var subjectExpression))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (!SymbolicIrLowerer.TryLowerTerm(subjectExpression, context, out subject) ||
            subject.Kind != SmtValueKind.Int)
        {
            subject = null!;
            return false;
        }

        SymbolicTerm comparisonValue;
        if (requiresComparisonValue)
        {
            if (!TryGetArgumentExpression(operation, 1, out var comparisonExpression) ||
                !SymbolicIrLowerer.TryLowerTerm(comparisonExpression, context, out comparisonValue) ||
                comparisonValue.Kind != SmtValueKind.Int)
            {
                subject = null!;
                return false;
            }
        }
        else
        {
            comparisonValue = new SymbolicIntegerConstantTerm(0);
        }

        var provenance = "ir.known-guard.argument-out-of-range." + guardKey;
        triggerCondition = CreateRelationCondition(
            triggerRelation,
            subject,
            comparisonValue,
            invocation,
            provenance + ".trigger");
        normalCompletionCondition = CreateRelationCondition(
            normalRelation,
            subject,
            comparisonValue,
            invocation,
            provenance + ".normal-completion");
        return true;
    }

    private static bool TryGetArgumentExpression(
        IInvocationOperation operation,
        int parameterOrdinal,
        out ExpressionSyntax expression)
    {
        foreach (var argument in operation.Arguments)
            if (argument.Parameter?.Ordinal == parameterOrdinal &&
                argument.ArgumentKind == ArgumentKind.Explicit &&
                argument.Syntax is ArgumentSyntax argumentSyntax)
            {
                expression = argumentSyntax.Expression;
                return true;
            }

        expression = null!;
        return false;
    }

    private static SymbolicCondition CreateRelationCondition(
        SymbolicRelationOperator relation,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode sourceNode,
        string provenance)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(relation, left, right),
            sourceNode,
            provenance,
            evidenceKey: provenance));
    }

    private static bool TryGetGuardRelations(
        string methodName,
        out SymbolicRelationOperator triggerRelation,
        out SymbolicRelationOperator normalRelation,
        out bool requiresComparisonValue,
        out string guardKey)
    {
        (triggerRelation, normalRelation, requiresComparisonValue, guardKey) = methodName switch
        {
            "ThrowIfNegative" => (SymbolicRelationOperator.LessThan, SymbolicRelationOperator.GreaterThanOrEqual, false,
                "negative"),
            "ThrowIfZero" => (SymbolicRelationOperator.Equal, SymbolicRelationOperator.NotEqual, false, "zero"),
            "ThrowIfNegativeOrZero" => (SymbolicRelationOperator.LessThanOrEqual, SymbolicRelationOperator.GreaterThan,
                false, "negative-or-zero"),
            "ThrowIfLessThan" => (SymbolicRelationOperator.LessThan, SymbolicRelationOperator.GreaterThanOrEqual, true,
                "less-than"),
            "ThrowIfLessThanOrEqual" => (SymbolicRelationOperator.LessThanOrEqual, SymbolicRelationOperator.GreaterThan,
                true, "less-than-or-equal"),
            "ThrowIfGreaterThan" => (SymbolicRelationOperator.GreaterThan, SymbolicRelationOperator.LessThanOrEqual,
                true, "greater-than"),
            "ThrowIfGreaterThanOrEqual" => (SymbolicRelationOperator.GreaterThanOrEqual,
                SymbolicRelationOperator.LessThan, true, "greater-than-or-equal"),
            _ => default
        };
        return !string.IsNullOrEmpty(guardKey);
    }
}