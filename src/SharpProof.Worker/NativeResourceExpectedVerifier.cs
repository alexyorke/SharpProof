namespace SharpProof.Worker;

internal sealed record NativeResourceExpectedCheck(ResourceBooleanDistribution? Distribution,
    ResourceExpression? FalseUpper, ResourceExpression? TrueUpper, ResourceExpression? ExpectedUpper, string? UnknownReason);

// The compiler specializes the entire callable for each immutable entry value.
// Both costs are checked; averaging never replaces the ordinary worst-case proof.
internal static class NativeResourceExpectedVerifier
{
    internal const string ModelId = WorkerResourceModels.Expected;

    internal static NativeResourceExpectedCheck Verify(ResourceProgram program, ResourceCostMetric metric, string callableId,
        CompilerTotalCallablePreparation owner, ResourceRuntimeLayoutContext? layout, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        if (program.Kind != ResourceProgramKind.BooleanDistribution || program.Distribution is not { } distribution)
        { return Unknown("ResourceInputModelNotDeclared"); }
        var checkedProgram = NativeResourceProgramVerifier.Verify(program, callableId, owner, cancellation);
        if (!checkedProgram.Verified)
        { return Unknown(checkedProgram.UnknownReason ?? "ResourceExpectedProgramUnproved"); }
        if (!owner.CallPreconditions.IsEmpty || owner.Clauses.Any(static clause => clause is null ? throw new ArgumentNullException(nameof(clause)) : clause.Kind == CompilerContractKind.Requires &&
            (clause.Value is not IrBooleanTerm { Value: true } || clause.Safe is not IrBooleanTerm { Value: true })))
        { return Unknown("ResourceInputModelSupportUnproved"); }
        if (metric is not (ResourceCostMetric.LoopBodyEntries or ResourceCostMetric.ArrayAllocations or ResourceCostMetric.AllocatedElements or ResourceCostMetric.AllocatedBytes))
        { return Unknown("ResourceExpectedMetricUnsupported"); }
        try
        {
            var no = Cost(program.Children[0]);
            var yes = Cost(program.Children[1]);
            if (no is null || yes is null)
            { return Unknown("ResourceExpectedConditionalCostUnproved"); }
            return new(distribution, no, yes, distribution.ExpectedUpper(no, yes), null);
        }
        catch (ResourceExpressionLimitException)
        { return Unknown("ResourceExpectedProofBudgetExceeded"); }

        ResourceExpression? Cost(ResourceProgram branch)
        {
            branch = ArgumentNullGuard.NotNull(branch, nameof(branch));
            if (metric == ResourceCostMetric.AllocatedBytes)
            {
                return layout is null ? null : NativeResourceAllocationVerifier.Verify(branch, callableId, owner, layout, cancellation).ConditionalUpperBound;
            }
            var result = NativeResourceProgramVerifier.Verify(branch, callableId, owner, cancellation).Cost;
            return metric switch
            {
                ResourceCostMetric.LoopBodyEntries => result?.LoopEntries,
                ResourceCostMetric.ArrayAllocations => result?.Arrays,
                _ => result?.Elements
            };
        }
    }

    internal static WorkerResourceExpectedEvidence Project(NativeResourceExpectedCheck check)
    {
        check = ArgumentNullGuard.NotNull(check, nameof(check));
        if (check.UnknownReason is { } reason)
        { return new() { UnknownReason = reason }; }
        if (check.Distribution is not { } distribution || check.FalseUpper is null || check.TrueUpper is null || check.ExpectedUpper is null)
        { return new() { UnknownReason = "ResourceExpectedEvidenceIncomplete" }; }
        return new()
        {
            InputOrdinal = distribution.Ordinal,
            InputName = distribution.Name,
            TrueWeight = distribution.TrueWeight,
            FalseWeight = distribution.FalseWeight,
            FalseUpperBound = check.FalseUpper.ToDisplayString(),
            TrueUpperBound = check.TrueUpper.ToDisplayString(),
            UpperBound = check.ExpectedUpper.ToDisplayString()
        };
    }

    private static NativeResourceExpectedCheck Unknown(string reason)
    {
        return new(null, null, null, null, ArgumentNullGuard.NotNull(reason, nameof(reason)));
    }
}
