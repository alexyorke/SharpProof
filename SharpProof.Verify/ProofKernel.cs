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
        var result = await _backend.CheckAsync(query, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result == null)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        return result.Status switch
        {
            BackendCheckStatus.Unsatisfiable => CreateProven(query, result),
            BackendCheckStatus.Satisfiable => ReplayCounterexample(query, result, cancellationToken),
            BackendCheckStatus.Unknown => Unknown(
                VerificationProjections.MapFailure(result.FailureReason)),
            _ => Unknown(AbstentionReason.MalformedBackendResult)
        };
    }
    private static ProofOutcome CreateProven(VerificationQuery query, BackendCheckResult result)
    {
        if (result.Model != null ||
            result.FailureReason != BackendFailureReason.None ||
            result.UnsatCore.IsDefault)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        if (result.UnsatCore.Any(index => index < 0 || index >= query.Assumptions.Length))
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        return new ProvenOutcome([.. result.UnsatCore.Distinct()
            .Select(index => query.Assumptions[index].Justification)]);
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
        if (goal.Status == IrEvaluationStatus.Exception &&
            query.Goal.Diagnostic == ProofDiagnosticKind.Postcondition)
        {
            return Unknown(AbstentionReason.PostconditionMayBeUndefined);
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
                    (type == query.Factory.BooleanType || type == query.Factory.IntegerType);
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
