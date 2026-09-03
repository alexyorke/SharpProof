using System.Globalization;

namespace SharpProof.Effects;

internal static class EffectContractMappings
{
    private static readonly (EffectContractCapabilityKind Contract, EffectCapabilityKind Analysis,
        EffectContractKind Effect)[] Capabilities =
        EffectContractMappingCatalog.Capabilities;

    internal static readonly (EffectRegionKind Region, EffectContractKind Read,
        EffectContractKind Write, EffectRegionId? AnalysisRegion,
        bool ExpandParameters)[] RegionContracts =
        EffectContractMappingCatalog.RegionContracts;

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

    internal static (EffectContractCapabilityKind Capabilities, EffectContractKind Effects)
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
        foreach (var mapping in RegionContracts)
        {
            if (mapping.Region == region)
            {
                return isWrite ? mapping.Write : mapping.Read;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(region));
    }

    internal static EffectRegionSet ToAnalysisRegions(
        EffectContractKind effects, bool isWrite, int parameterCount)
    {
        var projections = ToAnalysisRegions(effects, parameterCount);
        return isWrite ? projections.Writes : projections.Reads;
    }

    internal static (EffectRegionSet Reads, EffectRegionSet Writes)
        ToAnalysisRegions(EffectContractKind effects, int parameterCount)
    {
        var reads = EffectRegionSet.Empty;
        var writes = EffectRegionSet.Empty;
        foreach (var mapping in RegionContracts)
        {
            var matchedRead = (effects & mapping.Read) != 0;
            var matchedWrite = (effects & mapping.Write) != 0;
            if (!matchedRead && !matchedWrite)
            {
                continue;
            }

            var regions = mapping.ExpandParameters
                ? ParameterRegions(parameterCount)
                : mapping.AnalysisRegion is { } region
                    ? EffectRegionSet.Create(region)
                    : EffectRegionSet.Empty;
            if (matchedRead)
            {
                reads = reads.Union(regions);
            }

            if (matchedWrite)
            {
                writes = writes.Union(regions);
            }
        }

        return (reads, writes);
    }

    internal static EffectRegionSet ParameterRegions(int count)
    {
        return EffectRegionSet.Create(Enumerable.Range(0, count).Select(EffectRegionId.Parameter));
    }

    internal static string EvidenceName(Enum value)
    {
        if (value == null)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var rule = EffectEvidenceCatalog.Rules.FirstOrDefault(
            candidate => candidate.Type == value.GetType());
        if (rule.Type == null)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var numeric = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        var valid = rule.Flags
            ? (numeric & ~rule.Mask) == 0
            : rule.Values.Contains(numeric);
        return valid
            ? value.ToString()
            : throw new ArgumentOutOfRangeException(nameof(value));
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
