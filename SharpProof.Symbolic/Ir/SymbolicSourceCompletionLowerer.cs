using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicSourceCompletionPlan(
    ImmutableArray<SymbolicCondition> Conditions);

internal static class SymbolicSourceCompletionLowerer
{
    internal static SymbolicOperationTransitionResult ApplyNormalCompletion(
        SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        bool includeThrowGuardFacts,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var provenance = ImmutableArray.CreateBuilder<SymbolicLoweringProvenance>();
        if (includeThrowGuardFacts)
            AdoptExact(
                ref state,
                provenance,
                ApplyThrowGuard(
                    state,
                    expression,
                    statement,
                    semanticModel,
                    cancellationToken,
                    "ir.path.normal-completion.throw-guarded-not-null"));

        var frameworkLowering = SymbolicFrameworkPostconditionLowerer.Lower(
            expression,
            statement,
            semanticModel,
            cancellationToken);
        if (frameworkLowering is { IsExact: true, Value: { } frameworkPlan })
            AdoptExact(
                ref state,
                provenance,
                ApplyConditions(
                    state,
                    frameworkPlan.BeforeDoesNotReturnIf,
                    expression,
                    "ir.path.normal-completion.framework-before"));

        foreach (var (_, _, parameter, argumentSyntax) in
                 SymbolicFrameworkPostconditionLowerer.EnumerateExplicitInvocationArguments(
                     expression,
                     semanticModel,
                     cancellationToken))
        {
            if (parameter.RefKind != RefKind.None ||
                !NullableFlowFacts.TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                    argumentSyntax.Expression,
                    statement,
                    semanticModel,
                    cancellationToken))
                continue;

            AdoptExact(
                ref state,
                provenance,
                SymbolicReachabilityLowerer.Apply(
                    state,
                    argumentSyntax.Expression,
                    !doesNotReturnWhen,
                    semanticModel,
                    cancellationToken));
        }

        if (frameworkLowering is { IsExact: true, Value: { } afterPlan })
            AdoptExact(
                ref state,
                provenance,
                ApplyConditions(
                    state,
                    afterPlan.AfterDoesNotReturnIf,
                    expression,
                    "ir.path.normal-completion.framework-after"));

        var sourceLowering = Lower(expression, statement, semanticModel, cancellationToken);
        if (sourceLowering is { IsExact: true, Value: { } sourcePlan })
            AdoptExact(
                ref state,
                provenance,
                ApplyConditions(
                    state,
                    sourcePlan.Conditions,
                    expression,
                    "ir.path.normal-completion.source"));

        return SymbolicOperationTransitionResult.Exact(state, provenance);
    }

    internal static SymbolicOperationTransitionResult ApplyConditions(
        SymbolicState state,
        ImmutableArray<SymbolicCondition> conditions,
        SyntaxNode source,
        string provenance) =>
        conditions.IsDefaultOrEmpty
            ? Exact(state, source, "no-conditions")
            : SymbolicOperationTransferKernel.AssumeAll(
                state,
                conditions,
                source.Span,
                provenance);

    internal static SymbolicOperationTransitionResult ApplyThrowGuard(
        SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax guardedStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string nonNullProvenance)
    {
        var guardedValue = SymbolicAssignmentStateTransfer.GetThrowGuardedValue(expression);
        if (!guardedValue.HasGuard)
            return Exact(state, expression, "no-throw-guard");
        if (guardedValue.GuardExpression is { } guard)
            return SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                guard,
                guardedStatement,
                semanticModel,
                cancellationToken)
                ? Exact(state, guard, "invalidated-guard")
                : SymbolicReachabilityLowerer.Apply(
                    state,
                    guard,
                    guardedValue.GuardBranchWhenTrue,
                    semanticModel,
                    cancellationToken);
        if (!guardedValue.RequiresNonNullValue ||
            SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(
                guardedValue.EffectiveValueExpression,
                guardedStatement,
                semanticModel,
                cancellationToken))
            return Exact(state, expression, "no-stable-reference");
        if (NullableFlowFacts.IsDefinitelyNullReferenceValue(
                guardedValue.EffectiveValueExpression,
                semanticModel,
                cancellationToken))
            return SymbolicOperationTransferKernel.Complete(
                state,
                guardedValue.EffectiveValueExpression.Span);
        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                guardedValue.EffectiveValueExpression,
                semanticModel,
                cancellationToken))
            return Exact(state, expression, "known-non-null");

        var lowering = SymbolicSemanticPipeline.LowerTerm(
            guardedValue.EffectiveValueExpression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { Kind: SmtValueKind.Reference } subject })
            return Unsupported(state, expression, "reference");
        return SymbolicOperationTransferKernel.Assume(
            state,
            SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                subject,
                new SymbolicNullTerm(),
                guardedValue.EffectiveValueExpression,
                nonNullProvenance),
            assumeTrue: true,
            guardedValue.EffectiveValueExpression.Span,
            nonNullProvenance);
    }

    internal static SymbolicLoweringResult<SymbolicSourceCompletionPlan> Lower(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddArrayBounds(conditions, expression, statement, semanticModel, cancellationToken);
        AddDereferenceSuccess(conditions, expression, statement, semanticModel, cancellationToken);
        return SymbolicLoweringResult<SymbolicSourceCompletionPlan>.Exact(
            new SymbolicSourceCompletionPlan(conditions.ToImmutable()),
            new SymbolicLoweringProvenance("source-completion", expression.Span, "exact"));
    }

    private static void AddArrayBounds(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = SymbolicFrameworkPostconditionLowerer.UnwrapAwaited(expression);
        if (expression is not ArrayCreationExpressionSyntax arrayCreation)
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        foreach (var sizeExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
            if (!SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                    sizeExpression,
                    statement,
                    semanticModel,
                    cancellationToken) &&
                SymbolicSemanticPipeline.LowerTerm(sizeExpression, context) is
                    { IsExact: true, Value: { Kind: SmtValueKind.Int } size })
                conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    size,
                    new SymbolicIntegerConstantTerm(0),
                    sizeExpression,
                    "ir.path.normal-completion.array-length.non-negative"));
    }

    private static void AddDereferenceSuccess(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is AwaitExpressionSyntax awaited)
        {
            var awaitable = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(awaited.Expression);
            AddStableNonNull(
                conditions,
                awaitable,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.awaitable-not-null");
            expression = awaitable;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            elementAccess.ArgumentList.Arguments.Count == 1 &&
            !SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                elementAccess,
                statement,
                semanticModel,
                cancellationToken) &&
            SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(
                elementAccess,
                new SymbolicLoweringContext(semanticModel, cancellationToken)) is
                { IsExact: true, Value: { } inRange })
            conditions.Add(inRange);

        if (TryGetDereferenceReceiver(expression, semanticModel, cancellationToken, out var receiver))
            AddStableNonNull(
                conditions,
                receiver,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.dereference.receiver-not-null");
    }

    private static void AddStableNonNull(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance)
    {
        if (NullableFlowFacts.TryGetArgumentTargetSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var symbol) &&
            !SymbolicLoopStateTransfer.AnyConditionSymbolMutatedInStatement(
                expression,
                statement,
                semanticModel,
                cancellationToken) &&
            TryCreateSymbolTerm(symbol, out var term) &&
            term.Kind == SmtValueKind.Reference)
            conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                term,
                new SymbolicNullTerm(),
                expression,
                provenance));
    }

    private static bool TryGetDereferenceReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax receiver)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(invocation.Expression) is
                         MemberAccessExpressionSyntax memberAccess &&
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
        CancellationToken cancellationToken) =>
        semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation
            { TargetMethod.ReducedFrom: not null };

    private static void AdoptExact(
        ref SymbolicState state,
        ImmutableArray<SymbolicLoweringProvenance>.Builder provenance,
        SymbolicOperationTransitionResult transition)
    {
        if (!transition.IsExact)
            return;
        state = transition.State;
        provenance.AddRange(transition.Provenance);
    }

    private static SymbolicOperationTransitionResult Exact(
        SymbolicState state,
        SyntaxNode source,
        string detail) =>
        SymbolicOperationTransitionResult.Exact(
            state,
            ImmutableArray.Create(new SymbolicLoweringProvenance(
                "source-completion",
                source.Span,
                detail)));

    private static SymbolicOperationTransitionResult Unsupported(
        SymbolicState state,
        SyntaxNode source,
        string detail) =>
        SymbolicOperationTransitionResult.Unsupported(
            state,
            SymbolicUnknownReason.UnsupportedIrEncoding,
            ImmutableArray.Create(new SymbolicLoweringProvenance(
                "source-completion",
                source.Span,
                detail)));
}
