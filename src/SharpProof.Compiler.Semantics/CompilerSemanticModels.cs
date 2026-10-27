#nullable enable
namespace SharpProof.Compiler.Semantics;

internal sealed record EffectClaimDeclaration(
    EffectEvaluationContractKind Kind,
    ImmutableArray<AttributeData> Attributes,
    EffectEvaluationOutcome Outcome,
    EffectEvaluationReason Reason,
    EffectEvaluationCertainty Certainty,
    string Evidence,
    EffectClaimConstraint Constraint)
{
    public ImmutableArray<AttributeData> Attributes { get; init; } = Attributes.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Attributes)) : Attributes;
    public string Evidence { get; init; } = Evidence ?? throw new ArgumentNullException(nameof(Evidence));
    public EffectClaimConstraint Constraint { get; init; } = Constraint ?? throw new ArgumentNullException(nameof(Constraint));
}

internal sealed partial record EffectClaimConstraint(
    EffectContractKind Effects,
    EffectContractCapabilityKind Capabilities,
    ImmutableArray<INamedTypeSymbol> ExceptionTypes
)
{
    public ImmutableArray<INamedTypeSymbol> ExceptionTypes { get; init; } = ExceptionTypes.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(ExceptionTypes)) : ExceptionTypes;
}

internal enum PotentialRequiresCallOrigin
{
    Operation,
    ImplicitBaseConstructor,
    ExplicitPrimaryBaseConstructor
}

internal sealed record PotentialRequiresCallSite(
    IMethodSymbol Owner,
    IOperation Origin,
    SyntaxNode Syntax,
    PotentialRequiresCallOrigin OriginKind,
    int CallRoleIndex,
    IMethodSymbol DeclaredTarget,
    IMethodSymbol Target,
    IOperation? Instance,
    ImmutableArray<IArgumentOperation> Arguments,
    ImmutableDictionary<int, IOperation> ExplicitArguments,
    ImmutableDictionary<int, long> ImplicitIntegerArguments,
    bool CanReplay
)
{
    public IMethodSymbol Owner { get; init; } = Owner ?? throw new ArgumentNullException(nameof(Owner));
    public IOperation Origin { get; init; } = Origin ?? throw new ArgumentNullException(nameof(Origin));
    public SyntaxNode Syntax { get; init; } = Syntax ?? throw new ArgumentNullException(nameof(Syntax));
    public IMethodSymbol DeclaredTarget { get; init; } = DeclaredTarget ?? throw new ArgumentNullException(nameof(DeclaredTarget));
    public IMethodSymbol Target { get; init; } = Target ?? throw new ArgumentNullException(nameof(Target));
    public ImmutableArray<IArgumentOperation> Arguments { get; init; } = Arguments.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Arguments)) : Arguments;
    public ImmutableDictionary<int, IOperation> ExplicitArguments { get; init; } = ExplicitArguments ?? throw new ArgumentNullException(nameof(ExplicitArguments));
    public ImmutableDictionary<int, long> ImplicitIntegerArguments { get; init; } = ImplicitIntegerArguments ?? throw new ArgumentNullException(nameof(ImplicitIntegerArguments));
}
