namespace SharpProof.Verify;

public enum BackendCheckStatus
{
    Unsatisfiable,
    Satisfiable,
    Unknown
}

public enum BackendFailureReason
{
    None,
    UnsupportedEncoding,
    ResourceLimit,
    Timeout,
    Unavailable,
    MalformedResult,
    InfrastructureFailure
}

public sealed partial class BackendModel
{
    public BackendModel(IEnumerable<KeyValuePair<IrVarId, IrValue>> assignments)
        : this(
            ArgumentNullGuard.NotNull(assignments, nameof(assignments)).ToImmutableDictionary(
                static assignment => assignment.Key,
                static assignment => assignment.Value),
            default)
    {
    }
}

public sealed partial class BackendCheckResult
{
    public static BackendCheckResult Unsatisfiable(IEnumerable<int> assumptionIndices)
    {
        assumptionIndices = ArgumentNullGuard.NotNull(assumptionIndices, nameof(assumptionIndices));

        return new BackendCheckResult(BackendCheckStatus.Unsatisfiable,
            [.. assumptionIndices], null, BackendFailureReason.None, default);
    }

    public static BackendCheckResult Satisfiable(BackendModel model)
    {
        return new(BackendCheckStatus.Satisfiable, [],
            ArgumentNullGuard.NotNull(model, nameof(model)),
            BackendFailureReason.None,
            default);
    }

    public static BackendCheckResult Unknown(BackendFailureReason reason)
    {
        if (reason == BackendFailureReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new BackendCheckResult(
            BackendCheckStatus.Unknown,
            [],
            null,
            reason,
            default);
    }
}

public interface ISmtBackend
{
    Task<BackendCheckResult> CheckAsync(
        VerificationQuery query,
        CancellationToken cancellationToken);
}

public sealed class VerificationQuery
{
    public VerificationQuery(
        IrFactory factory,
        IEnumerable<Assumption> assumptions,
        Goal goal,
        ImmutableArray<IrVarId> modelVariables = default)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        assumptions = ArgumentNullGuard.NotNull(assumptions, nameof(assumptions));
        goal = ArgumentNullGuard.NotNull(goal, nameof(goal));
        Factory = factory;
        Assumptions = [.. assumptions];
        if (Assumptions.Any(static assumption => assumption == null))
        {
            throw new ArgumentException("Assumptions cannot contain null.", nameof(assumptions));
        }

        Goal = goal;
        foreach (var assumption in Assumptions)
        {
            FactoryGuards.RequireBooleanTerm(factory, assumption.Predicate, nameof(assumptions));
        }

        FactoryGuards.RequireBooleanTerm(factory, goal.Predicate, nameof(goal));
        if (modelVariables.IsDefault)
        {
            modelVariables = [];
        }

        if (modelVariables.Distinct().Count() != modelVariables.Length)
        {
            throw new ArgumentException("Model variables cannot contain duplicates.", nameof(modelVariables));
        }

        foreach (var variable in modelVariables)
        {
            factory.GetVariableInfo(variable);
        }

        ModelVariables = [.. IrTraversal.CollectVariables(
                Assumptions
                    .Select(static assumption => assumption.Predicate)
                    .Append(goal.Predicate))
            .Concat(modelVariables)
            .Distinct()
            .OrderBy(static variable => variable.Value)];
    }

    public IrFactory Factory
    {
        get;
    }
    public ImmutableArray<Assumption> Assumptions
    {
        get;
    }
    public Goal Goal
    {
        get;
    }
    public ImmutableArray<IrVarId> ModelVariables
    {
        get;
    }
}
