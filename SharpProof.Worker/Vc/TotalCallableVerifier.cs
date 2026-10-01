using SharpProof.Host;

namespace SharpProof.Worker;

internal sealed record TotalCallableClaimCheck(string ClaimId, bool Enrolled, bool Checked,
    PassiveCallableCheckResult Evidence, PassiveCallableFeasibilityKind Feasibility,
    WorkerVacuityKind Vacuity, ImmutableArray<WorkerAssumptionEvidence> Assumptions);

// Owns one native session per callable. Publication is incremental so a later
// interruption cannot discard earlier kernel-validated claim results.
internal static class TotalCallableVerifier
{
    internal static async Task VerifyAsync(CompilerCallablePreparation preparation, WorkerBudgets budgets,
        Action<TotalCallableClaimCheck> publish, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        ArgumentNullGuard.NotNull(publish, nameof(publish));
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return; }
        var total = preparation.Total!;
        var ensures = total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Ensures).ToArray();
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var constructionReason, cancellationToken))
        {
            foreach (var clause in ensures)
            { PublishUnchecked(clause.ClaimId!, false, constructionReason); }
            return;
        }
        foreach (var clause in ensures)
        { PublishUnchecked(clause.ClaimId!, true, WorkerClaimReason.UnsupportedBody); }
        cancellationToken.ThrowIfCancellationRequested();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var feasibility = await solver.VerifyFeasibilityAsync(cancellationToken).ConfigureAwait(false);
        var requiresByLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        var requiresOrdinal = 0;
        foreach (var clause in total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Requires))
        {
            cancellationToken.ThrowIfCancellationRequested();
            requiresByLabel.Add("requires:" + (requiresOrdinal++).ToString(CultureInfo.InvariantCulture), clause.AssumptionId!);
        }
        var canonicalAssumptions = preparation.Entry.Assumptions.OrderBy(assumption => assumption.Id, StringComparer.Ordinal).ToArray();
        var assumesByOperation = total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Assume)
            .ToDictionary(clause => clause.Operation, clause => clause.AssumptionId!);
        for (var ordinal = 0; ordinal < ensures.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimId = ensures[ordinal].ClaimId!;
            if (feasibility.Kind == PassiveCallableFeasibilityKind.Unknown && !solver.CanCheckWithoutNormalWitness)
            {
                publish(new(claimId, true, false, feasibility.Evidence, feasibility.Kind, WorkerVacuityKind.None, []));
                continue;
            }
            var vacuity = feasibility.Kind switch
            {
                PassiveCallableFeasibilityKind.ContradictoryEntry => WorkerVacuityKind.ContradictoryPreconditions,
                PassiveCallableFeasibilityKind.NoModeledNormalReturn => WorkerVacuityKind.NoModeledNormalReturn,
                _ => WorkerVacuityKind.None
            };
            var evidence = vacuity == WorkerVacuityKind.None
                ? await solver.VerifyEnsuresAsync(ordinal, cancellationToken).ConfigureAwait(false)
                : feasibility.Evidence;
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var label in evidence.Core)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (requiresByLabel.TryGetValue(label, out var id))
                { used.Add(id); }
            }
            foreach (var operation in evidence.BodyAssumptions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (assumesByOperation.TryGetValue(operation, out var id))
                { used.Add(id); }
            }
            var assumptions = ImmutableArray.CreateBuilder<WorkerAssumptionEvidence>(canonicalAssumptions.Length);
            foreach (var assumption in canonicalAssumptions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                assumptions.Add(new() { Id = assumption.Id, Kind = assumption.Kind, Used = used.Contains(assumption.Id) });
            }
            publish(new(claimId, true, evidence.Outcome != null || evidence.QueryCompleted, evidence,
                feasibility.Kind, vacuity, assumptions.MoveToImmutable()));
        }

        void PublishUnchecked(string claimId, bool enrolled, WorkerClaimReason reason)
        {
            cancellationToken.ThrowIfCancellationRequested();
            publish(new(claimId, enrolled, false,
                new(null, reason, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []),
                PassiveCallableFeasibilityKind.Unknown, WorkerVacuityKind.None, []));
        }
    }
}
