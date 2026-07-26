namespace SharpProof.Verify;

public enum BackendCheckStatus {
    Unsatisfiable,
    Satisfiable,
    Unknown
}

public enum BackendFailureReason {
    None,
    UnsupportedEncoding,
    ResourceLimit,
    Timeout,
    Unavailable,
    MalformedResult,
    InfrastructureFailure
}

public sealed class BackendModel {
    public BackendModel(IEnumerable<KeyValuePair<IrVarId, IrValue>> assignments) {
        if (assignments == null) throw new ArgumentNullException(nameof(assignments));
        Assignments = assignments.ToImmutableDictionary(
            static assignment => assignment.Key,
            static assignment => assignment.Value);
    }

    public ImmutableDictionary<IrVarId, IrValue> Assignments { get; }
}

public sealed class BackendCheckResult {
    private BackendCheckResult(
        BackendCheckStatus status,
        ImmutableArray<int> unsatCore,
        BackendModel? model,
        BackendFailureReason failureReason) {
        Status = status;
        UnsatCore = unsatCore;
        Model = model;
        FailureReason = failureReason;
    }

    public BackendCheckStatus Status { get; }
    public ImmutableArray<int> UnsatCore { get; }
    public BackendModel? Model { get; }
    public BackendFailureReason FailureReason { get; }

    public static BackendCheckResult Unsatisfiable(IEnumerable<int> assumptionIndices) {
        if (assumptionIndices == null) throw new ArgumentNullException(nameof(assumptionIndices));
        return new BackendCheckResult(
            BackendCheckStatus.Unsatisfiable,
            [.. assumptionIndices],
            null,
            BackendFailureReason.None);
    }

    public static BackendCheckResult Satisfiable(BackendModel model) =>
        new(
            BackendCheckStatus.Satisfiable,
            [],
            model ?? throw new ArgumentNullException(nameof(model)),
            BackendFailureReason.None);

    public static BackendCheckResult Unknown(BackendFailureReason reason) {
        if (reason == BackendFailureReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));
        return new BackendCheckResult(BackendCheckStatus.Unknown, [], null, reason);
    }
}

public interface ISmtBackend {
    Task<BackendCheckResult> CheckAsync(
        VerificationQuery query,
        CancellationToken cancellationToken);
}

public sealed class VerificationQuery {
    public VerificationQuery(
        IrFactory factory,
        IEnumerable<Assumption> assumptions,
        Goal goal) {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (assumptions == null) throw new ArgumentNullException(nameof(assumptions));
        Assumptions = [.. assumptions];
        if (Assumptions.Any(static assumption => assumption == null))
            throw new ArgumentException("Assumptions cannot contain null.", nameof(assumptions));
        Goal = goal ?? throw new ArgumentNullException(nameof(goal));
        foreach (var assumption in Assumptions)
            FactoryGuards.RequireBooleanTerm(factory, assumption.Predicate, nameof(assumptions));
        FactoryGuards.RequireBooleanTerm(factory, goal.Predicate, nameof(goal));
    }

    public IrFactory Factory { get; }
    public ImmutableArray<Assumption> Assumptions { get; }
    public Goal Goal { get; }
}
