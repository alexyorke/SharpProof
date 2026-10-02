namespace SharpProof.Worker;

// Qualification entry point for native DoesNotThrow evidence. Compiler effect
// publication remains authoritative until the complete effect universe qualifies.
internal static class NativeExceptionEffectVerifier
{
    internal static async Task<PassiveCallableCheckResult> VerifyAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        { return Unknown(WorkerClaimReason.UnsupportedExpression); }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        { return Unknown(failure); }
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is ProvenOutcome)
        { return entry; }
        if (entry.Outcome is not RefutedOutcome)
        { return entry; }
        // Exception claims require a feasible entry, not a normal-return witness.
        return await solver.VerifyExceptionsAsync([], cancellationToken).ConfigureAwait(false);
    }

    private static PassiveCallableCheckResult Unknown(WorkerClaimReason reason)
    { return new(null, reason, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
}
