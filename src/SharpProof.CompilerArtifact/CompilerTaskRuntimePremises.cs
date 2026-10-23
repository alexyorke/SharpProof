using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal sealed record CompilerTaskRuntimePremiseInventory(string RootCallableId, string ModuleSha256,
    string CompilerIdentity, string FrameworkAssemblyIdentity, WorkerResourceTaskRuntimeDeclarations Declarations)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public string CompilerIdentity { get; init; } = RepositoryParameterGuard.NotNull(CompilerIdentity, nameof(CompilerIdentity));
    public string FrameworkAssemblyIdentity { get; init; } = RepositoryParameterGuard.NotNull(FrameworkAssemblyIdentity, nameof(FrameworkAssemblyIdentity));
    public WorkerResourceTaskRuntimeDeclarations Declarations { get; init; } = RepositoryParameterGuard.NotNull(Declarations, nameof(Declarations));
}

internal sealed record CompilerTaskRuntimePremiseCapture(CompilerTaskRuntimePremiseInventory? Inventory, string? UnknownReason);

internal sealed record CompilerTaskRuntimePremiseDeclarationCapture(WorkerResourceTaskRuntimeDeclarations? Declarations, string? UnknownReason);
