namespace SharpProof.Contracts;

// Rebinds compiler-bound clauses to caller-owned variables as one immutable
// result. A rejected substitution never exposes a partially rebound clause set.
internal static class ContractClauseSubstitution
{
    internal static ContractClauseSubstitutionResult Substitute(
        IrFactory factory,
        ImmutableArray<BoundContractClause> sourceClauses,
        IReadOnlyDictionary<IrVarId, IrTerm> substitutions)
    {
        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        foreach (var clause in sourceClauses)
        {
            IrTerm condition;
            try
            {
                condition = IrSubstitution.Substitute(factory, clause.Condition, substitutions);
            }
            catch (ArgumentException)
            {
                return new([], ContractBindingFailure.UnsupportedExpression);
            }
            clauses.Add(new BoundContractClause(
                clause.Kind,
                condition,
                clause.SourceOperation,
                clause.Evidence,
                clause.DiagnosticText,
                clause.SourceSyntax));
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None);
    }
}

internal readonly struct ContractClauseSubstitutionResult(
    ImmutableArray<BoundContractClause> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundContractClause> Clauses { get; } = clauses;
    internal ContractBindingFailure Failure { get; } = failure;
}
