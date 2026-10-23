using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

internal sealed record WorkerResourceTaskRuntimePremiseEvidence
{
    [JsonRequired] public string Model { get; init; } = ResourceTaskRuntimePremiseIdentity.Model;
    [JsonRequired] public string ProfileCatalogSha256 { get; init; } = string.Empty;
    [JsonRequired] public string RootCallableId { get; init; } = string.Empty;
    [JsonRequired] public string ModuleSha256 { get; init; } = string.Empty;
    [JsonRequired] public string CompilerIdentity { get; init; } = string.Empty;
    [JsonRequired] public string ReferenceAssembly { get; init; } = string.Empty;
    [JsonRequired] public string ReferenceSha256 { get; init; } = string.Empty;
    [JsonRequired] public WorkerResourceTaskDeploymentProfile Profile { get; init; }
    [JsonRequired] public string DeploymentManifestSha256 { get; init; } = string.Empty;
    [JsonRequired] public string DeploymentEvidence { get; init; } = string.Empty;
    [JsonRequired] public string DeclarationSha256 { get; init; } = string.Empty;
    [JsonRequired] public string DeploymentAssumptionId { get; init; } = string.Empty;
    [JsonRequired, JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerResourceTaskRuntimeQuantityEvidence> Quantities { get; init; } = [];
}

internal sealed record WorkerResourceTaskRuntimeQuantityEvidence
{
    [JsonRequired] public WorkerResourceTaskRuntimeQuantity Quantity { get; init; }
    [JsonRequired] public long UpperBound { get; init; }
    [JsonRequired] public string Evidence { get; init; } = string.Empty;
    [JsonRequired] public string Unit { get; init; } = string.Empty;
    [JsonRequired] public string Scope { get; init; } = string.Empty;
    [JsonRequired] public string AssumptionId { get; init; } = string.Empty;
}
