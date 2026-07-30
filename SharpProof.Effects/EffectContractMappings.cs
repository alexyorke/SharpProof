namespace SharpProof.Effects;

internal static class EffectContractMappings
{
    private static readonly ImmutableArray<(
        EffectContractCapabilityKind Contract, EffectCapabilityKind Analysis,
        EffectContractKind Effect)> Capabilities = [
        (EffectContractCapabilityKind.IO, EffectCapabilityKind.IO, EffectContractKind.None),
        (EffectContractCapabilityKind.FileRead, EffectCapabilityKind.FileRead, EffectContractKind.None),
        (EffectContractCapabilityKind.FileWrite, EffectCapabilityKind.FileWrite, EffectContractKind.None),
        (EffectContractCapabilityKind.Network, EffectCapabilityKind.Network, EffectContractKind.None),
        (EffectContractCapabilityKind.Console, EffectCapabilityKind.Console, EffectContractKind.None),
        (EffectContractCapabilityKind.Process, EffectCapabilityKind.Process, EffectContractKind.None),
        (EffectContractCapabilityKind.Environment, EffectCapabilityKind.Environment, EffectContractKind.None),
        (EffectContractCapabilityKind.Registry, EffectCapabilityKind.Registry, EffectContractKind.None),
        (EffectContractCapabilityKind.Clock, EffectCapabilityKind.Clock, EffectContractKind.UsesNondeterminism),
        (EffectContractCapabilityKind.Randomness, EffectCapabilityKind.Randomness, EffectContractKind.UsesNondeterminism),
        (EffectContractCapabilityKind.Reflection, EffectCapabilityKind.Reflection, EffectContractKind.UsesReflection),
        (EffectContractCapabilityKind.Synchronization, EffectCapabilityKind.Synchronization, EffectContractKind.Synchronizes),
        (EffectContractCapabilityKind.NativeInterop, EffectCapabilityKind.NativeInterop, EffectContractKind.UsesNativeCode)
    ];

    internal static EffectCapabilityKind ToAnalysisCapabilities(EffectContractCapabilityKind source)
    {
        if ((source & ~EffectContractMetadata.AllCapabilities) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var result = EffectCapabilityKind.None;
        foreach (var pair in Capabilities)
        {
            if ((source & pair.Contract) != 0)
            {
                result |= pair.Analysis;
            }
        }

        return result;
    }

    internal static EffectContractCapabilityKind ToContractCapabilities(EffectCapabilityKind source)
    {
        return ProjectCapabilities(source).Capabilities;
    }

    internal static EffectContractKind ToContractEffects(EffectCapabilityKind source)
    {
        return ProjectCapabilities(source).Effects;
    }

    private static (EffectContractCapabilityKind Capabilities, EffectContractKind Effects)
        ProjectCapabilities(EffectCapabilityKind source)
    {
        if ((source & ~EffectCapabilityKind.AllKnown) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var capabilities = EffectContractCapabilityKind.None;
        var effects = EffectContractKind.None;
        foreach (var pair in Capabilities)
        {
            if ((source & pair.Analysis) == 0)
            {
                continue;
            }

            capabilities |= pair.Contract;
            effects |= pair.Effect;
        }
        return (capabilities, effects);
    }

    internal static EffectContractKind ToContractRegion(EffectRegionKind region, bool isWrite)
    {
        return (region, isWrite) switch
        {
            (EffectRegionKind.Receiver, false) => EffectContractKind.ReadsReceiverState,
            (EffectRegionKind.Parameter, false) => EffectContractKind.ReadsArgumentState,
            (EffectRegionKind.Captured, false) => EffectContractKind.ReadsCapturedState,
            (EffectRegionKind.Static, false) => EffectContractKind.ReadsStaticState,
            (EffectRegionKind.Ambient, false) => EffectContractKind.ReadsAmbientState,
            (EffectRegionKind.Receiver, true) => EffectContractKind.WritesReceiverState,
            (EffectRegionKind.Parameter, true) => EffectContractKind.WritesArgumentState,
            (EffectRegionKind.Captured, true) => EffectContractKind.WritesCapturedState,
            (EffectRegionKind.Static, true) => EffectContractKind.WritesStaticState,
            (EffectRegionKind.Ambient, true) => EffectContractKind.WritesAmbientState,
            (EffectRegionKind.Fresh or EffectRegionKind.Unknown, _) => EffectContractKind.None,
            _ => throw new ArgumentOutOfRangeException(nameof(region))
        };
    }

    internal static EffectRegionSet ToAnalysisRegions(
        EffectContractKind effects, bool isWrite, int parameterCount)
    {
        var result = EffectRegionSet.Empty;
        bool Has(EffectContractKind read, EffectContractKind write)
        {
            return (effects & (isWrite ? write : read)) != 0;
        }

        if (Has(EffectContractKind.ReadsReceiverState, EffectContractKind.WritesReceiverState))
        {
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Receiver));
        }

        if (Has(EffectContractKind.ReadsArgumentState, EffectContractKind.WritesArgumentState))
        {
            result = result.Union(ParameterRegions(parameterCount));
        }

        if (Has(EffectContractKind.ReadsCapturedState, EffectContractKind.WritesCapturedState))
        {
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Captured(0)));
        }

        if (Has(EffectContractKind.ReadsStaticState, EffectContractKind.WritesStaticState))
        {
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Static()));
        }

        if (Has(EffectContractKind.ReadsAmbientState, EffectContractKind.WritesAmbientState))
        {
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
        }

        return result;
    }

    internal static EffectRegionSet ParameterRegions(int count)
    {
        return EffectRegionSet.Create(Enumerable.Range(0, count).Select(EffectRegionId.Parameter));
    }

    internal static string EvidenceName(Enum value)
    {
        return value switch
        {
            EffectContractKind effects when
                (effects & ~EffectContractMetadata.AllEffects) == 0 => effects.ToString(),
            EffectContractCapabilityKind capabilities when
                (capabilities & ~EffectContractMetadata.AllCapabilities) == 0 => capabilities.ToString(),
            EffectAllocationKind.None or EffectAllocationKind.Managed or EffectAllocationKind.Native or
                EffectAllocationKind.ManagedAndNative or EffectAllocationKind.Unknown => value.ToString(),
            EffectCompleteness.Complete or EffectCompleteness.Incomplete => value.ToString(),
            EffectAnalysisIncompleteReason reason when
                (reason & ~(EffectAnalysisIncompleteReason.BlockBudgetExceeded |
                            EffectAnalysisIncompleteReason.OperationBudgetExceeded |
                            EffectAnalysisIncompleteReason.CyclicControlFlow |
                            EffectAnalysisIncompleteReason
                                .CallPreconditionNotProven)) == 0 => reason.ToString(),
            EffectUncertainty uncertainty when
                (uncertainty & ~EffectUncertainty.Unknown) == 0 => uncertainty.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    internal static bool IsObservablePure(EffectSummary summary)
    {
        return summary.Capabilities.IsEmpty &&
        !summary.Reads.Regions.Any(static region =>
            region.Kind is EffectRegionKind.Ambient or EffectRegionKind.Captured or EffectRegionKind.Static) &&
        summary.Writes.Regions.All(static region => region.Kind == EffectRegionKind.Fresh);
    }

    internal static bool IsPurityViolation(EffectDirectWitness witness)
    {
        return witness.Capabilities != EffectContractCapabilityKind.None ||
        (witness.Effects & ImpureState) != 0;
    }

    internal static bool Covers(EffectSummary actual, EffectSummary declared)
    {
        var actualProjection = EffectSummaryProjector.Project(actual);
        var declaredProjection = EffectSummaryProjector.Project(declared);
        return actualProjection.IsComplete &&
            (actualProjection.Effects & ~declaredProjection.Effects) == 0 &&
            (actualProjection.Capabilities & ~declaredProjection.Capabilities) == 0 &&
            ExceptionsCovered(actual.Throws, declared.Throws);
    }

    internal static bool Violates(EffectDirectWitness witness, EffectSummary declared)
    {
        var projection = EffectSummaryProjector.Project(declared);
        return (witness.Effects & ~projection.Effects) != 0 ||
            (witness.Capabilities & ~projection.Capabilities) != 0 ||
            witness.ExceptionType != null &&
            !ExceptionsCovered(EffectThrowSet.Create([witness.ExceptionType]), declared.Throws);
    }

    private static bool ExceptionsCovered(EffectThrowSet actual, EffectThrowSet declared)
    {
        return actual.IsEmpty ||
        declared.IncludesUnknown ||
        !actual.IncludesUnknown &&
        actual.Types.All(thrown =>
            declared.Types.Any(allowed => EffectTypeFacts.IsDerivedFrom(thrown, allowed)));
    }

    private const EffectContractKind ImpureState =
        EffectContractKind.ReadsCapturedState | EffectContractKind.ReadsStaticState |
        EffectContractKind.ReadsAmbientState | EffectContractKind.WritesReceiverState |
        EffectContractKind.WritesArgumentState | EffectContractKind.WritesCapturedState |
        EffectContractKind.WritesStaticState | EffectContractKind.WritesAmbientState;
}
