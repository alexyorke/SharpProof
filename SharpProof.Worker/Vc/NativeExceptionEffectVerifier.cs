namespace SharpProof.Worker;

// Native exception evidence for DoesNotThrow and AllowedExceptions claims;
// NativeEffectClaims publishes it.
internal static class NativeExceptionEffectVerifier
{
    internal static async Task<PassiveCallableCheckResult> VerifyAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(budgets);
        System.ArgumentNullException.ThrowIfNull(preparation);
        var checks = await VerifyClaimsAsync(preparation, budgets, cancellationToken).ConfigureAwait(false);
        return checks.Count == 1 ? checks.Values.Single() : Unknown(WorkerClaimReason.UnsupportedContract);
    }

    internal static async Task<ImmutableDictionary<string, PassiveCallableCheckResult>> VerifyClaimsAsync(
        CompilerCallablePreparation preparation, WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        cancellationToken.ThrowIfCancellationRequested();
        var expected = preparation.EffectClaims.Where(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ContractKind is
            WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions)).ToArray();
        var results = ImmutableDictionary.CreateBuilder<string, PassiveCallableCheckResult>(StringComparer.Ordinal);
        if (expected.Length == 0)
        { return results.ToImmutable(); }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return All(Unknown(WorkerClaimReason.UnsupportedBody)); }
        var constraints = (preparation.Total?.ExceptionConstraints ?? []).ToDictionary(constraint => constraint is null ? throw new System.ArgumentNullException(nameof(constraint)) : (constraint.ClaimId), StringComparer.Ordinal);
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
        using var solver = new PassiveCallableSolver(plan ?? throw new InvalidOperationException("Passive callable plan missing after successful build."), budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return All(entry); }
        // Exception claims require a feasible entry, not a normal-return witness.
        foreach (var claim in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!results.ContainsKey(claim.ClaimId))
            {
                var allowed = constraints[claim.ClaimId].AllowedKinds.ToImmutableHashSet();
                var anyThrowViolates = claim.ContractKind == WorkerEffectContractKind.DoesNotThrow;
                var allowedTypes = claim.Constraint.AllowedExceptionTypes.ToHashSet(StringComparer.Ordinal);
                var factory = candidate.Program.Factory;
                results.Add(claim.ClaimId, await solver.VerifyExceptionsAsync(allowed, anyThrowViolates,
                    site => site.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(site)) : (ExplicitThrowSites.Types(factory, site).Any(allowedTypes.Contains)),
                    site => site.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(site)) : (ExplicitThrowSites.IsExact(factory, site)), cancellationToken).ConfigureAwait(false));
            }
        }
        return results.ToImmutable();

        ImmutableDictionary<string, PassiveCallableCheckResult> All(PassiveCallableCheckResult evidence)
        {
            System.ArgumentNullException.ThrowIfNull(evidence);
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
