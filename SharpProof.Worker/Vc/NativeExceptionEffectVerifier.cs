namespace SharpProof.Worker;

// Qualification entry point for declared native exception evidence. Compiler effect
// publication remains authoritative until the complete effect universe qualifies.
internal static class NativeExceptionEffectVerifier
{
    internal static async Task<PassiveCallableCheckResult> VerifyAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        var checks = await VerifyClaimsAsync(preparation, budgets, cancellationToken).ConfigureAwait(false);
        return checks.Count == 1 ? checks.Values.Single() : Unknown(WorkerClaimReason.UnsupportedContract);
    }

    internal static async Task<ImmutableDictionary<string, PassiveCallableCheckResult>> VerifyClaimsAsync(
        CompilerCallablePreparation preparation, WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        cancellationToken.ThrowIfCancellationRequested();
        var expected = preparation.EffectClaims.Where(claim => claim.ContractKind is
            WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions).ToArray();
        var results = ImmutableDictionary.CreateBuilder<string, PassiveCallableCheckResult>(StringComparer.Ordinal);
        if (expected.Length == 0)
        { return results.ToImmutable(); }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return All(Unknown(WorkerClaimReason.UnsupportedBody)); }
        var constraints = (preparation.Total?.ExceptionConstraints ?? []).ToDictionary(constraint => constraint.ClaimId, StringComparer.Ordinal);
        foreach (var claim in expected)
        {
            if (!constraints.ContainsKey(claim.ClaimId))
            { results.Add(claim.ClaimId, Unknown(WorkerClaimReason.UnsupportedContract)); }
        }
        if (results.Count == expected.Length)
        { return results.ToImmutable(); }
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        { return All(Unknown(WorkerClaimReason.UnsupportedExpression)); }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        { return All(Unknown(failure)); }
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return All(entry); }
        // Exception claims require a feasible entry, not a normal-return witness.
        foreach (var claim in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!results.ContainsKey(claim.ClaimId))
            { results.Add(claim.ClaimId, await solver.VerifyExceptionsAsync(constraints[claim.ClaimId].AllowedKinds.ToImmutableHashSet(), cancellationToken).ConfigureAwait(false)); }
        }
        return results.ToImmutable();

        ImmutableDictionary<string, PassiveCallableCheckResult> All(PassiveCallableCheckResult evidence)
        {
            foreach (var claim in expected)
            {
                if (!results.ContainsKey(claim.ClaimId))
                { results.Add(claim.ClaimId, evidence); }
            }
            return results.ToImmutable();
        }
    }

    private static PassiveCallableCheckResult Unknown(WorkerClaimReason reason)
    { return new(null, reason, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
}
