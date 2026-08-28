namespace SharpProof.Verify;

public sealed class ProofKernel(ISmtBackend backend)
{
    private readonly ISmtBackend _backend =
        ArgumentNullGuard.NotNull(backend, nameof(backend));

    public async Task<ProofOutcome> VerifyAsync(VerificationQuery query,
        CancellationToken cancellationToken = default)
    {
        query = ArgumentNullGuard.NotNull(query, nameof(query));

        cancellationToken.ThrowIfCancellationRequested();
        BackendCheckResult? result;
        try
        {
            var backendTask = _backend.CheckAsync(query, cancellationToken);
            result = backendTask is null
                ? null
                : await backendTask.ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            result = null;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            // A backend must not be able to manufacture caller cancellation
            // with a task canceled by an unrelated token. Treat that task as
            // malformed and let the common validation below classify it.
            result = null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (result == null)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        var outcome = result.Status switch
        {
            BackendCheckStatus.Unsatisfiable => CreateProven(
                query, result, cancellationToken),
            BackendCheckStatus.Satisfiable => ReplayCounterexample(query, result, cancellationToken),
            BackendCheckStatus.Unknown => Unknown(
                VerificationProjections.MapFailure(result.FailureReason)),
            _ => Unknown(AbstentionReason.MalformedBackendResult)
        };
        cancellationToken.ThrowIfCancellationRequested();
        return outcome;
    }
    private static ProofOutcome CreateProven(
        VerificationQuery query,
        BackendCheckResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Model != null ||
            result.FailureReason != BackendFailureReason.None ||
            result.UnsatCore.IsDefault)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        var core = ImmutableArray.CreateBuilder<ProofJustification>();
        var seen = new HashSet<int>();
        foreach (var index in result.UnsatCore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index < 0 || index >= query.Assumptions.Length)
            {
                return Unknown(AbstentionReason.MalformedBackendResult);
            }

            if (seen.Add(index))
            {
                core.Add(query.Assumptions[index].Justification);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ProvenOutcome(core.ToImmutable());
    }
    private static ProofOutcome ReplayCounterexample(
        VerificationQuery query,
        BackendCheckResult result,
        CancellationToken cancellationToken)
    {
        if (result.Model == null ||
            result.FailureReason != BackendFailureReason.None ||
            !result.UnsatCore.IsDefaultOrEmpty)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        if (!ValidateAssignments(query, result.Model.Assignments))
        {
            return Unknown(AbstentionReason.CounterexampleReplayFailed);
        }

        var interpreter = new IrInterpreter(query.Factory);
        foreach (var assumption in query.Assumptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = interpreter.Evaluate(assumption.Predicate,
                result.Model.Assignments, cancellationToken);
            if (!IsBoolean(evaluated, expected: true))
            {
                return Unknown(AbstentionReason.CounterexampleReplayFailed);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var goal = interpreter.Evaluate(query.Goal.Predicate, result.Model.Assignments, cancellationToken);
        if (goal.Status == IrEvaluationStatus.Exception)
        {
            return Unknown(query.Goal.Diagnostic switch
            {
                ProofDiagnosticKind.InternalConsistency =>
                    AbstentionReason.InternalConsistencyMayBeUndefined,
                ProofDiagnosticKind.Postcondition =>
                    AbstentionReason.PostconditionMayBeUndefined,
                _ => AbstentionReason.CounterexampleReplayFailed
            });
        }

        return IsBoolean(goal, expected: false)
            ? new RefutedOutcome(new ValidatedModel(result.Model.Assignments))
            : Unknown(AbstentionReason.CounterexampleReplayFailed);
    }
    private static bool ValidateAssignments(VerificationQuery query,
        ImmutableDictionary<IrVarId, IrValue> assignments)
    {
        if (assignments.Count != query.ModelVariables.Length ||
            query.ModelVariables.Any(variable => !assignments.ContainsKey(variable)))
        {
            return false;
        }

        return assignments.All(IsValid);

        bool IsValid(KeyValuePair<IrVarId, IrValue> assignment)
        {
            if (assignment.Value == null)
            {
                return false;
            }

            try
            {
                var type = query.Factory.GetVariableInfo(assignment.Key).Type;
                return type == assignment.Value.Type &&
                    (type == query.Factory.BooleanType ||
                     type == query.Factory.IntegerType ||
                     type == query.Factory.StringType &&
                     assignment.Value.Kind is IrValueKind.String or IrValueKind.Null);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    private static bool IsBoolean(IrEvaluationResult result, bool expected)
    {
        return result.Status == IrEvaluationStatus.Value &&
        result.Value is { Kind: IrValueKind.Boolean } value &&
        value.Boolean == expected;
    }

    private static UnknownOutcome Unknown(AbstentionReason reason)
    {
        return new(reason);
    }
}
