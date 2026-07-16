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
        if (!options.FailOnUnprovenImplies ||
            result is not SymbolicQueryResult queryResult)
            return;

        var outcomes = queryResult.ProgramPointSummary.ProofOutcomes;
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
            var allowed = SymbolicCapabilityFacts.ExpandAllowed(options.AllowedCapabilities.Aggregate(
                SymbolicCapability.None,
                static (current, capability) => current | capability));
            var disallowed = SymbolicCapabilityFacts.Normalize(capabilities.Capabilities) & ~allowed;
            if (disallowed != SymbolicCapability.None)
                failures.Add(new SymbolicCliExitGateFailure(
                    "capability-violation",
                    "observed=" + SymbolicCapabilityFacts.Format(capabilities.Capabilities) +
                    "; allowed=" + SymbolicCapabilityFacts.Format(allowed) +
                    "; disallowed=" + SymbolicCapabilityFacts.Format(disallowed) + "."));
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
            result is not SymbolicQueryResult queryResult ||
            queryResult.InvariantQuery.UnknownFactCount <= options.MaximumConservativeUnknowns.Value)
            return;

        failures.Add(new SymbolicCliExitGateFailure(
            "conservative-unknowns",
            "actual=" + queryResult.InvariantQuery.UnknownFactCount.ToString(CultureInfo.InvariantCulture) +
            "; maximum=" + options.MaximumConservativeUnknowns.Value.ToString(CultureInfo.InvariantCulture) + "."));
    }

    private static void EvaluateCompactTruncation(
        SymbolicCliOptions options,
        object result,
        ICollection<SymbolicCliExitGateFailure> failures)
    {
        if (!options.FailOnCompactTruncation) return;

        var isTruncated = options.InvariantJson
            ? result is SymbolicQueryResult queryResult
                ? queryResult.ToInvariantQueryResult(options.CreateCompactOptions()).QuerySummary.HasTruncatedOutput
                : throw new InvalidOperationException("Unexpected invariant query result type.")
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

    private static int GetCompactMetric(object result, string metric)
    {
        if (result is SymbolicQueryResult queryResult)
            return metric switch
            {
                "program-points" => queryResult.ProgramPointCount,
                "conservative-unknowns" => queryResult.InvariantQuery.UnknownFactCount,
                "proof-unknowns" => queryResult.ProgramPointSummary.ProofOutcomes.UnknownCount,
                "reachability-unknowns" => queryResult.Reachability.UnknownCount,
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
        if (result is SymbolicQueryResult queryResult)
            return queryResult.ToCompactResult(options.CreateCompactOptions()).Truncation.IsTruncated;

        return result switch
        {
            SymbolicRuntimeHazardQueryResult hazards => IsTruncated(
                SymbolicCompactRuntimeHazardProjection.Create(hazards, options.CreateCompactHazardOptions())),
            SymbolicCapabilityResult => false,
            SymbolicComplexityResult => false,
            _ => throw new InvalidOperationException("Unexpected query result type.")
        };
    }

    private static bool IsTruncated(SymbolicCompactRuntimeHazardProjection projection)
    {
        return projection.HazardsTruncated || projection.PathConditionsTruncated;
    }

    private static ComplexityComparison CompareComplexity(
        SymbolicComplexityKind actual,
        ComplexityKind maximum)
    {
        if (!TryMapActual(actual, out var actualClass)) return ComplexityComparison.Incomparable;
        var maximumClass = MapMaximum(maximum);
        if (actualClass == maximumClass || actualClass == ComplexityClass.Constant)
            return ComplexityComparison.Within;

        if (maximumClass == ComplexityClass.Constant)
            return ComplexityComparison.Exceeds;

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
