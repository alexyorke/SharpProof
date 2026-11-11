namespace SharpProof.Worker;

// Decides each call-precondition claim of a lowered body. A proof covers every
// occurrence of the call; a refutation is a concretely replayed false marker.
internal static class NativeCallPreconditionClaims
{
    internal static ImmutableHashSet<string> ClaimIds(CompilerCallablePreparation target)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        return [.. (target.Total?.CallPreconditions ?? []).Select(static row => row.ClaimId)
            .OfType<string>()];
    }

    internal static async Task VerifyAsync(CompilerCallablePreparation target, CallableEntryFeasibility entry, WorkerBudgets budgets,
        Action<WorkerClaimResult> publish, MethodResourceBudget resourceBudget, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        ArgumentNullGuard.NotNull(entry, nameof(entry));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        ArgumentNullGuard.NotNull(publish, nameof(publish));
        ArgumentNullGuard.NotNull(resourceBudget, nameof(resourceBudget));
        var rows = (target.Total?.CallPreconditions ?? []).Select(static (row, ordinal) => row is null ? throw new ArgumentNullException(nameof(row)) : (Row: row, Ordinal: ordinal))
            .Where(static item => item.Row.ClaimId != null).ToImmutableArray();
        if (rows.IsEmpty)
        { return; }
        var total = target.Total!;
        if (entry.IsContradictory)
        {
            foreach (var (row, _) in rows)
            {
                publish(CallableClaimResultAssembler.Contradictory(target, row.ClaimId!, WorkerEffectEvidenceCertainty.Unspecified,
                    entry.ProofCore, entry.UsedAssumptionIds));
            }
            return;
        }
        if (entry.IsUnknown)
        {
            PublishUnknown(entry.Reason);
            return;
        }
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        {
            PublishUnknown(WorkerClaimReason.UnsupportedExpression);
            return;
        }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        {
            PublishUnknown(failure);
            return;
        }
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit, resourceBudget);
        foreach (var (row, ordinal) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IrTermAnalysis.GetDepth(row.Value) > budgets.MaximumExpressionDepth ||
                IrTermAnalysis.GetDepth(row.Safe) > budgets.MaximumExpressionDepth)
            {
                publish(Unknown(target, row.ClaimId!, WorkerClaimReason.UnsupportedExpression));
                continue;
            }
            publish(Project(target, row.ClaimId!, await solver.VerifyCallPreconditionAsync(ordinal, cancellationToken).ConfigureAwait(false)));
        }

        void PublishUnknown(WorkerClaimReason reason)
        {
            foreach (var (row, _) in rows)
            { publish(Unknown(target, row.ClaimId!, reason)); }
        }
    }

    private static WorkerClaimResult Project(CompilerCallablePreparation target, string claimId, PassiveCallableCheckResult check)
    {
        System.ArgumentNullException.ThrowIfNull(check);
        System.ArgumentNullException.ThrowIfNull(claimId);
        if (check.Outcome is ProvenOutcome && check.Reason == WorkerClaimReason.None)
        {
            var proven = CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Proven, WorkerClaimReason.None,
                WorkerEffectEvidenceCertainty.Unspecified, projectAssumptions: false);
            return proven with
            {
                ProofCore = [.. check.Core.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(target, NativeEffectClaims.UsedAssumptions(target, check))
            };
        }
        if (check.Outcome is not RefutedOutcome || check.Reason != WorkerClaimReason.None || check.CallPreconditionWitness == null)
        { return Unknown(target, claimId, check.Outcome is RefutedOutcome ? WorkerClaimReason.CounterexampleNotReplayable : check.Reason); }
        // Replay starts past any type or module initializer, which could
        // change the state the call reads or keep it from running at all.
        var total = target.Total!;
        if (!total.EffectsCompleteAtEntry)
        { return Unknown(target, claimId, WorkerClaimReason.CounterexampleNotReplayable); }
        if (CallableClaimResultAssembler.ProjectModel(total, check.EntryModel) is not { } model)
        { return Unknown(target, claimId, WorkerClaimReason.MalformedBackendResult); }
        return CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Refuted, WorkerClaimReason.None,
            WorkerEffectEvidenceCertainty.Unspecified) with
        { Model = model };
    }

    private static WorkerClaimResult Unknown(CompilerCallablePreparation target, string claimId, WorkerClaimReason reason)
    {
        System.ArgumentNullException.ThrowIfNull(target);
        System.ArgumentNullException.ThrowIfNull(claimId);
        return CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Unknown,
            reason == WorkerClaimReason.None ? WorkerClaimReason.SolverIncomplete : reason, WorkerEffectEvidenceCertainty.Unspecified);
    }
}
