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
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string FrameworkAssemblyIdentity { get; init; } = RepositoryParameterGuard.NotNull(FrameworkAssemblyIdentity, nameof(FrameworkAssemblyIdentity));
    public ImmutableArray<string> RootParameterNames { get; init; } = RootParameterNames.IsDefault
        ? throw new ArgumentException("Root parameter names must be initialized.", nameof(RootParameterNames)) : RootParameterNames;
    public ImmutableArray<CompilerForkJoinChild> Children { get; init; } = Children.IsDefault
        ? throw new ArgumentException("Children must be initialized.", nameof(Children)) : Children;
}

internal sealed record CompilerForkJoinProjectionResult(CompilerForkJoinProjection? Projection, string? UnknownReason);
