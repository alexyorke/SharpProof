using System.Collections.Immutable;

namespace SharpProof.Gates.Performance;

internal sealed record PackageBuildSample
{
    internal PackageBuildSample(
        int index,
        bool unannotatedAdvisoryFirst,
        double baselineMilliseconds,
        double unannotatedAdvisoryMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ValidateElapsedTime(
            baselineMilliseconds,
            nameof(baselineMilliseconds));
        ValidateElapsedTime(
            unannotatedAdvisoryMilliseconds,
            nameof(unannotatedAdvisoryMilliseconds));

        Index = index;
        UnannotatedAdvisoryFirst = unannotatedAdvisoryFirst;
        BaselineMilliseconds = baselineMilliseconds;
        UnannotatedAdvisoryMilliseconds =
            unannotatedAdvisoryMilliseconds;
        Ratio = unannotatedAdvisoryMilliseconds / baselineMilliseconds;
        if (!double.IsFinite(Ratio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(unannotatedAdvisoryMilliseconds),
                unannotatedAdvisoryMilliseconds,
                "The advisory-to-baseline ratio must be finite.");
        }
    }

    public int Index
    {
        get;
    }

    public bool UnannotatedAdvisoryFirst
    {
        get;
    }

    public double BaselineMilliseconds
    {
        get;
    }

    public double UnannotatedAdvisoryMilliseconds
    {
        get;
    }

    public double Ratio
    {
        get;
    }

    private static void ValidateElapsedTime(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Elapsed time must be finite and positive.");
        }
    }
}

internal sealed record PackageBuildStatistics(
    double OrderBalancedMedianRatio,
    double RawMedianRatio,
    double BaselineFirstMedianRatio,
    double UnannotatedAdvisoryFirstMedianRatio,
    double P95Ratio,
    ImmutableArray<double> OrderBalancedRatios);

internal static class PackageBuildEstimator
{
    internal const string Version =
        "paired-interleaved-geometric-order-balanced-median-v1";

    internal static PackageBuildStatistics Estimate(
        IReadOnlyCollection<PackageBuildSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException(
                "At least one package-build sample is required.",
                nameof(samples));
        }

        var ordered = samples
            .OrderBy(static sample => sample.Index)
            .ToImmutableArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Index != index)
            {
                throw new ArgumentException(
                    "Package-build sample indices must be unique and contiguous.",
                    nameof(samples));
            }
        }

        var baselineFirstBuilder = ImmutableArray.CreateBuilder<double>(
            ordered.Length / 2);
        var unannotatedAdvisoryFirstBuilder =
            ImmutableArray.CreateBuilder<double>(ordered.Length / 2);
        var ratiosBuilder = ImmutableArray.CreateBuilder<double>(
            ordered.Length);
        foreach (var sample in ordered)
        {
            ratiosBuilder.Add(sample.Ratio);
            if (sample.UnannotatedAdvisoryFirst)
            {
                unannotatedAdvisoryFirstBuilder.Add(sample.Ratio);
            }
            else
            {
                baselineFirstBuilder.Add(sample.Ratio);
            }
        }
        if (baselineFirstBuilder.Count == 0 ||
            baselineFirstBuilder.Count != unannotatedAdvisoryFirstBuilder.Count)
        {
            throw new ArgumentException(
                "Package-build samples must balance baseline-first and " +
                "unannotated-advisory-first execution orders.",
                nameof(samples));
        }
        var baselineFirst = baselineFirstBuilder.MoveToImmutable();
        var unannotatedAdvisoryFirst =
            unannotatedAdvisoryFirstBuilder.MoveToImmutable();

        var balancedRatios = ImmutableArray.CreateBuilder<double>(
            ordered.Length / 2);
        for (var index = 0; index < ordered.Length; index += 2)
        {
            var first = ordered[index];
            var second = ordered[index + 1];
            if (first.UnannotatedAdvisoryFirst ==
                second.UnannotatedAdvisoryFirst)
            {
                throw new ArgumentException(
                    "Each adjacent package-build sample pair must contain " +
                    "opposite execution orders.",
                    nameof(samples));
            }

            balancedRatios.Add(GeometricMean(
                first.Ratio,
                second.Ratio));
        }

        var baselineFirstSorted = ValidateAndSort(baselineFirst);
        var unannotatedAdvisoryFirstSorted =
            ValidateAndSort(unannotatedAdvisoryFirst);
        var balanced = balancedRatios.MoveToImmutable();
        var balancedSorted = ValidateAndSort(balanced);
        var ratiosSorted = ValidateAndSort(ratiosBuilder.MoveToImmutable());
        return new PackageBuildStatistics(
            MedianSorted(balancedSorted),
            MedianSorted(ratiosSorted),
            MedianSorted(baselineFirstSorted),
            MedianSorted(unannotatedAdvisoryFirstSorted),
            NearestRankPercentileSorted(ratiosSorted, 0.95),
            balanced);
    }

    internal static double Median(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = ValidateAndSort(values);
        return MedianSorted(sorted);
    }

    private static double GeometricMean(double first, double second)
    {
        return Math.Sqrt(first) * Math.Sqrt(second);
    }

    private static double Midpoint(double first, double second)
    {
        var lower = Math.Min(first, second);
        var upper = Math.Max(first, second);
        return lower + ((upper - lower) / 2);
    }

    internal static double NearestRankPercentile(
        IEnumerable<double> values,
        double rank,
        bool requireFinitePositive = true)
    {
        var sorted = values.OrderBy(static value => value).ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException(
                "At least one sample is required.",
                nameof(values));
        }

        if (requireFinitePositive &&
            sorted.Any(static value => !double.IsFinite(value) || value <= 0))
        {
            throw new ArgumentException(
                "Every sample must be finite and positive.",
                nameof(values));
        }

        return NearestRankPercentileSorted(sorted, rank);
    }

    private static double MedianSorted(double[] sorted)
    {
        var upperIndex = sorted.Length / 2;
        return (sorted.Length & 1) != 0
            ? sorted[upperIndex]
            : Midpoint(sorted[upperIndex - 1], sorted[upperIndex]);
    }

    private static double NearestRankPercentileSorted(
        double[] sorted,
        double rank)
    {
        var index = Math.Clamp(
            (int)Math.Ceiling(rank * sorted.Length) - 1,
            0,
            sorted.Length - 1);
        return sorted[index];
    }

    private static double[] ValidateAndSort(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(static value => value).ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException(
                "At least one sample is required.",
                nameof(values));
        }

        if (sorted.Any(static value => !double.IsFinite(value) || value <= 0))
        {
            throw new ArgumentException(
                "Every sample must be finite and positive.",
                nameof(values));
        }

        return sorted;
    }
}
