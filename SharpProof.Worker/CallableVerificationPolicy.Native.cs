namespace SharpProof.Worker;

internal static partial class CallableVerificationPolicy
{
    internal static async Task<CallableVerificationResult> VerifyNativeTargetAsync(
        ISmtBackend backend, CompilerCallablePreparation target, WorkerBudgets budgets,
        Func<long>? readConsumedResourceCount, int methodWallTimeMilliseconds,
        CancellationTokenSource projectBoundary, CancellationToken callerCancellation)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        ArgumentNullGuard.NotNull(projectBoundary, nameof(projectBoundary));
        ArgumentOutOfRangeException.ThrowIfNegative(methodWallTimeMilliseconds);
        if (callerCancellation.IsCancellationRequested)
        { return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled); }
        using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectBoundary.Token);
        var diagnosticBoundaryStarted = Stopwatch.GetTimestamp();
        methodBoundary.CancelAfter(methodWallTimeMilliseconds);
        var diagnostics = NativeStageDiagnostics.For(target.Entry, diagnosticBoundaryStarted,
            methodBoundary.Token, projectBoundary.Token, callerCancellation);
        using var callableStage = diagnostics?.Measure("callable", flushOnDispose: true);
        var completed = new Dictionary<string, WorkerClaimResult>(StringComparer.Ordinal);
        var pending = new Dictionary<string, WorkerClaimResult>(StringComparer.Ordinal);
        var effects = target.EffectClaims.ToDictionary(evidence => evidence.ClaimId, StringComparer.Ordinal);
        var hasPostconditions = target.Entry.ClaimIds.Any(id => !effects.ContainsKey(id));
        var entryPublished = false;
        var entryFeasibility = CallableEntryFeasibility.Feasible;
        try
        {
            var resourceBudget = new MethodResourceBudget(readConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
            if (hasPostconditions && target.Total != null)
            {
                using (diagnostics?.Measure("total-verification"))
                {
                    await TotalCallableVerifier.VerifyAsync(target, budgets, check =>
                    {
                        var result = CallableClaimResultAssembler.FromTotal(target, check);
                        if (check.Checked)
                        { completed[check.ClaimId] = result; }
                        else
                        { pending[check.ClaimId] = result; }
                    }, PublishEntry, methodBoundary.Token, backend, resourceBudget, readConsumedResourceCount).ConfigureAwait(false);
                }
            }
            if (!entryPublished)
            {
                CallableEntryFeasibility entry;
                using (diagnostics?.Measure("entry-feasibility"))
                {
                    entry = target.Entry.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.Precondition)
                        ? await TotalCallableVerifier.VerifyEntryAsync(target, budgets, methodBoundary.Token, backend, resourceBudget).ConfigureAwait(false)
                        : CallableEntryFeasibility.Feasible;
                }
                PublishEntry(entry);
            }
            using (diagnostics?.Measure("native-effects"))
            {
                foreach (var result in await NativeEffectClaims.VerifyAsync(target, entryFeasibility, budgets,
                    diagnostics, methodBoundary.Token).ConfigureAwait(false))
                { completed[result.ClaimId] = result; }
            }
            var records = Fill(target.IsSuccess ? WorkerClaimReason.UnsupportedBody : target.FailureReason);
            return Result(target, WorkerResultAssembler.ProjectCallableReasons(records).Reason, records);
        }
        catch (OperationCanceledException)
        {
            diagnostics?.Record("interruption", "exception=OperationCanceledException");
            if (callerCancellation.IsCancellationRequested)
            { diagnostics?.Record("interruption-result", "reason=Canceled"); return Interrupted(WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled); }
            if (projectBoundary.IsCancellationRequested)
            { diagnostics?.Record("interruption-result", "reason=ProjectTimeout"); return Interrupted(WorkerClaimReason.ProjectTimeout, WorkerCallableCoverageReason.ProjectTimeout); }
            if (methodBoundary.IsCancellationRequested)
            { diagnostics?.Record("interruption-result", "reason=MethodTimeout"); return Interrupted(WorkerClaimReason.MethodTimeout, WorkerCallableCoverageReason.MethodTimeout); }
            return Interrupted(WorkerClaimReason.InfrastructureFailure, WorkerCallableCoverageReason.InfrastructureFailure);
        }
        catch (AggregateException)
        { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return Interrupted(WorkerHost.IsBackendUnavailable(exception) ? WorkerClaimReason.BackendUnavailable : WorkerClaimReason.InfrastructureFailure,
                WorkerCallableCoverageReason.InfrastructureFailure);
        }

        void PublishEntry(CallableEntryFeasibility entry)
        {
            entryPublished = true;
            entryFeasibility = entry;
            foreach (var evidence in target.EffectClaims.Where(evidence => !NativeEffectClaims.IsNative(evidence.ContractKind) ||
                evidence.Certainty == WorkerEffectEvidenceCertainty.TrustedCompleteBoundary))
            {
                var result = EffectClaimResultAssembler.Assemble(target, evidence, entry, methodBoundary.Token);
                completed[result.ClaimId] = result;
            }
        }

        ImmutableArray<WorkerClaimResult> Fill(WorkerClaimReason reason, bool includePending = true)
        {
            var unknown = CallableClaimResultAssembler.Unknowns(target, reason);
            return [.. unknown.Select(result => completed.TryGetValue(result.ClaimId, out var published) ? published :
                includePending && pending.TryGetValue(result.ClaimId, out var observed) ? observed : result)];
        }

        CallableVerificationResult Interrupted(WorkerClaimReason claimReason, WorkerCallableCoverageReason callableReason)
        {
            var records = Fill(claimReason, includePending: false);
            return Result(target, completed.Count == target.Entry.ClaimIds.Length
                ? WorkerResultAssembler.ProjectCallableReasons(records).Reason : callableReason, records);
        }
    }
}
