namespace SharpProof.Effects;

internal static class EffectSummaryOperations
{
    private static EffectSummaryDomain Domain => EffectSummaryDomain.Instance;

    internal static EffectSummary Join(EffectSummary first, EffectSummary second)
    {
        first = RepositoryParameterGuard.NotNull(first, nameof(first));
        second = RepositoryParameterGuard.NotNull(second, nameof(second));
        return Domain.Join(first, second);
    }

    internal static EffectSummary Join(
        EffectSummary first,
        EffectSummary second,
        EffectSummary third)
    {
        first = RepositoryParameterGuard.NotNull(first, nameof(first));
        second = RepositoryParameterGuard.NotNull(second, nameof(second));
        third = RepositoryParameterGuard.NotNull(third, nameof(third));
        return Domain.Join(Domain.Join(first, second), third);
    }

    internal static EffectSummary JoinFrom(EffectSummary result, IEnumerable<EffectSummary> summaries)
    {
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        summaries = ArgumentNullGuard.NotNull(summaries, nameof(summaries));
        foreach (var summary in summaries)
        {
            result = Domain.Join(result, summary);
        }

        return result;
    }

    internal static EffectSummary Read(EffectRegionSet regions /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */)
    {
        return Create(reads: regions);
    }

    internal static EffectSummary Write(EffectRegionSet regions /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */)
    {
        return Create(writes: regions);
    }

    internal static EffectSummary Allocate(EffectAllocationKind allocation)
    {
        return Create(allocation: allocation);
    }

    internal static EffectSummary Throw(
        EffectThrowSet exceptions /* SPINT001: all values valid - Empty, Unknown, and every exception set are valid */,
        EffectUncertainty uncertainty = EffectUncertainty.None)
    {
        return Create(
            allocation: exceptions.IsEmpty
                ? EffectAllocationKind.None
                : EffectAllocationKind.Managed,
            throws: exceptions,
            completeness: exceptions.IncludesUnknown
                ? EffectCompleteness.Incomplete
                : EffectCompleteness.Complete,
            uncertainty: uncertainty);
    }

    internal static EffectSummary WithThrows(EffectSummary summary, EffectThrowSet exceptions /* SPINT001: all values valid - Empty, Unknown, and every exception set are valid */)
    {
        summary = ArgumentNullGuard.NotNull(summary, nameof(summary));
        if (summary.IsBottom)
        {
            return summary;
        }

        return new EffectSummary(
            summary.Reads, summary.Writes, summary.Allocation, summary.Capabilities,
            exceptions, summary.Termination, summary.Completeness, summary.Uncertainty,
            summary.AnalysisIncompleteReason);
    }

    internal static EffectSummary ExceptionConstructionThrow(
        EffectSummary construction,
        EffectThrowSet exceptions /* SPINT001: all values valid - Empty, Unknown, and every exception set are valid */,
        EffectUncertainty uncertainty = EffectUncertainty.None)
    {
        construction = RepositoryParameterGuard.NotNull(construction, nameof(construction));
        var sequence = Domain.Join(
            construction,
            Throw(exceptions, uncertainty));
        return new EffectSummary(
            sequence.Reads,
            sequence.Writes,
            sequence.Allocation,
            sequence.Capabilities,
            sequence.Throws,
            EffectTermination.Unknown,
            sequence.Completeness,
            sequence.Uncertainty,
            sequence.AnalysisIncompleteReason);
    }

    internal static EffectSummary Capability(EffectCapabilityKind capabilities)
    {
        return Create(capabilities: new EffectCapabilitySet(capabilities));
    }

    internal static EffectSummary DirectCall()
    {
        return Create(uncertainty: EffectUncertainty.DirectCall);
    }

    internal static EffectSummary UnknownBoundary(EffectUncertainty uncertainty)
    {
        return new(
            EffectRegionSet.Unknown, EffectRegionSet.Unknown,
            EffectAllocationKind.Unknown, EffectCapabilitySet.Unknown,
            EffectThrowSet.Unknown, EffectTermination.Unknown,
            EffectCompleteness.Incomplete, uncertainty);
    }

    internal static EffectSummary TypeInitializationBoundary()
    {
        return new(
            EffectRegionSet.Empty, EffectRegionSet.Empty,
            EffectAllocationKind.Unknown, EffectCapabilitySet.Unknown,
            EffectThrowSet.Unknown, EffectTermination.Unknown,
            EffectCompleteness.Incomplete, EffectUncertainty.UnmodeledCall);
    }

    internal static EffectSummary IncompleteAnalysis(EffectAnalysisIncompleteReason reason)
    {
        return new(
            EffectRegionSet.Empty, EffectRegionSet.Empty,
            EffectAllocationKind.None, EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            reason == EffectAnalysisIncompleteReason.CyclicControlFlow
                ? EffectTermination.MayDiverge
                : EffectTermination.Unknown,
            EffectCompleteness.Incomplete, EffectUncertainty.None, reason);
    }

    internal static EffectSummary Unsupported()
    {
        return UnknownBoundary(EffectUncertainty.UnsupportedOperation);
    }

    internal static EffectSummary MayDiverge()
    {
        return Create(termination: EffectTermination.MayDiverge);
    }

    internal static EffectSummary IncompleteDivergence(EffectSummary summary)
    {
        summary = ArgumentNullGuard.NotNull(summary, nameof(summary));
        return new EffectSummary(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.MayDiverge,
            EffectCompleteness.Incomplete,
            summary.Uncertainty,
            summary.AnalysisIncompleteReason);
    }

    internal static EffectSummary Remap(
        EffectSummary summary,
        EffectRegionSet receiver /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */,
        ImmutableArray<EffectRegionSet> arguments)
    {

        RepositoryParameterGuard.ThrowIfMissing(arguments.IsDefault, nameof(arguments));
        summary = RepositoryParameterGuard.NotNull(summary, nameof(summary));
        return Remap(summary, receiver, receiver, arguments);
    }

    internal static EffectSummary Remap(
        EffectSummary summary,
        EffectRegionSet receiver /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */,
        EffectRegionSet writeReceiver /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */,
        ImmutableArray<EffectRegionSet> arguments)
    {
        summary = ArgumentNullGuard.NotNull(summary, nameof(summary));
        if (summary.IsBottom)
        {
            return summary;
        }

        RepositoryParameterGuard.ThrowIfMissing(arguments.IsDefault, nameof(arguments));

        return new EffectSummary(
            RemapRegions(summary.Reads, receiver, arguments),
            RemapRegions(summary.Writes, writeReceiver, arguments),
            summary.Allocation, summary.Capabilities,
            summary.Throws, summary.Termination,
            summary.Completeness, summary.Uncertainty, summary.AnalysisIncompleteReason);
    }

    private static EffectRegionSet RemapRegions(
        EffectRegionSet regions, EffectRegionSet receiver /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */, ImmutableArray<EffectRegionSet> arguments)
    {

        if (regions.IsUnknown)
        {
            return EffectRegionSet.Unknown;
        }

        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        var mappedRegions = ImmutableArray.CreateBuilder<EffectRegionId>();
        var hasUnknown = false;
        foreach (var region in regions.Regions)
        {
            var mapped = region.Kind switch
            {
                EffectRegionKind.Receiver => receiver,
                EffectRegionKind.Parameter when region.Ordinal < arguments.Length => arguments[region.Ordinal],
                EffectRegionKind.Parameter => EffectRegionSet.Unknown,
                _ => EffectRegionSet.Create(region)
            };
            if (mapped.IsUnknown)
            {
                hasUnknown = true;
            }
            else
            {
                mappedRegions.AddRange(mapped.Regions);
            }
        }

        return hasUnknown
            ? EffectRegionSet.Unknown
            : EffectRegionSet.Create(mappedRegions.ToImmutable());
    }

    private static EffectSummary Create(
        EffectRegionSet reads /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */ = default,
        EffectRegionSet writes /* SPINT001: all values valid - Empty, Unknown, and every region set are valid */ = default,
        EffectAllocationKind allocation = EffectAllocationKind.None,
        EffectCapabilitySet capabilities /* SPINT001: all values valid - Empty, Unknown, and every constructed capability set are valid */ = default,
        EffectThrowSet throws /* SPINT001: all values valid - Empty, Unknown, and every exception set are valid */ = default,
        EffectTermination termination = EffectTermination.Terminates,
        EffectCompleteness completeness = EffectCompleteness.Complete,
        EffectUncertainty uncertainty = EffectUncertainty.None)
    {
        return new(
            reads, writes, allocation, capabilities,
            throws, termination, completeness, uncertainty);
    }
}
