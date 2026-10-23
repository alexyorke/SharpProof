namespace SharpProof.Worker;

internal sealed record NativeTaskRuntimePremiseBindingCheck(NativeTaskRuntimeCostInputs? Inputs,
    WorkerResourceTaskRuntimePremiseEvidence? Evidence, string? UnknownReason);

// A declaration is a reported user premise, never runtime detection. Compiler
// ownership, reference identity and manifest declarations must all bind before
// any quantity can enter the conditional arithmetic or result evidence.
internal static class NativeResourceTaskRuntimeBinding
{
    internal static NativeTaskRuntimePremiseBindingCheck Bind(CompilerCallablePreparation target, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        cancellation.ThrowIfCancellationRequested();
        if (target.ForkJoinProjection is not { } projection || target.TotalEntry is not { } root ||
            projection.RuntimePremises is not { } capture)
        { return Unknown("TaskRuntimePremisesNotDeclared"); }
        if (capture.Inventory is not { } inventory)
        { return Unknown(capture.UnknownReason ?? "TaskRuntimePremisesInvalid"); }
        if (capture.UnknownReason is not null || projection.Carriers?.Shapes is not { } shapes ||
            inventory.RootCallableId != root.CallableId || inventory.RootCallableId != target.Entry.CallableId ||
            inventory.RootCallableId != projection.RootCallableId || inventory.RootCallableId != shapes.RootCallableId ||
            inventory.ModuleSha256 != shapes.ModuleSha256 || inventory.CompilerIdentity != shapes.CompilerIdentity ||
            inventory.FrameworkAssemblyIdentity != projection.FrameworkAssemblyIdentity ||
            !WorkerProtocolJson.IsSha256(inventory.ModuleSha256) || string.IsNullOrWhiteSpace(inventory.CompilerIdentity) ||
            inventory.CompilerIdentity.Length > 512 || inventory.Declarations is not { } declarations ||
            declarations.RootCallableId != inventory.RootCallableId)
        { return Unknown("TaskRuntimePremiseOwnershipInvalid"); }
        if (ResourceTaskRuntimePremiseIdentity.Validate(declarations) is { } invalid)
        { return Unknown(invalid); }
        var dependency = NativeResourceTaskBinding.Bind(target, cancellation);
        if (dependency is null || new NativeResourceLayoutBinding(target).Bind() is null)
        { return Unknown("TaskRuntimeReferencePremiseNotBound"); }
        var deploymentId = ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations);
        var ids = new HashSet<string>(StringComparer.Ordinal) { deploymentId };
        foreach (var declaration in declarations.Quantities)
        {
            cancellation.ThrowIfCancellationRequested();
            ids.Add(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declaration));
        }
        if (target.Entry.Assumptions.IsDefault || ids.Any(id => id is null ? throw new ArgumentNullException(nameof(id)) : target.Entry.Assumptions.Count(assumption =>
                assumption is not null && assumption.Id == id && assumption.Kind == WorkerAssumptionKind.UserAssume && !assumption.Used) != 1))
        { return Unknown("TaskRuntimeAssumptionsNotBound"); }
        var quantities = ImmutableArray.CreateBuilder<WorkerResourceTaskRuntimeQuantityEvidence>(declarations.Quantities.Length);
        foreach (var declaration in declarations.Quantities)
        {
            cancellation.ThrowIfCancellationRequested();
            quantities.Add(new()
            {
                Quantity = declaration.Quantity,
                UpperBound = declaration.UpperBound,
                Evidence = declaration.Evidence,
                Unit = ResourceTaskRuntimePremiseIdentity.Unit(declaration.Quantity),
                Scope = ResourceTaskRuntimePremiseIdentity.Scope(declaration.Quantity),
                AssumptionId = ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declaration)
            });
        }
        var values = declarations.Quantities;
        return new(new(values[0].UpperBound, values[1].UpperBound, values[2].UpperBound, values[3].UpperBound,
            values[4].UpperBound, values[5].UpperBound, values[6].UpperBound, values[7].UpperBound,
            values[8].UpperBound, values[9].UpperBound, values[10].UpperBound), new()
            {
                ProfileCatalogSha256 = ResourceTaskRuntimePremiseIdentity.ProfileCatalogSha256,
                RootCallableId = root.CallableId,
                ModuleSha256 = inventory.ModuleSha256,
                CompilerIdentity = inventory.CompilerIdentity,
                ReferenceAssembly = dependency.ReferenceAssembly,
                ReferenceSha256 = dependency.ReferenceSha256,
                Profile = declarations.Profile,
                DeploymentManifestSha256 = declarations.DeploymentManifestSha256,
                DeploymentEvidence = declarations.Evidence,
                DeclarationSha256 = ResourceTaskRuntimePremiseIdentity.DeclarationSha256(declarations),
                DeploymentAssumptionId = deploymentId,
                Quantities = quantities.MoveToImmutable()
            }, null);
    }

    internal static WorkerClaimResult Project(CompilerCallablePreparation target, WorkerClaimResult result,
        NativeTaskRuntimePremiseBindingCheck binding)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        binding = ArgumentNullGuard.NotNull(binding, nameof(binding));
        if (binding.Inputs is null || binding.Evidence is not { } evidence || binding.UnknownReason is not null ||
            result.Outcome != WorkerClaimOutcome.Proven || result.ResourceBound is null)
        { throw new ArgumentException("Conditional runtime publication requires complete bound premises and a proof.", nameof(binding)); }
        var used = new HashSet<string>(result.Assumptions.Where(static assumption => assumption is not null && assumption.Used)
            .Select(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id), StringComparer.Ordinal)
        { evidence.DeploymentAssumptionId };
        foreach (var quantity in evidence.Quantities)
        { used.Add(quantity.AssumptionId); }
        result.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(target, used.ToImmutableHashSet(StringComparer.Ordinal));
        result.ProofCore = [.. result.ProofCore, "resource-runtime-premise-record:" + WorkerProtocolJson.RuntimePremiseRecordSha256(evidence)];
        return result with { ResourceBound = result.ResourceBound! with { RuntimePremises = evidence } };
    }

    private static NativeTaskRuntimePremiseBindingCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, null, string.IsNullOrWhiteSpace(reason) || reason.Length > 256 || reason.Any(char.IsControl)
            ? "TaskRuntimePremisesInvalid" : reason);
    }
}
