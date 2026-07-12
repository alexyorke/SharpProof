using System.Globalization;
using SharpProof.Attributes;
using SharpProof.Symbolic;

internal sealed class SymbolicCliExitGateFailure
{
    public SymbolicCliExitGateFailure(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}

internal static class SymbolicCliExitGateEvaluator
{
    public static IReadOnlyList<SymbolicCliExitGateFailure> Evaluate(
        SymbolicCliOptions options,
        object result)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (result == null) throw new ArgumentNullException(nameof(result));

        var failures = new List<SymbolicCliExitGateFailure>();
        EvaluateRuntimeHazards(options, result, failures);
        EvaluateInvariantProofs(options, result, failures);
        EvaluateCapabilities(options, result, failures);
        EvaluateComplexity(options, result, failures);
        EvaluateConservativeUnknowns(options, result, failures);
        EvaluateCompactTruncation(options, result, failures);
        EvaluateCompactThresholds(options, result, failures);
        return failures;
    }

    private static void EvaluateRuntimeHazards(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (!options.FailOnHazard || result is not SymbolicRuntimeHazardQueryResult hazards) return;
        if (hazards.HazardCount == 0) return;

        failures.Add(new SymbolicCliExitGateFailure(
            "runtime-hazards",
            $"hazards={hazards.HazardCount.ToString(CultureInfo.InvariantCulture)}; maximum=0."));
    }

    private static void EvaluateInvariantProofs(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (!options.FailOnUnprovenImplies || !TryGetInvariantMetrics(result, out var metrics)) return;

        var outcomes = metrics.ProofOutcomes;
        var unprovenCount = outcomes.TotalCount - outcomes.ProvenTrueCount;
        if (outcomes.TotalCount != 0 && unprovenCount == 0) return;

        failures.Add(new SymbolicCliExitGateFailure(
            "unproven-implies",
            "proofs=" + outcomes.TotalCount.ToString(CultureInfo.InvariantCulture) +
            "; provenTrue=" + outcomes.ProvenTrueCount.ToString(CultureInfo.InvariantCulture) +
            "; unproven=" + unprovenCount.ToString(CultureInfo.InvariantCulture) + "."));
    }

    private static void EvaluateCapabilities(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (result is not SymbolicCapabilityResult capabilities) return;

        if (options.FailOnCapabilityViolation)
        {
            var allowed = ExpandAllowedCapabilities(options.AllowedCapabilities.Aggregate(
                SymbolicCapability.None,
                static (current, capability) => current | capability));
            var disallowed = NormalizeCapabilities(capabilities.Capabilities) & ~allowed;
            if (disallowed != SymbolicCapability.None)
                failures.Add(new SymbolicCliExitGateFailure(
                    "capability-violation",
                    "observed=" + FormatCapabilities(capabilities.Capabilities) +
                    "; allowed=" + FormatCapabilities(allowed) +
                    "; disallowed=" + FormatCapabilities(disallowed) + "."));
        }

        if (options.FailOnCapabilityUnknown && capabilities.HasUnknowns)
            failures.Add(new SymbolicCliExitGateFailure(
                "capability-unknown",
                "unknownReasons=" + capabilities.UnknownReasons.Count.ToString(CultureInfo.InvariantCulture) +
                "; unknownSites=" + capabilities.Sites.Count(static site => site.IsUnknown)
                    .ToString(CultureInfo.InvariantCulture) + "."));
    }

    private static void EvaluateComplexity(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (result is not SymbolicComplexityResult complexity) return;

        var comparison = options.MaximumComplexity.HasValue
            ? CompareComplexity(complexity.Complexity.Kind, options.MaximumComplexity.Value)
            : ComplexityComparison.Within;
        if (options.MaximumComplexity.HasValue && comparison == ComplexityComparison.Exceeds)
            failures.Add(new SymbolicCliExitGateFailure(
                "complexity-exceeded",
                "actual=" + complexity.Complexity.Kind +
                "; maximum=" + options.MaximumComplexity.Value + "."));

        var isUnknown = complexity.Complexity.IsUnknown ||
                        complexity.Complexity.IsRecursiveUnknown ||
                        complexity.UnknownReasons.Count != 0 ||
                        comparison == ComplexityComparison.Incomparable;
        if (options.FailOnComplexityUnknown && isUnknown)
            failures.Add(new SymbolicCliExitGateFailure(
                "complexity-unknown",
                "actual=" + complexity.Complexity.Kind +
                (options.MaximumComplexity.HasValue
                    ? "; maximum=" + options.MaximumComplexity.Value
                    : string.Empty) +
                "; unknownReasons=" + complexity.UnknownReasons.Count.ToString(CultureInfo.InvariantCulture) + "."));
    }

    private static void EvaluateConservativeUnknowns(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (!options.MaximumConservativeUnknowns.HasValue ||
            !TryGetInvariantMetrics(result, out var metrics) ||
            metrics.ConservativeUnknownCount <= options.MaximumConservativeUnknowns.Value)
            return;

        failures.Add(new SymbolicCliExitGateFailure(
            "conservative-unknowns",
            "actual=" + metrics.ConservativeUnknownCount.ToString(CultureInfo.InvariantCulture) +
            "; maximum=" + options.MaximumConservativeUnknowns.Value.ToString(CultureInfo.InvariantCulture) + "."));
    }

    private static void EvaluateCompactTruncation(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (!options.FailOnCompactTruncation) return;

        var isTruncated = options.InvariantJson
            ? CreateInvariantResult(result, options).QuerySummary.HasTruncatedOutput
            : IsCompactResultTruncated(result, options);
        if (!isTruncated) return;

        failures.Add(new SymbolicCliExitGateFailure(
            "compact-truncation",
            "one or more configured compact output limits were exceeded."));
    }

    private static void EvaluateCompactThresholds(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        foreach (var threshold in options.CompactThresholds.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var actual = GetCompactMetric(result, threshold.Key);
            if (actual <= threshold.Value) continue;

            failures.Add(new SymbolicCliExitGateFailure(
                "compact-threshold." + threshold.Key,
                "actual=" + actual.ToString(CultureInfo.InvariantCulture) +
                "; maximum=" + threshold.Value.ToString(CultureInfo.InvariantCulture) + "."));
        }
    }

    private static bool TryGetInvariantMetrics(object result, out InvariantMetrics metrics)
    {
        switch (result)
        {
            case SymbolicSourceQueryResult point:
                metrics = new InvariantMetrics(
                    1,
                    point.InvariantQuery.UnknownFactCount,
                    point.ProofOutcomes,
                    point.Reachability == SymbolicReachability.Unknown ? 1 : 0);
                return true;
            case SymbolicLineQueryResult line:
                metrics = new InvariantMetrics(
                    line.ProgramPoints.Count,
                    line.InvariantQuery.UnknownFactCount,
                    line.ProgramPointSummary.ProofOutcomes,
                    line.Reachability.UnknownCount);
                return true;
            case SymbolicSpanQueryResult span:
                metrics = new InvariantMetrics(
                    span.ProgramPointCount,
                    span.InvariantQuery.UnknownFactCount,
                    span.ProgramPointSummary.ProofOutcomes,
                    span.Reachability.UnknownCount);
                return true;
            case SymbolicFileQueryResult file:
                metrics = new InvariantMetrics(
                    file.ProgramPointCount,
                    file.InvariantQuery.UnknownFactCount,
                    file.ProgramPointSummary.ProofOutcomes,
                    file.Reachability.UnknownCount);
                return true;
            default:
                metrics = default;
                return false;
        }
    }

    private static int GetCompactMetric(object result, string metric)
    {
        if (TryGetInvariantMetrics(result, out var invariant))
            return metric switch
            {
                "program-points" => invariant.ProgramPointCount,
                "conservative-unknowns" => invariant.ConservativeUnknownCount,
                "proof-unknowns" => invariant.ProofOutcomes.UnknownCount,
                "reachability-unknowns" => invariant.ReachabilityUnknownCount,
                _ => throw new InvalidOperationException("Unsupported invariant compact threshold metric: " + metric)
            };

        return result switch
        {
            SymbolicRuntimeHazardQueryResult hazards when metric == "hazards" => hazards.HazardCount,
            SymbolicCapabilityResult capabilities when metric == "capability-sites" => capabilities.Sites.Count,
            SymbolicCapabilityResult capabilities when metric == "capability-unknowns" =>
                Math.Max(capabilities.UnknownReasons.Count, capabilities.Sites.Count(static site => site.IsUnknown)),
            SymbolicComplexityResult complexity when metric == "complexity-drivers" => complexity.Drivers.Count,
            SymbolicComplexityResult complexity when metric == "complexity-unknowns" =>
                Math.Max(
                    complexity.UnknownReasons.Count,
                    complexity.Complexity.IsUnknown || complexity.Complexity.IsRecursiveUnknown ? 1 : 0),
            _ => throw new InvalidOperationException("Unsupported compact threshold metric: " + metric)
        };
    }

    private static bool IsCompactResultTruncated(object result, SymbolicCliOptions options)
    {
        return result switch
        {
            SymbolicFileQueryResult file => file.ToCompactResult(options.CreateCompactOptions()).Truncation.IsTruncated,
            SymbolicLineQueryResult line => line.ToCompactResult(options.CreateCompactOptions()).Truncation.IsTruncated,
            SymbolicSpanQueryResult span => span.ToCompactResult(options.CreateCompactOptions()).Truncation.IsTruncated,
            SymbolicSourceQueryResult point => point.ToCompactResult(options.CreateCompactOptions()).Truncation
                .IsTruncated,
            SymbolicRuntimeHazardQueryResult hazards => IsTruncated(
                hazards.ToCompactResult(options.CreateCompactHazardOptions()).Truncation),
            SymbolicCapabilityResult => false,
            SymbolicComplexityResult => false,
            _ => throw new InvalidOperationException("Unexpected query result type.")
        };
    }

    private static SymbolicInvariantQueryResult CreateInvariantResult(object result, SymbolicCliOptions options)
    {
        return result switch
        {
            SymbolicFileQueryResult file => file.ToInvariantQueryResult(options.CreateCompactOptions()),
            SymbolicLineQueryResult line => line.ToInvariantQueryResult(options.CreateCompactOptions()),
            SymbolicSpanQueryResult span => span.ToInvariantQueryResult(options.CreateCompactOptions()),
            SymbolicSourceQueryResult point => point.ToInvariantQueryResult(options.CreateCompactOptions()),
            _ => throw new InvalidOperationException("Unexpected invariant query result type.")
        };
    }

    private static bool IsTruncated(SymbolicCompactRuntimeHazardOutputTruncation truncation)
    {
        return truncation.Hazards || truncation.PathConditions;
    }

    private static SymbolicCapability NormalizeCapabilities(SymbolicCapability capabilities)
    {
        if ((capabilities & (SymbolicCapability.FileRead |
                             SymbolicCapability.FileWrite |
                             SymbolicCapability.Network |
                             SymbolicCapability.Console |
                             SymbolicCapability.Registry)) != 0)
            capabilities |= SymbolicCapability.IO;

        return capabilities;
    }

    private static SymbolicCapability ExpandAllowedCapabilities(SymbolicCapability capabilities)
    {
        if ((capabilities & SymbolicCapability.IO) != 0)
            capabilities |= SymbolicCapability.FileRead |
                            SymbolicCapability.FileWrite |
                            SymbolicCapability.Network |
                            SymbolicCapability.Console |
                            SymbolicCapability.Registry;

        return NormalizeCapabilities(capabilities);
    }

    private static string FormatCapabilities(SymbolicCapability capabilities)
    {
        if (capabilities == SymbolicCapability.None) return SymbolicCapability.None.ToString();

        return string.Join(", ", Enum.GetValues(typeof(SymbolicCapability))
            .Cast<SymbolicCapability>()
            .Where(capability => capability != SymbolicCapability.None && capabilities.HasFlag(capability)));
    }

    private static ComplexityComparison CompareComplexity(
        SymbolicComplexityKind actual,
        ComplexityKind maximum)
    {
        if (!TryMapActual(actual, out var actualClass)) return ComplexityComparison.Incomparable;
        var maximumClass = MapMaximum(maximum);
        if (actualClass == maximumClass || actualClass == ComplexityClass.Constant)
            return ComplexityComparison.Within;

        if (TryGetChainRank(actualClass, out var actualRank) &&
            TryGetChainRank(maximumClass, out var maximumRank))
            return actualRank <= maximumRank
                ? ComplexityComparison.Within
                : ComplexityComparison.Exceeds;

        return ComplexityComparison.Incomparable;
    }

    private static bool TryMapActual(SymbolicComplexityKind actual, out ComplexityClass complexityClass)
    {
        switch (actual)
        {
            case SymbolicComplexityKind.Constant:
                complexityClass = ComplexityClass.Constant;
                return true;
            case SymbolicComplexityKind.Linear:
                complexityClass = ComplexityClass.Linear;
                return true;
            case SymbolicComplexityKind.Quadratic:
                complexityClass = ComplexityClass.Quadratic;
                return true;
            case SymbolicComplexityKind.Product:
                complexityClass = ComplexityClass.Product;
                return true;
            case SymbolicComplexityKind.Max:
                complexityClass = ComplexityClass.Max;
                return true;
            default:
                complexityClass = default;
                return false;
        }
    }

    private static ComplexityClass MapMaximum(ComplexityKind maximum)
    {
        return maximum switch
        {
            ComplexityKind.Constant => ComplexityClass.Constant,
            ComplexityKind.Logarithmic => ComplexityClass.Logarithmic,
            ComplexityKind.Linear => ComplexityClass.Linear,
            ComplexityKind.Linearithmic => ComplexityClass.Linearithmic,
            ComplexityKind.Quadratic => ComplexityClass.Quadratic,
            ComplexityKind.Product => ComplexityClass.Product,
            ComplexityKind.Max => ComplexityClass.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "Complexity bound is not defined.")
        };
    }

    private static bool TryGetChainRank(ComplexityClass complexity, out int rank)
    {
        switch (complexity)
        {
            case ComplexityClass.Constant:
                rank = 0;
                return true;
            case ComplexityClass.Logarithmic:
                rank = 1;
                return true;
            case ComplexityClass.Linear:
                rank = 2;
                return true;
            case ComplexityClass.Linearithmic:
                rank = 3;
                return true;
            case ComplexityClass.Quadratic:
                rank = 4;
                return true;
            default:
                rank = -1;
                return false;
        }
    }

    private readonly record struct InvariantMetrics(
        int ProgramPointCount,
        int ConservativeUnknownCount,
        SymbolicProofOutcomeSummary ProofOutcomes,
        int ReachabilityUnknownCount);

    private enum ComplexityComparison
    {
        Within,
        Exceeds,
        Incomparable
    }

    private enum ComplexityClass
    {
        Constant,
        Logarithmic,
        Linear,
        Linearithmic,
        Quadratic,
        Product,
        Max
    }
}
