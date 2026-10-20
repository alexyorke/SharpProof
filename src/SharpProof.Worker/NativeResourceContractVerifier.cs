using System.Text.Json;

namespace SharpProof.Worker;

internal static class NativeResourceContractVerifier
{
    internal static async Task<WorkerClaimResult> VerifyAsync(CompilerCallablePreparation target,
        CompilerEffectClaimArtifact claim, WorkerBudgets budgets, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        claim = ArgumentNullGuard.NotNull(claim, nameof(claim));
        budgets = ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        var result = Verify(target, claim, cancellation);
        if (claim.Constraint.ResourceUpperBound?.Metric is WorkerResourceMetric.SourceActivationDepth or WorkerResourceMetric.SourceStackBytes ||
            result.ResourceBound?.UnknownReason != "ResourceBoundComparisonUnproved")
        { return result; }
        try
        {
            var limit = ResourceExpressionCodec.DecodeForCallable(claim.Constraint.ResourceUpperBound!.UpperBound,
                target.Entry.CallableId, target.Total!, cancellation);
            var violation = await NativeResourceViolationReplay.FindAsync(target, claim, limit, budgets, cancellation).ConfigureAwait(false);
            if (violation is null)
            { return result; }
            if (claim.Constraint.ResourceUpperBound!.Metric is WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes)
            {
                var lifetimeLayout = new NativeResourceLayoutBinding(target);
                if (lifetimeLayout.Bind() is null)
                { return result; }
                lifetimeLayout.Project(violation);
                return violation;
            }
            var metadata = new NativeResourceMetadataBinding(target);
            var check = NativeResourceProgramVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId, target.Total!, metadata.Bind, cancellation);
            if (!check.Verified)
            { return result; }
            var layout = new NativeResourceLayoutBinding(target);
            if (claim.Constraint.ResourceUpperBound!.Metric == WorkerResourceMetric.AllocatedBytes && layout.Bind() is null)
            { return result; }
            metadata.Project(violation);
            if (claim.Constraint.ResourceUpperBound.Metric == WorkerResourceMetric.AllocatedBytes)
            { layout.Project(violation); }
            return violation;
        }
        catch (ResourceExpressionLimitException)
        {
            result.Reason = WorkerClaimReason.ResourceLimit;
            result.ResourceBound.UnknownReason = "ResourceReplayBudgetExceeded";
            return result;
        }
    }

    internal static WorkerClaimResult Verify(CompilerCallablePreparation target, CompilerEffectClaimArtifact claim, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        claim = ArgumentNullGuard.NotNull(claim, nameof(claim));
        cancellation.ThrowIfCancellationRequested();
        var constraint = claim.Constraint.ResourceUpperBound;
        if (claim.ContractKind != WorkerEffectContractKind.ResourceUpperBound || constraint is null ||
            !target.Entry.ClaimIds.Contains(claim.ClaimId))
        { throw new ArgumentException("The resource claim is not owned by this callable.", nameof(claim)); }
        CompilerEffectClaimArtifactCodec.Validate(claim);
        if (claim.Reason == WorkerClaimReason.UnsupportedContract)
        { return Unknown("UnsupportedResourceContract", WorkerClaimReason.UnsupportedContract); }
        if (target.Total is not { IsBodyAbstraction: false } owner || !owner.EffectsCompleteAtEntry)
        { return Unknown("ResourceEntryUnavailable", WorkerClaimReason.UnsupportedBody); }
        if (!owner.ValidEffectClaimIds.Contains(claim.ClaimId))
        { return Unknown("UnsupportedResourceContract", WorkerClaimReason.UnsupportedContract); }
        try
        {
            var limit = ResourceExpressionCodec.DecodeForCallable(constraint.UpperBound, target.Entry.CallableId, owner, cancellation);
            if (constraint.Metric is WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes)
            {
                var lifetimeLayout = new NativeResourceLayoutBinding(target);
                var context = lifetimeLayout.Bind();
                if (context is null)
                { return Unknown("ResourceLayoutAssumptionsNotBound", WorkerClaimReason.UnsupportedBody); }
                var memory = ResourceMemoryProgram.Evaluate(owner.Program, context, cancellation);
                NativeResourceMetadataBinding? envelopeMetadata = null;
                ResourceExpression memoryUpper;
                if (memory.Bounds is { } bounds)
                {
                    memoryUpper = ResourceExpression.Literal(constraint.Metric == WorkerResourceMetric.PeakAuxiliaryBytes
                        ? bounds.PeakAuxiliaryUpper : bounds.RetainedUpper);
                }
                else
                {
                    if (target.ResourceProgram is null || !NativeResourceMemoryEnvelope.Admits(owner, cancellation))
                    { return Unknown(memory.UnknownReason ?? "ResourceMemoryProofUnavailable", WorkerClaimReason.UnsupportedBody); }
                    envelopeMetadata = new(target);
                    var costs = NativeResourceProgramVerifier.Verify(target.ResourceProgram, target.Entry.CallableId, owner, envelopeMetadata.Bind, cancellation);
                    if (!costs.Verified)
                    { return Unknown(costs.UnknownReason ?? "ResourceProofUnavailable", WorkerClaimReason.UnsupportedBody); }
                    var allocation = NativeResourceAllocationVerifier.Verify(target.ResourceProgram, target.Entry.CallableId, owner, context, cancellation);
                    if (allocation.ConditionalUpperBound is not { } cumulativeUpper)
                    { return Unknown(allocation.UnknownReason ?? "ResourceAllocationProofUnavailable", WorkerClaimReason.UnsupportedBody); }
                    var scalarOutput = owner.Result is not { } output ||
                        owner.Program.Factory.GetTypeInfo(owner.Program.Factory.GetVariableInfo(output).Type).Kind is IrTypeKind.Integer or IrTypeKind.Boolean;
                    memoryUpper = constraint.Metric == WorkerResourceMetric.RetainedBytes && scalarOutput ? ResourceExpression.Zero : cumulativeUpper;
                }
                if (!ResourceBoundOrder.Proves(memoryUpper, limit, cancellation))
                { return Unknown("ResourceBoundComparisonUnproved", WorkerClaimReason.SolverIncomplete); }
                var memoryResult = CallableClaimResultAssembler.Create(target, claim.ClaimId, WorkerClaimOutcome.Proven,
                    WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, projectAssumptions: false);
                memoryResult.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(target, new HashSet<string>(StringComparer.Ordinal));
                memoryResult.ProofCore = ["native-resource:" + claim.ClaimId,
                    "resource-memory-model:" + ResourceMemoryTrace.ModelId,
                    "resource-memory-program:" + WorkerProtocolJson.ComputeSha256(JsonSerializer.SerializeToUtf8Bytes(
                        PortableIrGraphCodec.Encode(owner.Program, [], cancellation).Graph))];
                memoryResult.ResourceBound = Evidence(memoryUpper.ToDisplayString(), null, memoryUpper);
                memoryResult.ResourceBound.Expected = new() { UnknownReason = "ResourceExpectedMetricUnsupported" };
                memoryResult.ProofCore = [.. memoryResult.ProofCore, "resource-asymptotic-model:" + WorkerResourceModels.Asymptotic];
                if (envelopeMetadata != null)
                {
                    memoryResult.ProofCore = [.. memoryResult.ProofCore,
                        "resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId,
                        "resource-program:" + WorkerProtocolJson.ComputeSha256(ResourceProgramCodec.Encode(target.ResourceProgram!, cancellation))];
                    envelopeMetadata.Project(memoryResult);
                }
                lifetimeLayout.Project(memoryResult);
                return memoryResult;
            }
            if (target.ResourceProgram is null)
            { return Unknown(target.ResourceUnknownReason ?? "ResourceProgramUnavailable", WorkerClaimReason.UnsupportedBody); }
            var metadata = new NativeResourceMetadataBinding(target);
            var check = NativeResourceProgramVerifier.Verify(target.ResourceProgram, target.Entry.CallableId, owner, metadata.Bind, cancellation);
            if (!check.Verified)
            { return Unknown(check.UnknownReason ?? "ResourceProofUnavailable", WorkerClaimReason.UnsupportedBody); }
            ResourceExpression? stackUpper = null;
            if (constraint.Metric is WorkerResourceMetric.SourceActivationDepth or WorkerResourceMetric.SourceStackBytes)
            {
                var stack = NativeResourceStackVerifier.Derive(target.ResourceProgram, cancellation);
                if (stack.Bounds is not { } stackBounds)
                { return Unknown(stack.UnknownReason ?? "ResourceStackProofUnavailable", WorkerClaimReason.UnsupportedBody); }
                stackUpper = constraint.Metric == WorkerResourceMetric.SourceActivationDepth ? stackBounds.Depth : stackBounds.StorageBytes;
            }
            var layout = new NativeResourceLayoutBinding(target);
            ResourceExpression? bytes = null;
            ResourceRuntimeLayoutContext? expectedLayout = null;
            if (constraint.Metric == WorkerResourceMetric.AllocatedBytes)
            {
                var context = layout.Bind();
                if (context is null)
                { return Unknown("ResourceLayoutAssumptionsNotBound", WorkerClaimReason.UnsupportedBody); }
                expectedLayout = context;
                var allocation = NativeResourceAllocationVerifier.Verify(target.ResourceProgram, target.Entry.CallableId, owner, context, cancellation);
                if (allocation.ConditionalUpperBound is not { } allocationUpper)
                { return Unknown(allocation.UnknownReason ?? "ResourceAllocationProofUnavailable", WorkerClaimReason.UnsupportedBody); }
                bytes = allocationUpper;
            }
            var upper = constraint.Metric switch
            {
                WorkerResourceMetric.LoopBodyEntries => check.Cost!.LoopEntries,
                WorkerResourceMetric.ArrayAllocations => check.Cost!.Arrays,
                WorkerResourceMetric.AllocatedElements => check.Cost!.Elements,
                WorkerResourceMetric.AllocatedBytes => bytes!,
                WorkerResourceMetric.SourceActivationDepth or WorkerResourceMetric.SourceStackBytes => stackUpper!,
                _ => throw new InvalidDataException("Resource metric is unsupported.")
            };
            if (!ResourceBoundOrder.Proves(upper, limit, cancellation))
            { return Unknown("ResourceBoundComparisonUnproved", WorkerClaimReason.SolverIncomplete); }
            var result = CallableClaimResultAssembler.Create(target, claim.ClaimId, WorkerClaimOutcome.Proven,
                WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, projectAssumptions: false);
            result.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(target, new HashSet<string>(StringComparer.Ordinal));
            result.ProofCore = ["native-resource:" + claim.ClaimId,
                "resource-program:" + WorkerProtocolJson.ComputeSha256(ResourceProgramCodec.Encode(target.ResourceProgram, cancellation))];
            if (stackUpper != null)
            { result.ProofCore = [.. result.ProofCore, "resource-stack-model:" + NativeResourceStackVerifier.ModelId]; }
            result.ResourceBound = Evidence(upper.ToDisplayString(), null, upper);
            if (stackUpper is null)
            {
                result.ResourceBound.Accounting = NativeResourceAccountingVerifier.Evidence(target.ResourceProgram, cancellation);
                if (result.ResourceBound.Accounting.Length != 0)
                { result.ProofCore = [.. result.ProofCore, "resource-accounting:" + NativeResourceAccountingVerifier.ModelId]; }
            }
            result.ResourceBound.Expected = NativeResourceExpectedVerifier.Project(NativeResourceExpectedVerifier.Verify(
                target.ResourceProgram, (ResourceCostMetric)constraint.Metric, target.Entry.CallableId, owner, expectedLayout, cancellation));
            if (result.ResourceBound.Expected.UnknownReason is null)
            {
                result.ProofCore = [.. result.ProofCore, "resource-input-model:" + NativeResourceExpectedVerifier.ModelId,
                "resource-input-record:" + WorkerProtocolJson.ComputeSha256(JsonSerializer.SerializeToUtf8Bytes(result.ResourceBound.Expected, WorkerProtocolJson.Options))];
            }
            var lower = constraint.Metric switch
            {
                WorkerResourceMetric.LoopBodyEntries => check.LowerCost?.LoopEntries,
                WorkerResourceMetric.ArrayAllocations => check.LowerCost?.Arrays,
                WorkerResourceMetric.AllocatedElements => check.LowerCost?.Elements,
                _ => null
            };
            result.ResourceBound.Asymptotic!.Lower = LowerEvidence(result.ResourceBound.Asymptotic, lower,
                check.LowerDomainUnknownReason, cancellation);
            if (result.ResourceBound.Asymptotic.Lower.UnknownReason is null)
            { result.ProofCore = [.. result.ProofCore, "resource-normal-domain:" + NativeResourceNormalDomain.ModelId]; }
            result.ProofCore = [.. result.ProofCore, "resource-asymptotic-model:" + WorkerResourceModels.Asymptotic];
            metadata.Project(result);
            if (constraint.Metric == WorkerResourceMetric.AllocatedBytes)
            { layout.Project(result); }
            return result;
        }
        catch (InvalidDataException) { return Unknown("MalformedResourceEvidence", WorkerClaimReason.UnsupportedContract); }
        catch (ResourceExpressionLimitException) { return Unknown("ResourceProofBudgetExceeded", WorkerClaimReason.ResourceLimit); }

        WorkerResourceBoundEvidence Evidence(string? inferred, string? reason, ResourceExpression? upper = null)
        {
            return new()
            {
                Metric = constraint.Metric,
                CostModel = constraint.CostModel,
                DeclaredBoundSha256 = WorkerProtocolJson.ComputeSha256(constraint.UpperBound),
                InferredUpperBound = inferred,
                UnknownReason = reason,
                Asymptotic = upper is null ? null : AsymptoticEvidence(upper, cancellation)
            };
        }

        WorkerClaimResult Unknown(string reason, WorkerClaimReason kind)
        {
            reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
            var result = NativeEffectClaims.Unknown(target, claim.ClaimId, kind);
            result.ResourceBound = Evidence(null, reason);
            return result;
        }
    }
    private static WorkerResourceLowerAsymptoticEvidence LowerEvidence(WorkerResourceAsymptoticEvidence upper,
        ResourceExpression? lower, string? domainReason, CancellationToken cancellation)
    {
        upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        cancellation.ThrowIfCancellationRequested();
        var reason = domainReason ?? (lower is null ? "ResourceLowerMetricUnsupported" :
            upper.UnknownReason is not null ? "ResourceLowerUpperGrowthUnavailable" : null);
        if (reason != null)
        { return new() { UnknownReason = reason }; }
        var growth = AsymptoticEvidence(lower!, cancellation);
        if (growth.UnknownReason != null)
        { return new() { UnknownReason = growth.UnknownReason }; }
        if (growth.IsZero == true && upper.IsZero != true)
        { return new() { UnknownReason = "ResourcePositiveLowerBoundUnproved" }; }
        if (growth.InputOrdinal is not null && (growth.InputOrdinal != upper.InputOrdinal || growth.InputKind != upper.InputKind || growth.InputName != upper.InputName))
        { return new() { UnknownReason = "ResourceLowerInputMismatch" }; }
        // The nested growth describes the lower formula, not another source claim.
        growth.Lower = null;
        return new()
        {
            InferredBound = lower!.ToDisplayString(),
            LowerBound = string.Concat("Omega", growth.UpperBound.AsSpan(1)),
            TightBound = growth.UpperBound == upper.UpperBound ? string.Concat("Theta", upper.UpperBound.AsSpan(1)) : null,
            TightUnknownReason = growth.UpperBound == upper.UpperBound ? null : "ResourceTightnessUnproved",
            Growth = growth
        };
    }

    private static WorkerResourceAsymptoticEvidence AsymptoticEvidence(ResourceExpression upper, CancellationToken cancellation)
    {
        upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        var growth = ResourceAsymptoticGrowth.Analyze(upper, cancellation);
        return new()
        {
            UpperBound = growth.Order is { } order ? "O(" + order + ")" : null,
            PolynomialDegree = growth.PolynomialDegree,
            LogarithmicDegree = growth.LogarithmicDegree,
            IsZero = growth.IsZero,
            InputOrdinal = growth.Input?.Ordinal,
            InputKind = growth.Input?.Kind.ToString(),
            InputName = growth.Input?.DisplayName,
            UnknownReason = growth.UnknownReason,
            Lower = new() { UnknownReason = "ResourceLowerMetricUnsupported" }
        };
    }
}
