using System.Collections.Immutable;
using System.Text.Json;

namespace SharpProof.Worker.Protocol;

internal static partial class WorkerProtocolJson
{
    internal static string RuntimePremiseRecordSha256(WorkerResourceTaskRuntimePremiseEvidence evidence)
    {
        evidence = ArgumentNullGuard.NotNull(evidence, nameof(evidence));
        return ComputeSha256([.. JsonSerializer.SerializeToUtf8Bytes(evidence, SharedOptions)]);
    }

    private static bool HasValidTaskRuntimePremises(WorkerClaimManifestEntry claim, WorkerClaimResult result,
        WorkerResourceBoundEvidence bound)
    {
        claim = ArgumentNullGuard.NotNull(claim, nameof(claim));
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        bound = ArgumentNullGuard.NotNull(bound, nameof(bound));
        var used = EmptyIfDefault(result.Assumptions).Where(static assumption => assumption is not null && assumption.Used &&
            assumption.Kind == WorkerAssumptionKind.UserAssume && IsTaskRuntimeAssumption(assumption.Id)).ToArray();
        var tokens = EmptyIfDefault(result.ProofCore).Where(static token => token is not null &&
            token.StartsWith("resource-runtime-premise-record:", StringComparison.Ordinal)).ToArray();
        if (bound.RuntimePremises is not { } evidence)
        { return bound.ExecutionScope != WorkerResourceModels.TaskRuntimeScope && used.Length == 0 && tokens.Length == 0; }
        if (result.Outcome != WorkerClaimOutcome.Proven || result.Vacuity != WorkerVacuityKind.None ||
            bound.ExecutionScope != WorkerResourceModels.TaskRuntimeScope || bound.Metric != WorkerResourceMetric.AllocatedBytes ||
            evidence.Model != ResourceTaskRuntimePremiseIdentity.Model ||
            evidence.ProfileCatalogSha256 != ResourceTaskRuntimePremiseIdentity.ProfileCatalogSha256 ||
            evidence.RootCallableId != claim.CallableId || !IsSha256(evidence.ModuleSha256) ||
            !ResourceMetadataText(evidence.CompilerIdentity, 512) || !ResourceMetadataText(evidence.ReferenceAssembly, 512) ||
            !IsSha256(evidence.ReferenceSha256) || !IsSha256(evidence.DeploymentManifestSha256) ||
            !ResourceTaskRuntimePremiseIdentity.IsEvidence(evidence.DeploymentEvidence) ||
            evidence.Quantities.IsDefault || evidence.Quantities.Length != ResourceTaskRuntimePremiseIdentity.RequiredQuantities.Length)
        { return false; }
        var declarations = ImmutableArray.CreateBuilder<WorkerResourceTaskRuntimeDeclaration>(evidence.Quantities.Length);
        for (var index = 0; index < evidence.Quantities.Length; index++)
        {
            var quantity = evidence.Quantities[index];
            if (quantity is null || quantity.Quantity != ResourceTaskRuntimePremiseIdentity.RequiredQuantities[index] ||
                quantity.UpperBound < 0 || !ResourceTaskRuntimePremiseIdentity.IsEvidence(quantity.Evidence) ||
                quantity.Unit != ResourceTaskRuntimePremiseIdentity.Unit(quantity.Quantity) ||
                quantity.Scope != ResourceTaskRuntimePremiseIdentity.Scope(quantity.Quantity))
            { return false; }
            declarations.Add(new(quantity.Quantity, quantity.UpperBound, quantity.Evidence));
        }
        var values = new WorkerResourceTaskRuntimeDeclarations(evidence.RootCallableId, evidence.Profile,
            evidence.DeploymentManifestSha256, evidence.DeploymentEvidence, declarations.MoveToImmutable());
        if (ResourceTaskRuntimePremiseIdentity.Validate(values) is not null ||
            evidence.DeclarationSha256 != ResourceTaskRuntimePremiseIdentity.DeclarationSha256(values) ||
            evidence.DeploymentAssumptionId != ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(values))
        { return false; }
        var expected = new HashSet<string>(StringComparer.Ordinal) { evidence.DeploymentAssumptionId };
        for (var index = 0; index < evidence.Quantities.Length; index++)
        {
            var id = ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(values, values.Quantities[index]);
            if (evidence.Quantities[index].AssumptionId != id || !expected.Add(id))
            { return false; }
        }
        if (used.Length != expected.Count || !expected.SetEquals(used.Select(static assumption =>
                assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id)) ||
            tokens.Length != 1 || tokens[0] != "resource-runtime-premise-record:" + RuntimePremiseRecordSha256(evidence))
        { return false; }
        var references = bound.MetadataAssumptions.Where(static dependency => dependency is not null &&
            dependency.RuntimeModel is WorkerResourceModels.TaskJoinModel or WorkerResourceModels.ConfiguredTaskJoinModel or WorkerResourceModels.AllocationLayout).ToArray();
        return references.Length == 2 && references.All(dependency => dependency is not null &&
            dependency.ReferenceAssembly == evidence.ReferenceAssembly && dependency.ReferenceSha256 == evidence.ReferenceSha256);
    }

    internal static bool IsTaskRuntimeAssumption(string? id)
    {
        return id is not null && (id.StartsWith("resource-runtime-deployment-v1:", StringComparison.Ordinal) ||
            id.StartsWith("resource-runtime-quantity-v1:", StringComparison.Ordinal));
    }
}
