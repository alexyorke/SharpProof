#nullable enable
namespace SharpProof.Verify;

internal sealed partial class BackendModel
{
    public ImmutableDictionary<IrVarId, IrValue> Assignments { get; }
}

internal sealed partial class Goal
{
    public IrTerm Predicate { get; }
    public ProofDiagnosticKind Diagnostic { get; }
    public SourceLocationId Location { get; }
}

internal sealed partial class BackendCheckResult
{
    internal BackendCheckResult(
        BackendCheckStatus status,
        ImmutableArray<int> unsatCore,
        BackendModel? model,
        BackendFailureReason failureReason
    )
    {
        RepositoryParameterGuard.RequireInitialized(unsatCore.IsDefault, nameof(unsatCore), "Array must be initialized.");
        Status = status;
        UnsatCore = unsatCore;
        Model = model;
        FailureReason = failureReason;
    }
    public BackendCheckStatus Status { get; }
    public ImmutableArray<int> UnsatCore { get; }
    public BackendModel? Model { get; }
    public BackendFailureReason FailureReason { get; }
}

internal sealed partial class ProvenOutcome : ProofOutcome
{
    internal ProvenOutcome(ImmutableArray<ProofJustification> core)
    {
        RepositoryParameterGuard.RequireInitialized(core.IsDefault, nameof(core), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(core.IsDefault, nameof(core), "Array must be initialized.");
        Core = core;
    }
    public ImmutableArray<ProofJustification> Core { get; }
}

internal sealed partial class ValidatedModel
{
    internal ValidatedModel(ImmutableDictionary<IrVarId, IrValue> assignments)
    {
        assignments = RepositoryParameterGuard.NotNull(assignments, nameof(assignments));
        Assignments = assignments;
    }
    public ImmutableDictionary<IrVarId, IrValue> Assignments { get; }
}

internal sealed partial class RefutedOutcome : ProofOutcome
{
    internal RefutedOutcome(ValidatedModel model)
    {
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        Model = model;
    }
    public ValidatedModel Model { get; }
}

internal sealed partial class UnknownOutcome : ProofOutcome
{
    internal UnknownOutcome(AbstentionReason reason)
    {
        Reason = reason;
    }
    public AbstentionReason Reason { get; }
}
