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
        AddTopLevelArrayCreationNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        AddTopLevelDereferenceNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
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

            SymbolicProgramPointFacts.AddReachabilityCondition(
                ref state,
                argumentSyntax.Expression,
                !doesNotReturnWhen,
                semanticModel,
                cancellationToken);
        }
    }

    private static void AddTopLevelArrayCreationNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = SymbolicFrameworkPostconditionLowerer.UnwrapAwaited(expression);
        if (expression is not ArrayCreationExpressionSyntax arrayCreation) return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        foreach (var sizeExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
        {
            var lowering = SymbolicSemanticPipeline.LowerTerm(sizeExpression, context);
            if (SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(sizeExpression, statement, semanticModel, cancellationToken) ||
                lowering is not { IsExact: true, Value: { } sizeTerm } ||
                sizeTerm.Kind != SmtValueKind.Int)
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThanOrEqual,
                sizeTerm,
                new SymbolicIntegerConstantTerm(0),
                sizeExpression,
                "ir.path.normal-completion.array-length.non-negative");
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

    private static void AddTopLevelDereferenceNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = SymbolicProgramPointFacts.UnwrapExpression(expression);
        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            var awaitableExpression = SymbolicProgramPointFacts.UnwrapExpression(awaitExpression.Expression);
            AddStableReferenceNonNullStateFact(
                ref state,
                awaitableExpression,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.awaitable-not-null");
            expression = awaitableExpression;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            !SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(elementAccess, statement, semanticModel, cancellationToken) &&
            elementAccess.ArgumentList.Arguments.Count == 1)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(elementAccess, context) is
                { IsExact: true, Value: { } inRangeCondition })
                state = state.AddPathCondition(inRangeCondition);
        }

        if (!TryGetTopLevelDereferenceReceiver(expression, semanticModel, cancellationToken, out var receiver)) return;

        AddStableReferenceNonNullStateFact(
            ref state,
            receiver,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.normal-completion.dereference.receiver-not-null");
    }

    private static bool AddStableReferenceNonNullStateFact(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        bool allowArgumentMutation = false)
    {
        if (!NullableFlowFacts.TryGetArgumentTargetSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var symbol) ||
            !allowArgumentMutation &&
            SymbolicLoopStateTransfer.AnyConditionSymbolMutatedInStatement(expression, statement, semanticModel, cancellationToken))
            return false;

        if (!TryCreateSymbolTerm(symbol, out var symbolTerm) ||
            symbolTerm.Kind != SmtValueKind.Reference)
            return false;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.NotEqual,
            symbolTerm,
            new SymbolicNullTerm(),
            expression,
            provenance);
        return true;
    }

    private static bool TryGetTopLevelDereferenceReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax receiver)
    {
        expression = SymbolicProgramPointFacts.UnwrapExpression(expression);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when SymbolicProgramPointFacts.UnwrapExpression(invocation.Expression) is MemberAccessExpressionSyntax memberAccess &&
                     !IsReducedExtensionMethodInvocation(invocation, semanticModel, cancellationToken):
                receiver = memberAccess.Expression;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                receiver = memberAccess.Expression;
                return true;
            case ElementAccessExpressionSyntax elementAccess:
                receiver = elementAccess.Expression;
                return true;
            default:
                receiver = null!;
                return false;
        }
    }

    private static bool IsReducedExtensionMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
               invocationOperation.TargetMethod.ReducedFrom != null;
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
