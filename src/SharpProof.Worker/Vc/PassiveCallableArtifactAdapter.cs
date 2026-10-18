namespace SharpProof.Worker;

// Enrollment consumes the immutable preparation decoded with its manifest row.
// It never reparses a graph or introduces candidate evidence into worker routing.
internal static class PassiveCallableArtifactAdapter
{
    // Separate structural shadow enrollment; never creates a manifest row.
    internal static PassiveCallableCandidate EnrollShadow(CompilerDecodedShadowBody preparation)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        var total = preparation.Body;
        return new(total.CallableId, total.Program,
            [.. total.Parameters.Select(static parameter => ((parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(parameter)) : (new PassiveParameterBinding(parameter.Entry, parameter.Current, parameter.Old))))],
            total.Result, [], [], false,
            [.. total.CallPreconditions.Select(static call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (new PassiveCallPrecondition(call.Instruction, call.Value, call.Safe)))]);
    }

    internal static PassiveCallableCandidate? Enroll(CompilerCallablePreparation preparation)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        var total = preparation.Total;
        if (total == null)
        { return null; }
        if (total.CallableId != preparation.Entry.CallableId ||
            !total.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Ensures))
                .Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.ClaimId)).SequenceEqual(preparation.Entry.ClaimIds.Take(total.Clauses.Count(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Ensures))), StringComparer.Ordinal))
        { throw new ArgumentException("The Total preparation belongs to another manifest callable.", nameof(preparation)); }
        ImmutableArray<PassiveContractClause> Clauses(CompilerContractKind kind)
        {
            return [.. total.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == kind))
                .Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (new PassiveContractClause(clause.Value, clause.Safe, clause.Operation)))];
        }
        return new(preparation.Entry.CallableId, total.Program,
            [.. total.Parameters.Select(parameter => ((parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(parameter)) : (new PassiveParameterBinding(parameter.Entry, parameter.Current, parameter.Old))))],
            total.Result, Clauses(CompilerContractKind.Requires), Clauses(CompilerContractKind.Ensures), total.IsBodyAbstraction,
            [.. total.CallPreconditions.Select(call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (new PassiveCallPrecondition(call.Instruction, call.Value, call.Safe)))], total.DisjointInputs)
        { HasReceiver = total.HasReceiver };
    }
}
