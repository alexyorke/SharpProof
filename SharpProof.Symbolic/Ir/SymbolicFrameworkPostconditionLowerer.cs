using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicFrameworkPostconditionPlan(
    ImmutableArray<SymbolicCondition> BeforeDoesNotReturnIf,
    ImmutableArray<SymbolicCondition> AfterDoesNotReturnIf);

internal static class SymbolicFrameworkPostconditionLowerer
{
    internal static SymbolicLoweringResult<SymbolicFrameworkPostconditionPlan> Lower(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var before = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddParameterNotNullConditions(before, expression, statement, semanticModel, cancellationToken);
        AddKnownGuardCondition(before, expression, semanticModel, cancellationToken);

        var after = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddMemberNotNullConditions(after, expression, semanticModel, cancellationToken);
        return Exact(expression, before.ToImmutable(), after.ToImmutable());
    }

    internal static SymbolicLoweringResult<SymbolicFrameworkPostconditionPlan> LowerMemberNotNull(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddMemberNotNullConditions(conditions, expression, semanticModel, cancellationToken);
        return Exact(expression, ImmutableArray<SymbolicCondition>.Empty, conditions.ToImmutable());
    }

    internal static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
    {
        var invokedExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(invocation.Expression);
        return invokedExpression is IdentifierNameSyntax or
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
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
            new SymbolicVariableTerm(
                SymbolicStateValueFacts.ImplicitThisVariableName,
                SmtValueKind.Reference),
            member.Name,
            kind);
        return true;
    }

    private static void AddParameterNotNullConditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var (invocation, argument, parameter, syntax) in
                 EnumerateExplicitInvocationArguments(expression, semanticModel, cancellationToken))
            if (ArgumentRefKindMatches(parameter, syntax) &&
                HasNotNullNormalCompletionPostcondition(parameter, cancellationToken) &&
                (parameter.RefKind == RefKind.None ||
                 IsUniqueOutputArgumentTarget(invocation, argument, semanticModel, cancellationToken)) &&
                TryCreateStableNonNullCondition(
                    syntax.Expression,
                    statement,
                    semanticModel,
                    cancellationToken,
                    "ir.path.normal-completion.parameter-not-null",
                    parameter.RefKind != RefKind.None,
                    out var condition))
                conditions.Add(condition);
    }

    private static void AddKnownGuardCondition(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaited(expression);
        if (expression is InvocationExpressionSyntax invocation &&
            SymbolicKnownGuardFacts.TryCreateArgumentOutOfRangeGuardConditions(
                invocation,
                semanticModel,
                cancellationToken,
                out _,
                out _,
                out var normalCompletionCondition,
                out _))
            conditions.Add(normalCompletionCondition);
    }

    private static void AddMemberNotNullConditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaited(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            operation.TargetMethod.IsStatic ||
            !IsCurrentInstanceInvocation(invocation))
            return;

        foreach (var target in NullableFlowFacts.GetMemberNotNullTargets(operation.TargetMethod))
            if (NullableFlowFacts.TryResolveInstanceMemberTarget(
                    operation.TargetMethod.ContainingType,
                    target,
                    out var member) &&
                TryCreateImplicitThisMemberTerm(member, out var memberTerm) &&
                memberTerm.Kind == SmtValueKind.Reference)
                conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    memberTerm,
                    new SymbolicNullTerm(),
                    invocation,
                    "ir.path.normal-completion.member-not-null"));
    }

    private static bool TryCreateStableNonNullCondition(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        bool allowArgumentMutation,
        out SymbolicCondition condition)
    {
        if (!NullableFlowFacts.TryGetArgumentTargetSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var symbol) ||
            !allowArgumentMutation &&
            SymbolicLoopStateTransfer.AnyConditionSymbolMutatedInStatement(
                expression,
                statement,
                semanticModel,
                cancellationToken) ||
            !TryCreateSymbolTerm(symbol, out var term) ||
            term.Kind != SmtValueKind.Reference)
        {
            condition = null!;
            return false;
        }

        condition = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            term,
            new SymbolicNullTerm(),
            expression,
            provenance);
        return true;
    }

    internal static IEnumerable<(IInvocationOperation Invocation, IArgumentOperation Argument,
        IParameterSymbol Parameter, ArgumentSyntax Syntax)> EnumerateExplicitInvocationArguments(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (UnwrapAwaited(expression) is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            yield break;

        foreach (var argument in operation.Arguments)
            if (argument is
                {
                    ArgumentKind: ArgumentKind.Explicit,
                    Parameter: { IsParams: false } parameter,
                    Syntax: ArgumentSyntax syntax
                })
                yield return (operation, argument, parameter, syntax);
    }

    private static bool HasNotNullNormalCompletionPostcondition(
        IParameterSymbol parameter,
        CancellationToken cancellationToken) =>
        parameter.RefKind == RefKind.None
            ? NullableFlowFacts.HasNotNullPostcondition(parameter) ||
              NullableFlowFacts.HasInferredNotNullNormalCompletionPostcondition(parameter, cancellationToken)
            : NullableFlowFacts.GetParameterOutputState(parameter) == NullableFlowFactState.NotNull;

    private static bool ArgumentRefKindMatches(IParameterSymbol parameter, ArgumentSyntax argument) =>
        parameter.RefKind switch
        {
            RefKind.None => argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefKind.Ref => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword),
            RefKind.Out => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
            _ => false
        };

    private static bool IsUniqueOutputArgumentTarget(
        IInvocationOperation invocation,
        IArgumentOperation argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (argument.Syntax is not ArgumentSyntax syntax ||
            !NullableFlowFacts.TryGetArgumentTargetSymbol(
                syntax.Expression,
                semanticModel,
                cancellationToken,
                out var target))
            return false;

        foreach (var other in invocation.Arguments)
            if (!ReferenceEquals(argument, other) &&
                other.Syntax is ArgumentSyntax otherSyntax &&
                NullableFlowFacts.TryGetArgumentTargetSymbol(
                    otherSyntax.Expression,
                    semanticModel,
                    cancellationToken,
                    out var otherTarget) &&
                SymbolEqualityComparer.Default.Equals(target, otherTarget))
                return false;
        return true;
    }

    internal static ExpressionSyntax UnwrapAwaited(ExpressionSyntax expression)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        return expression is AwaitExpressionSyntax awaited
            ? CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(awaited.Expression)
            : expression;
    }

    private static SymbolicLoweringResult<SymbolicFrameworkPostconditionPlan> Exact(
        SyntaxNode source,
        ImmutableArray<SymbolicCondition> before,
        ImmutableArray<SymbolicCondition> after) =>
        SymbolicLoweringResult<SymbolicFrameworkPostconditionPlan>.Exact(
            new SymbolicFrameworkPostconditionPlan(before, after),
            new SymbolicLoweringProvenance("framework-postconditions", source.Span, "exact"));
}
