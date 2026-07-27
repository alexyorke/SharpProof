namespace SharpProof.Verify;

public sealed class ProofKernel(ISmtBackend backend) {
    private readonly ISmtBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    public async Task<ProofOutcome> VerifyAsync(VerificationQuery query,
        CancellationToken cancellationToken = default) {
        if (query == null) throw new ArgumentNullException(nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _backend.CheckAsync(query, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result == null) return Unknown(AbstentionReason.MalformedBackendResult);
        return result.Status switch {
            BackendCheckStatus.Unsatisfiable => CreateProven(query, result),
            BackendCheckStatus.Satisfiable => ReplayCounterexample(query, result, cancellationToken),
            BackendCheckStatus.Unknown => Unknown(MapFailure(result.FailureReason)),
            _ => Unknown(AbstentionReason.MalformedBackendResult)
        };
    }

    private static ProofOutcome CreateProven(VerificationQuery query, BackendCheckResult result) {
        if (result.Model != null ||
            result.FailureReason != BackendFailureReason.None ||
            result.UnsatCore.IsDefault)
            return Unknown(AbstentionReason.MalformedBackendResult);
        var seen = new HashSet<int>();
        var core = ImmutableArray.CreateBuilder<ProofJustification>();
        foreach (var index in result.UnsatCore) {
            if (index < 0 || index >= query.Assumptions.Length)
                return Unknown(AbstentionReason.MalformedBackendResult);
            if (seen.Add(index)) core.Add(query.Assumptions[index].Justification);
        }
        return new ProvenOutcome(core.ToImmutable());
    }

    private static ProofOutcome ReplayCounterexample(VerificationQuery query, BackendCheckResult result, CancellationToken cancellationToken) {
        if (result.Model == null ||
            result.FailureReason != BackendFailureReason.None ||
            !result.UnsatCore.IsDefaultOrEmpty)
            return Unknown(AbstentionReason.MalformedBackendResult);
        if (!ValidateAssignments(query, result.Model.Assignments))
            return Unknown(AbstentionReason.CounterexampleReplayFailed);
        var interpreter = new IrInterpreter(query.Factory);
        foreach (var assumption in query.Assumptions) {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = interpreter.Evaluate(assumption.Predicate,
                result.Model.Assignments, cancellationToken);
            if (!IsBoolean(evaluated, expected: true))
                return Unknown(AbstentionReason.CounterexampleReplayFailed);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var goal = interpreter.Evaluate(query.Goal.Predicate, result.Model.Assignments, cancellationToken);
        return IsBoolean(goal, expected: false)
            ? new RefutedOutcome(new ValidatedModel(result.Model.Assignments))
            : Unknown(AbstentionReason.CounterexampleReplayFailed);
    }

    private static bool ValidateAssignments(VerificationQuery query,
        ImmutableDictionary<IrVarId, IrValue> assignments) {
        if (assignments.Count != query.ModelVariables.Length ||
            query.ModelVariables.Any(variable => !assignments.ContainsKey(variable)))
            return false;
        foreach (var assignment in assignments) {
            if (assignment.Value == null) return false;
            try {
                var type = query.Factory.GetVariableInfo(assignment.Key).Type;
                if (type != assignment.Value.Type ||
                    type != query.Factory.BooleanType && type != query.Factory.IntegerType)
                    return false;
            }
            catch (ArgumentException) {
                return false;
            }
        }
        return true;
    }

    private static bool IsBoolean(IrEvaluationResult result, bool expected) =>
        result.Status == IrEvaluationStatus.Value &&
        result.Value is { Kind: IrValueKind.Boolean } value &&
        value.Boolean == expected;

    private static AbstentionReason MapFailure(BackendFailureReason reason) => reason switch {
        BackendFailureReason.UnsupportedEncoding => AbstentionReason.UnsupportedEncoding,
        BackendFailureReason.ResourceLimit => AbstentionReason.ResourceLimit,
        BackendFailureReason.Timeout => AbstentionReason.Timeout,
        BackendFailureReason.Unavailable => AbstentionReason.BackendUnavailable,
        BackendFailureReason.InfrastructureFailure => AbstentionReason.InfrastructureFailure,
        BackendFailureReason.MalformedResult or BackendFailureReason.None =>
            AbstentionReason.MalformedBackendResult,
        _ => AbstentionReason.MalformedBackendResult
    };

    private static UnknownOutcome Unknown(AbstentionReason reason) => new(reason);
}
