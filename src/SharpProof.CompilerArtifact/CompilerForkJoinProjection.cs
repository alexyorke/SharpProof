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
    public CompilerTaskRuntimePremiseCapture? RuntimePremises { get; init; }
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
    internal CompilerTaskRootExceptionBoundaryCapture? RootExceptionBoundary { get; init; }
    internal CompilerTaskCallbackActivationCapture? CallbackActivations { get; init; }
    internal CompilerTaskSourceBodyCapture? SourceBodies { get; init; }
    internal CompilerTaskEmittedFaultBodyCapture? EmittedFaultBodies { get; init; }
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public string CompilerIdentity { get; init; } = RepositoryParameterGuard.NotNull(CompilerIdentity, nameof(CompilerIdentity));
    public ImmutableArray<CompilerTaskCarrierType> CallbackFrames { get; init; } = CallbackFrames.IsDefault
        ? throw new ArgumentException("Callback frames must be initialized.", nameof(CallbackFrames)) : CallbackFrames;
}
internal sealed record CompilerTaskCarrierCapture(CompilerTaskCarrierShapes? Shapes, string? UnknownReason);

internal sealed record CompilerTaskCallbackForwarder(int MethodToken, int ChildMethodToken);
internal sealed record CompilerTaskCallbackActivation(string RootCallableId, string ModuleSha256,
    ImmutableArray<CompilerTaskCallbackForwarder> Forwarders)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public ImmutableArray<CompilerTaskCallbackForwarder> Forwarders { get; init; } = Forwarders.IsDefault
        ? throw new ArgumentException("Forwarders must be initialized.", nameof(Forwarders)) : Forwarders;
    internal int WrapperActivationsUpper => Forwarders.IsEmpty ? 0 : 1;
}
internal sealed record CompilerTaskCallbackActivationCapture(CompilerTaskCallbackActivation? Activations, string? UnknownReason);

// Emitted call-free primitive child bodies, not a bound on CLR/helper frames.
// The owning module is the same cached emission as the callback forwarders.
internal sealed record CompilerTaskSourceBodyInventory(string RootCallableId, string ModuleSha256,
    ImmutableArray<int> MethodTokens)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public ImmutableArray<int> MethodTokens { get; init; } = MethodTokens.IsDefault
        ? throw new ArgumentException("Method tokens must be initialized.", nameof(MethodTokens)) : MethodTokens;
}
internal sealed record CompilerTaskSourceBodyCapture(CompilerTaskSourceBodyInventory? Inventory, string? UnknownReason);

// Source-owned emitted boundary evidence only, not a CLR frame or byte bound.
internal sealed record CompilerTaskRootExceptionBoundary(string RootCallableId, string ModuleSha256,
    string StateMachineMetadataName, int MethodToken, int TryOffset, int TryLength)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public string StateMachineMetadataName { get; init; } = RepositoryParameterGuard.NotNull(StateMachineMetadataName, nameof(StateMachineMetadataName));
}

internal sealed record CompilerTaskRootExceptionBoundaryCapture(CompilerTaskRootExceptionBoundary? Boundary, string? UnknownReason);
