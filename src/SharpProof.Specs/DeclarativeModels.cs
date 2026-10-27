#nullable enable
namespace SharpProof.Specs;

internal sealed partial class SpecInstantiationResult
{
    private SpecInstantiationResult(
        SpecInstantiationStatus status,
        ImmutableArray<IrTerm> postconditions,
        IrTerm? normalCompletionCondition,
        SpecInstantiationFailure? failure
    )
    {
        RepositoryParameterGuard.RequireInitialized(postconditions.IsDefault, nameof(postconditions), "Array must be initialized.");
        Status = status;
        Postconditions = postconditions;
        NormalCompletionCondition = normalCompletionCondition;
        Failure = failure;
    }
    public SpecInstantiationStatus Status { get; }
    public ImmutableArray<IrTerm> Postconditions { get; }
    public IrTerm? NormalCompletionCondition { get; }
    public SpecInstantiationFailure? Failure { get; }
}

internal sealed partial record SpecInstantiationFailure(
    SpecInstantiationFailureKind Kind,
    SpecVarId? Variable,
    string Detail
)
{
    public string Detail { get; init; } = Detail ?? throw new ArgumentNullException(nameof(Detail));
}

internal static partial class ApiSpecInstantiator
{
    private sealed partial class Instantiation
    {
        internal readonly partial record struct TermResult(IrTerm? Term, SpecInstantiationFailure? Failure);
    }
}
