namespace SharpProof.Contracts;

// Binds raw compiler-owned invocation candidates with a requested clause kind.
// Canonical API recognition, callable ownership, placement and intrinsic
// enrollment remain the caller's responsibility. These inputs are not enrolled
// inventory records or trusted successful lowering captures.
internal static class ContractInvocationMaterialization
{
    internal static ContractInvocationMaterializationResult Bind(
        IrFactory factory,
        ContractExpressionBinder expressionBinder,
        ImmutableArray<ContractInvocationCandidate> candidates,
        bool usesCompanion,
        bool requiresOnly)
    {

        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        expressionBinder = ArgumentNullGuard.NotNull(expressionBinder, nameof(expressionBinder));
        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        foreach (var candidate in candidates)
        {
            if (requiresOnly && candidate.Kind != BoundContractKind.Requires)
            {
                continue;
            }
            var invocation = candidate.Invocation;
            if (invocation.Arguments.Length != 1)
            {
                return new([], ContractBindingFailure.InvalidIntrinsicSignature);
            }
            var expression = expressionBinder.Bind(invocation.Arguments[0].Value);
            if (!expression.IsSuccess)
            {
                return new([], expression.Failure);
            }
            if (expression.Term!.Type != factory.BooleanType)
            {
                return new([], ContractBindingFailure.NonBooleanCondition);
            }
            clauses.Add(new BoundContractClause(
                candidate.Kind, expression.Term,
                factory.CreateOperation("contract@" + invocation.Syntax.SpanStart.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                usesCompanion ? BoundContractEvidence.Companion : BoundContractEvidence.CompilerBoundInvocation,
                FormatDiagnosticSourceText(invocation.Arguments[0].Value.Syntax),
                invocation.Syntax.GetReference()));
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    internal static string FormatDiagnosticSourceText(SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        return syntax.Span.Length > ClosedContractAttributeBinding.MaximumDiagnosticConditionLength
            ? "[condition exceeds the display limit]"
            : syntax.ToString();
    }
}

internal readonly struct ContractInvocationCandidate(
    BoundContractKind kind,
    IInvocationOperation invocation)
{
    internal BoundContractKind Kind { get; } = kind;
    internal IInvocationOperation Invocation { get; } =
        ArgumentNullGuard.NotNull(invocation, nameof(invocation));
}

internal sealed class ContractInvocationMaterializationResult(
    ImmutableArray<BoundContractClause> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundContractClause> Clauses { get; } = (clauses.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(clauses)) : clauses);
    internal ContractBindingFailure Failure { get; } = failure;
    internal static ContractInvocationMaterializationResult Empty { get; } = new([], ContractBindingFailure.None);
}
