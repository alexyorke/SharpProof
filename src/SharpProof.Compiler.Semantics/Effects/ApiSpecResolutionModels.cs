#nullable enable
namespace SharpProof.Specs;

internal sealed partial class ApiSpecLookupResult
{
    public ApiSpecLookupResult(ApiSpecLookupStatus status, ResolvedApiSpec? spec, ApiSpecLookupFailure? failure)
    {
        Status = status;
        Spec = spec;
        Failure = failure;
    }
    public ApiSpecLookupStatus Status { get; }
    public ResolvedApiSpec? Spec { get; }
    public ApiSpecLookupFailure? Failure { get; }
}

internal sealed partial record ApiSpecResolutionFailure(
    SpecId Spec,
    string WitnessIdentifier,
    ApiSpecResolutionFailureKind Kind,
    string Detail
)
{
    public SpecId Spec { get; init; } = Spec.IsDefault ? throw new ArgumentOutOfRangeException(nameof(Spec)) : Spec;
    public string WitnessIdentifier { get; init; } = WitnessIdentifier ?? throw new ArgumentNullException(nameof(WitnessIdentifier));
    public string Detail { get; init; } = Detail ?? throw new ArgumentNullException(nameof(Detail));
}

internal sealed partial record ResolvedApiSpec(ApiSpecTemplate Template, ISymbol Symbol)
{
    public ApiSpecTemplate Template { get; init; } = Template ?? throw new ArgumentNullException(nameof(Template));
    public ISymbol Symbol { get; init; } = Symbol ?? throw new ArgumentNullException(nameof(Symbol));
}

internal sealed partial record ApiSpecLookupFailure(
    ApiSpecLookupFailureKind Kind,
    string SymbolIdentifier,
    string Detail
)
{
    public string SymbolIdentifier { get; init; } = SymbolIdentifier ?? throw new ArgumentNullException(nameof(SymbolIdentifier));
    public string Detail { get; init; } = Detail ?? throw new ArgumentNullException(nameof(Detail));
}
