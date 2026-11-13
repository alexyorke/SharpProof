namespace SharpProof.Worker;

internal static partial class CallableVerificationPolicy
{
    internal static async Task<CallableVerificationResult> VerifyNativeTargetAsync(
        /* SPINT001: all values valid - Null represents an unavailable backend for native verification. */ ISmtBackend backend, CompilerCallablePreparation target, WorkerBudgets budgets,
        Func<long>? readConsumedResourceCount, int methodWallTimeMilliseconds,
        CancellationTokenSource projectBoundary, CancellationToken callerCancellation,
        Action<long>? /* SPINT001: all values valid - null skips cost measurement */ reportConsumedResources = null)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        ArgumentNullGuard.NotNull(projectBoundary, nameof(projectBoundary));
        ArgumentOutOfRangeException.ThrowIfNegative(methodWallTimeMilliseconds);
        if (callerCancellation.IsCancellationRequested)
        { return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled); }
        using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectBoundary.Token, callerCancellation);
        methodBoundary.CancelAfter(methodWallTimeMilliseconds);
        var completed = ImmutableDictionary.CreateBuilder<string, WorkerClaimResult>(StringComparer.Ordinal);
        var pending = ImmutableDictionary.CreateBuilder<string, WorkerClaimResult>(StringComparer.Ordinal);
        var effects = ImmutableDictionaryCapture.Create(target.EffectClaims, evidence => evidence.ClaimId, static evidence => evidence, StringComparer.Ordinal);
        var calls = NativeCallPreconditionClaims.ClaimIds(target);
        var hasPostconditions = target.Entry.ClaimIds.Any(id => !effects.ContainsKey(id) && !calls.Contains(id));
        var entryPublished = false;
        var entryFeasibility = CallableEntryFeasibility.Feasible;
        MethodResourceBudget? measuredBudget = null;
        try
        {
            var resourceBudget = measuredBudget = new MethodResourceBudget(readConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
            if (hasPostconditions && target.Total != null)
            {
                await TotalCallableVerifier.VerifyAsync(target, budgets, check =>
                {
                    System.ArgumentNullException.ThrowIfNull(check);
                    var result = CallableClaimResultAssembler.FromTotal(target, check);
                    if (check.Checked)
                    { completed[check.ClaimId] = result; }
                    else
                    { pending[check.ClaimId] = result; }
                }, PublishEntry, methodBoundary.Token, backend, resourceBudget, readConsumedResourceCount).ConfigureAwait(false);
            }
            if (!entryPublished)
            {
                var entry = target.Entry.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.Precondition)
                    ? await TotalCallableVerifier.VerifyEntryAsync(target, budgets, methodBoundary.Token, backend, resourceBudget).ConfigureAwait(false)
                    : CallableEntryFeasibility.Feasible;
                PublishEntry(entry);
            }
            await NativeEffectClaims.VerifyAsync(target, entryFeasibility, budgets, methodBoundary.Token,
                result =>
                {
                    ArgumentNullException.ThrowIfNull(result);
                    completed[result.ClaimId] = result;
                }, resourceBudget).ConfigureAwait(false);
            await NativeCallPreconditionClaims.VerifyAsync(target, entryFeasibility, budgets,
                result =>
                {
                    ArgumentNullException.ThrowIfNull(result);
                    completed[result.ClaimId] = result;
                }, resourceBudget, methodBoundary.Token).ConfigureAwait(false);
            var records = Fill(target.IsSuccess ? WorkerClaimReason.UnsupportedBody : target.FailureReason);
            return Result(target, WorkerResultAssembler.ProjectCallableReasons(records).Reason, records);
        }
        catch (OperationCanceledException)
        {
            if (callerCancellation.IsCancellationRequested)
            { return Interrupted(WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled); }
            if (projectBoundary.IsCancellationRequested)
            { return Interrupted(WorkerClaimReason.ProjectTimeout, WorkerCallableCoverageReason.ProjectTimeout); }
            if (methodBoundary.IsCancellationRequested)
            { return Interrupted(WorkerClaimReason.MethodTimeout, WorkerCallableCoverageReason.MethodTimeout); }
            return Interrupted(WorkerClaimReason.InfrastructureFailure, WorkerCallableCoverageReason.InfrastructureFailure);
        }
        catch (AggregateException)
        { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Interrupted(WorkerHost.IsBackendUnavailable(exception) ? WorkerClaimReason.BackendUnavailable : WorkerClaimReason.InfrastructureFailure,
                WorkerCallableCoverageReason.InfrastructureFailure);
        }
        finally
        {
            ReportCost();
        }

        // Cost reporting is diagnostic; a counter that cannot be read leaves the cost unreported.
        void ReportCost()
        {
            if (measuredBudget == null || reportConsumedResources == null)
            { return; }
            long consumed;
            try
            { consumed = measuredBudget.MeasuredResourceCount; }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            { return; }
            reportConsumedResources(consumed);
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
