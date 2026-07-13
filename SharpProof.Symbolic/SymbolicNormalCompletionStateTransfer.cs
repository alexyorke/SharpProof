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

        AddTopLevelNotNullParameterNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        AddTopLevelKnownGuardNormalCompletionStateFacts(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
        AddTopLevelDoesNotReturnIfNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        AddTopLevelMemberNotNullNormalCompletionStateFacts(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
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

    private static void AddTopLevelNotNullParameterNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { IsParams: false } parameter ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
                !ArgumentRefKindMatches(parameter, argumentSyntax) ||
                !HasNotNullNormalCompletionPostcondition(parameter, cancellationToken) ||
                parameter.RefKind != RefKind.None &&
                !IsUniqueOutputArgumentTarget(
                    invocationOperation,
                    argument,
                    semanticModel,
                    cancellationToken))
                continue;

            AddStableReferenceNonNullStateFact(
                ref state,
                argumentSyntax.Expression,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.parameter-not-null",
                parameter.RefKind != RefKind.None);
        }
    }

    private static bool HasNotNullNormalCompletionPostcondition(
        IParameterSymbol parameter,
        CancellationToken cancellationToken)
    {
        return parameter.RefKind == RefKind.None
            ? NullableFlowFacts.HasNotNullPostcondition(parameter) ||
              NullableFlowFacts.HasInferredNotNullNormalCompletionPostcondition(
                  parameter,
                  cancellationToken)
            : NullableFlowFacts.GetParameterOutputState(parameter) == NullableFlowFactState.NotNull;
    }

    private static bool ArgumentRefKindMatches(IParameterSymbol parameter, ArgumentSyntax argument)
    {
        return parameter.RefKind switch
        {
            RefKind.None => argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefKind.Ref => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword),
            RefKind.Out => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
            _ => false
        };
    }

    private static bool IsUniqueOutputArgumentTarget(
        IInvocationOperation invocation,
        IArgumentOperation argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (argument.Syntax is not ArgumentSyntax argumentSyntax ||
            !NullableFlowFacts.TryGetArgumentTargetSymbol(
                argumentSyntax.Expression,
                semanticModel,
                cancellationToken,
                out var target))
            return false;

        foreach (var otherArgument in invocation.Arguments)
        {
            if (ReferenceEquals(argument, otherArgument) ||
                otherArgument.Syntax is not ArgumentSyntax otherArgumentSyntax ||
                !NullableFlowFacts.TryGetArgumentTargetSymbol(
                    otherArgumentSyntax.Expression,
                    semanticModel,
                    cancellationToken,
                    out var otherTarget))
                continue;

            if (SymbolEqualityComparer.Default.Equals(target, otherTarget)) return false;
        }

        return true;
    }

    internal static void AddTopLevelMemberNotNullNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod.IsStatic ||
            !IsCurrentInstanceInvocation(invocation))
            return;

        var memberTargets = NullableFlowFacts.GetMemberNotNullTargets(invocationOperation.TargetMethod);
        foreach (var memberTarget in memberTargets)
        {
            if (!NullableFlowFacts.TryResolveInstanceMemberTarget(
                    invocationOperation.TargetMethod.ContainingType,
                    memberTarget,
                    out var member) ||
                !NullableFlowFacts.TryGetMemberType(member, out var type) ||
                !TryGetValueKind(type, out var kind) ||
                kind != SmtValueKind.Reference)
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                new SymbolicMemberTerm(
                    new SymbolicVariableTerm(SymbolicStateValueFacts.ImplicitThisVariableName, SmtValueKind.Reference),
                    member.Name,
                    kind),
                new SymbolicNullTerm(),
                invocation,
                "ir.path.normal-completion.member-not-null");
        }
    }

    internal static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
    {
        var invokedExpression = SymbolicProgramPointFacts.UnwrapExpression(invocation.Expression);
        return invokedExpression is IdentifierNameSyntax ||
               invokedExpression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

    internal static bool TryCreateImplicitThisMemberTerm(ISymbol member, out SymbolicTerm term)
    {
        if (!NullableFlowFacts.TryGetMemberType(member, out var type) ||
            !TryGetValueKind(type, out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicMemberTerm(
            new SymbolicVariableTerm(SymbolicStateValueFacts.ImplicitThisVariableName, SmtValueKind.Reference),
            member.Name,
            kind);
        return true;
    }

    private static void AddTopLevelDoesNotReturnIfNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { RefKind: RefKind.None, IsParams: false } parameter ||
                !NullableFlowFacts.TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
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
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
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

    private static ExpressionSyntax UnwrapAwaitedNormalCompletionExpression(ExpressionSyntax expression)
    {
        expression = SymbolicProgramPointFacts.UnwrapExpression(expression);
        return expression is AwaitExpressionSyntax awaitExpression
            ? SymbolicProgramPointFacts.UnwrapExpression(awaitExpression.Expression)
            : expression;
    }

    private static bool IsReducedExtensionMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
               invocationOperation.TargetMethod.ReducedFrom != null;
    }

    private static void AddTopLevelKnownGuardNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is InvocationExpressionSyntax invocation &&
            SymbolicKnownGuardFacts.TryCreateArgumentOutOfRangeGuardConditions(
                invocation,
                semanticModel,
                cancellationToken,
                out _,
                out _,
                out var normalCompletionCondition,
                out _))
            state = state.AddPathCondition(normalCompletionCondition);
    }
}
