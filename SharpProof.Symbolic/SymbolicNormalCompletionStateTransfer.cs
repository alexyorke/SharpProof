using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicNormalCompletionStateTransfer
{
    internal static void AddNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        bool includeThrowGuardFacts,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (includeThrowGuardFacts)
            AddTopLevelThrowGuardNormalCompletionStateFacts(
                ref state,
                expression,
                statement,
                semanticModel,
                cancellationToken);

        var frameworkLowering = SymbolicFrameworkPostconditionLowerer.Lower(
            expression,
            statement,
            semanticModel,
            cancellationToken);
        if (frameworkLowering is { IsExact: true, Value: { } frameworkPlan })
            ApplyConditions(
                ref state,
                frameworkPlan.BeforeDoesNotReturnIf,
                expression,
                "ir.path.normal-completion.framework-before");
        AddTopLevelDoesNotReturnIfNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        if (frameworkLowering is { IsExact: true, Value: { } afterPlan })
            ApplyConditions(
                ref state,
                afterPlan.AfterDoesNotReturnIf,
                expression,
                "ir.path.normal-completion.framework-after");
        var sourceLowering = SymbolicSourceCompletionLowerer.Lower(
            expression,
            statement,
            semanticModel,
            cancellationToken);
        if (sourceLowering is { IsExact: true, Value: { } sourcePlan })
            ApplyConditions(
                ref state,
                sourcePlan.Conditions,
                expression,
                "ir.path.normal-completion.source");
    }

    private static void AddTopLevelDoesNotReturnIfNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var (_, _, parameter, argumentSyntax) in SymbolicFrameworkPostconditionLowerer.EnumerateExplicitInvocationArguments(
                     expression, semanticModel, cancellationToken))
        {
            if (parameter.RefKind != RefKind.None ||
                !NullableFlowFacts.TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(argumentSyntax.Expression, statement, semanticModel,
                    cancellationToken))
                continue;

            var transition = SymbolicReachabilityLowerer.Apply(
                state,
                argumentSyntax.Expression,
                !doesNotReturnWhen,
                semanticModel,
                cancellationToken);
            if (transition.IsExact)
                state = transition.State;
        }
    }

    private static void AddTopLevelThrowGuardNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SymbolicLoopStateTransfer.AddThrowGuardedExpressionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.normal-completion.throw-guarded-not-null");
    }

    internal static void ApplyConditions(
        ref SymbolicState state,
        ImmutableArray<SymbolicCondition> conditions,
        SyntaxNode source,
        string provenance)
    {
        if (conditions.IsDefaultOrEmpty)
            return;
        var transition = SymbolicOperationTransferKernel.AssumeAll(
            state,
            conditions,
            source.Span,
            provenance);
        if (transition.IsExact)
            state = transition.State;
    }
}
