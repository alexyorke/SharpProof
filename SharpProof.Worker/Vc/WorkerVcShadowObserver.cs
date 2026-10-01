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
                await TotalCallableVerifier.VerifyAsync(preparation, budgets, check =>
                {
                    var result = CallableClaimResultAssembler.FromTotal(preparation, check);
                    rows[check.ClaimId] = rows[check.ClaimId] with
                    {
                        Enrolled = check.Enrolled,
                        Checked = check.Checked,
                        NewOutcome = result.Outcome,
                        NewReason = result.Reason,
                        NewVacuity = result.Vacuity,
                        Feasibility = check.Feasibility,
                        NewAssumptions = check.Assumptions.IsEmpty ? [] : Assumptions(result.Assumptions),
                        HasBodyAssumptions = !check.Evidence.BodyAssumptions.IsEmpty
                    };
                }, methodBoundary.Token).ConfigureAwait(false);
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
