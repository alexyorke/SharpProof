using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// These compiler-owned children are resource projections, never a replacement
// for the root callable's functional Total program. No numeric result is
// published until the native worker checks the children and runtime model.
internal sealed record CompilerForkJoinArgument(int ParameterOrdinal, int? RootParameterOrdinal, int? Constant);

internal sealed record CompilerForkJoinChild(
    CompilerTotalCallablePreparation Owner,
    ResourceProgram Cost,
    ImmutableArray<CompilerForkJoinArgument> Arguments)
{
    public CompilerTotalCallablePreparation Owner { get; init; } = RepositoryParameterGuard.NotNull(Owner, nameof(Owner));
    public ResourceProgram Cost { get; init; } = RepositoryParameterGuard.NotNull(Cost, nameof(Cost));
    public ImmutableArray<CompilerForkJoinArgument> Arguments { get; init; } = Arguments.IsDefault
        ? throw new ArgumentException("Arguments must be initialized.", nameof(Arguments)) : Arguments;
}

internal sealed record CompilerForkJoinProjection(
    string RootCallableId,
    string FrameworkAssemblyIdentity,
    ImmutableArray<string> RootParameterNames,
    ImmutableArray<CompilerForkJoinChild> Children)
{
    public bool ConfiguredAwait { get; init; }
    public CompilerTaskCarrierCapture? Carriers { get; init; }
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string FrameworkAssemblyIdentity { get; init; } = RepositoryParameterGuard.NotNull(FrameworkAssemblyIdentity, nameof(FrameworkAssemblyIdentity));
    public ImmutableArray<string> RootParameterNames { get; init; } = RootParameterNames.IsDefault
        ? throw new ArgumentException("Root parameter names must be initialized.", nameof(RootParameterNames)) : RootParameterNames;
    public ImmutableArray<CompilerForkJoinChild> Children { get; init; } = Children.IsDefault
        ? throw new ArgumentException("Children must be initialized.", nameof(Children)) : Children;
}

internal sealed record CompilerForkJoinProjectionResult(CompilerForkJoinProjection? Projection, string? UnknownReason);

// Compiler-owned metadata shapes, not byte verdicts. A runtime/layout binder
// and native composition are required before any root storage claim is made.
internal sealed record CompilerTaskCarrierType(string MetadataName, bool IsValueType, ImmutableArray<ResourceStorageKind> Fields)
{
    public string MetadataName { get; init; } = RepositoryParameterGuard.NotNull(MetadataName, nameof(MetadataName));
    public ImmutableArray<ResourceStorageKind> Fields { get; init; } = Fields.IsDefault
        ? throw new ArgumentException("Fields must be initialized.", nameof(Fields)) : Fields;
}
internal sealed record CompilerTaskCarrierShapes(string RootCallableId, string ModuleSha256, string CompilerIdentity,
    int MatchedCallbacks, ImmutableArray<CompilerTaskCarrierType> CallbackFrames, CompilerTaskCarrierType? StateMachine)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public string CompilerIdentity { get; init; } = RepositoryParameterGuard.NotNull(CompilerIdentity, nameof(CompilerIdentity));
    public ImmutableArray<CompilerTaskCarrierType> CallbackFrames { get; init; } = CallbackFrames.IsDefault
        ? throw new ArgumentException("Callback frames must be initialized.", nameof(CallbackFrames)) : CallbackFrames;
}
internal sealed record CompilerTaskCarrierCapture(CompilerTaskCarrierShapes? Shapes, string? UnknownReason);
