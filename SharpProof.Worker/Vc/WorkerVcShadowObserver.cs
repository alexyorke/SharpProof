using SharpProof.Host;

namespace SharpProof.Worker;

internal static class WorkerVcShadowObserver
{
    internal static async Task<WorkerVcShadowReport> ObserveAsync(WorkerVerifyResponse legacy,
        ImmutableArray<CompilerCallablePreparation> preparations, WorkerBudgets budgets,
        CancellationToken projectCancellation, CancellationToken callerCancellation)
    {
        var old = legacy.ClaimResults.ToDictionary(claim => claim.ClaimId, StringComparer.Ordinal);
        var owned = preparations.ToDictionary(preparation => preparation.Entry.CallableId, StringComparer.Ordinal);
        var rows = new Dictionary<string, WorkerVcShadowRow>(StringComparer.Ordinal);
        foreach (var claim in legacy.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition))
        {
            var preparation = owned[claim.CallableId];
            var authoritative = old[claim.ClaimId];
            rows.Add(claim.ClaimId, new(claim.CallableId, claim.ClaimId, authoritative.Outcome, authoritative.Vacuity,
                Assumptions(authoritative.Assumptions), preparation.Total != null, false, false,
                WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerVacuityKind.None,
                PassiveCallableFeasibilityKind.Unknown, [], false));
        }
        foreach (var preparation in preparations.OrderBy(item => item.Entry.CallableId, StringComparer.Ordinal))
        {
            var claimIds = preparation.Entry.ClaimIds.Where(rows.ContainsKey).ToArray();
            if (claimIds.Length == 0)
            { continue; }
            var interruption = Interruption(projectCancellation, callerCancellation, CancellationToken.None);
            if (interruption != WorkerClaimReason.None)
            { SetReason(claimIds, interruption); continue; }
            await ObserveCallable(preparation, claimIds).ConfigureAwait(false);
        }
        return new(legacy.InputHash, legacy.RequestHash, legacy.Summary.CacheStatus,
            [.. rows.Values.OrderBy(row => row.CallableId, StringComparer.Ordinal).ThenBy(row => row.ClaimId, StringComparer.Ordinal)]);

        async Task ObserveCallable(CompilerCallablePreparation preparation, string[] claimIds)
        {
            using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectCancellation, callerCancellation);
            try
            {
                methodBoundary.CancelAfter(budgets.MethodWallTimeMilliseconds);
                var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
                if (candidate == null)
                { return; }
                if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var constructionReason, methodBoundary.Token))
                { SetReason(claimIds, constructionReason); return; }
                foreach (var claimId in claimIds)
                { rows[claimId] = rows[claimId] with { Enrolled = true }; }
                methodBoundary.Token.ThrowIfCancellationRequested();
                ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
                using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
                var feasibility = await solver.VerifyFeasibilityAsync(methodBoundary.Token).ConfigureAwait(false);
                var ensures = preparation.Total!.Clauses.Where(clause => clause.Kind == CompilerContractKind.Ensures).ToArray();
                var requiresByLabel = new Dictionary<string, string>(StringComparer.Ordinal);
                var requiresOrdinal = 0;
                foreach (var clause in preparation.Total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Requires))
                {
                    methodBoundary.Token.ThrowIfCancellationRequested();
                    requiresByLabel.Add("requires:" + (requiresOrdinal++).ToString(CultureInfo.InvariantCulture), clause.AssumptionId!);
                }
                var canonicalAssumptions = preparation.Entry.Assumptions.OrderBy(assumption => assumption.Id, StringComparer.Ordinal).ToArray();
                var assumesByOperation = preparation.Total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Assume)
                    .ToDictionary(clause => clause.Operation, clause => clause.AssumptionId!);
                for (var ordinal = 0; ordinal < ensures.Length; ordinal++)
                {
                    methodBoundary.Token.ThrowIfCancellationRequested();
                    var claimId = ensures[ordinal].ClaimId!;
                    if (feasibility.Kind == PassiveCallableFeasibilityKind.Unknown && !solver.CanCheckWithoutNormalWitness)
                    {
                        rows[claimId] = rows[claimId] with { Feasibility = feasibility.Kind, NewReason = feasibility.Evidence.Reason };
                        continue;
                    }
                    var vacuity = feasibility.Kind switch
                    {
                        PassiveCallableFeasibilityKind.ContradictoryEntry => WorkerVacuityKind.ContradictoryPreconditions,
                        PassiveCallableFeasibilityKind.NoModeledNormalReturn => WorkerVacuityKind.NoModeledNormalReturn,
                        _ => WorkerVacuityKind.None
                    };
                    var evidence = vacuity == WorkerVacuityKind.None
                        ? await solver.VerifyEnsuresAsync(ordinal, methodBoundary.Token).ConfigureAwait(false)
                        : feasibility.Evidence;
                    var outcome = evidence.Outcome switch
                    {
                        ProvenOutcome => WorkerClaimOutcome.Proven,
                        RefutedOutcome => WorkerClaimOutcome.Refuted,
                        _ => WorkerClaimOutcome.Unknown
                    };
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var label in evidence.Core)
                    {
                        methodBoundary.Token.ThrowIfCancellationRequested();
                        if (requiresByLabel.TryGetValue(label, out var id))
                        { used.Add(id); }
                    }
                    foreach (var operation in evidence.BodyAssumptions)
                    {
                        methodBoundary.Token.ThrowIfCancellationRequested();
                        if (assumesByOperation.TryGetValue(operation, out var id))
                        { used.Add(id); }
                    }
                    var assumptions = ImmutableArray.CreateBuilder<WorkerVcShadowAssumption>(canonicalAssumptions.Length);
                    foreach (var assumption in canonicalAssumptions)
                    {
                        methodBoundary.Token.ThrowIfCancellationRequested();
                        assumptions.Add(new(assumption.Id, assumption.Kind, used.Contains(assumption.Id)));
                    }
                    rows[claimId] = rows[claimId] with
                    {
                        Checked = evidence.Outcome != null || evidence.QueryCompleted,
                        NewOutcome = outcome,
                        NewReason = evidence.Reason,
                        NewVacuity = vacuity,
                        Feasibility = feasibility.Kind,
                        NewAssumptions = assumptions.MoveToImmutable(),
                        HasBodyAssumptions = !evidence.BodyAssumptions.IsEmpty
                    };
                }
            }
            catch (OperationCanceledException)
            {
                var reason = Interruption(projectCancellation, callerCancellation, methodBoundary.Token);
                SetReason(claimIds, reason == WorkerClaimReason.None ? WorkerClaimReason.InfrastructureFailure : reason);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                SetReason(claimIds, WorkerHost.IsBackendUnavailable(exception) ? WorkerClaimReason.BackendUnavailable : WorkerClaimReason.InfrastructureFailure);
            }
        }

        void SetReason(IEnumerable<string> claims, WorkerClaimReason reason)
        {
            foreach (var claimId in claims)
            {
                if (!rows[claimId].Checked)
                { rows[claimId] = rows[claimId] with { NewReason = reason }; }
            }
        }
    }

    private static ImmutableArray<WorkerVcShadowAssumption> Assumptions(IEnumerable<WorkerAssumptionEvidence> assumptions)
    {
        return [.. assumptions.OrderBy(assumption => assumption.Id, StringComparer.Ordinal)
            .Select(assumption => new WorkerVcShadowAssumption(assumption.Id, assumption.Kind, assumption.Used))];
    }

    private static WorkerClaimReason Interruption(CancellationToken project, CancellationToken caller, CancellationToken method)
    {
        return caller.IsCancellationRequested ? WorkerClaimReason.Canceled : project.IsCancellationRequested ? WorkerClaimReason.ProjectTimeout
            : method.IsCancellationRequested ? WorkerClaimReason.MethodTimeout : WorkerClaimReason.None;
    }
}
