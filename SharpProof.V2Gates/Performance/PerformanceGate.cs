using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer;

namespace SharpProof.V2Gates.Performance;

public sealed record PerformanceGateResult(
    bool Passed,
    int Warmups,
    int Samples,
    double MedianRatio,
    double P95Ratio,
    long BaselineRetainedBytes,
    long DefaultOffRetainedBytes,
    double RetainedMemoryRatio,
    double RetainedMemoryIncreaseMiB,
    int EnabledRetainedCompilationCount,
    double EnabledRetainedMemoryIncreaseMiB,
    int IdeEdits,
    double IdeEditP95Milliseconds,
    double IdeEditMaximumMilliseconds,
    int IdeDiagnosticReplayFailureCount,
    double CancellationP95Milliseconds,
    double ForcedTerminationMilliseconds,
    ImmutableArray<string> Failures);

public static class PerformanceGate {
    private const int TimingIterationsPerSample = 50;
    private const int RetainedCompilationCount = 40;

    public static async Task<PerformanceGateResult> RunAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default) {
        var contract = AcceptancePerformanceContract.Load(repositoryRoot);
        ValidateContract(contract);
        var source = CreateDefaultOffSource(320);
        var compilation = AnalyzerGateHost.CreateCompilation(
            source,
            "DefaultOffPerformance");

        for (var index = 0; index < contract.Warmups; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            MeasureNoAnalyzerBatch(
                source,
                "Baseline",
                TimingIterationsPerSample,
                cancellationToken);
            MeasureNoAnalyzerBatch(
                source,
                "DefaultOff",
                TimingIterationsPerSample,
                cancellationToken);
        }

        var baselineTimes = new double[contract.Samples];
        var defaultOffTimes = new double[contract.Samples];
        for (var index = 0; index < contract.Samples; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            if ((index & 1) == 0) {
                baselineTimes[index] = MeasureNoAnalyzerBatch(
                    source,
                    "Baseline",
                    TimingIterationsPerSample,
                    cancellationToken);
                defaultOffTimes[index] = MeasureNoAnalyzerBatch(
                    source,
                    "DefaultOff",
                    TimingIterationsPerSample,
                    cancellationToken);
            }
            else {
                defaultOffTimes[index] = MeasureNoAnalyzerBatch(
                    source,
                    "DefaultOff",
                    TimingIterationsPerSample,
                    cancellationToken);
                baselineTimes[index] = MeasureNoAnalyzerBatch(
                    source,
                    "Baseline",
                    TimingIterationsPerSample,
                    cancellationToken);
            }
        }

        var medianRatio = Ratio(
            Percentile(defaultOffTimes, 0.50),
            Percentile(baselineTimes, 0.50));
        var p95Ratio = Ratio(
            Percentile(defaultOffTimes, 0.95),
            Percentile(baselineTimes, 0.95));

        WarmRetentionPaths(
            compilation,
            contract.Warmups,
            cancellationToken);
        var baselineRetained = MeasureNoAnalyzerRetainedBytes(
            source,
            "Baseline",
            cancellationToken);
        var defaultOffRetained = MeasureNoAnalyzerRetainedBytes(
            source,
            "DefaultOff",
            cancellationToken);
        var retainedRatio = Ratio(defaultOffRetained, baselineRetained);
        var retainedIncreaseMiB =
            (defaultOffRetained - baselineRetained) / (1024d * 1024d);
        WarmEnabledAnalyzerRetentionPaths(
            contract.Warmups,
            cancellationToken);
        var enabledRetention = MeasureEnabledAnalyzerRetention(
            cancellationToken);

        var editMeasurement = await MeasureIdeEditsAsync(
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        var editP95 = Percentile(editMeasurement.Latencies, 0.95);
        var editMaximum = editMeasurement.Latencies.Max();
        var workerMeasurements = await WorkerPerformanceProbe.MeasureAsync(
                repositoryRoot,
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        var cancellationP95 = Percentile(
            workerMeasurements.CancellationLatencies,
            0.95);
        var forcedTermination =
            workerMeasurements.ForcedTerminationMilliseconds;

        var failures = ImmutableArray.CreateBuilder<string>();
        if (medianRatio > contract.MaximumMedianRatio)
            failures.Add(
                $"Default-off median ratio {Format(medianRatio)} exceeds " +
                $"{Format(contract.MaximumMedianRatio)}.");
        if (p95Ratio > contract.MaximumP95Ratio)
            failures.Add(
                $"Default-off p95 ratio {Format(p95Ratio)} exceeds " +
                $"{Format(contract.MaximumP95Ratio)}.");
        failures.AddRange(EvaluateRetainedMemoryLimits(
            retainedRatio,
            retainedIncreaseMiB,
            contract));
        failures.AddRange(EvaluateEnabledAnalyzerRetentionLimits(
            enabledRetention.RetainedCompilationCount,
            enabledRetention.RetainedMemoryIncreaseMiB,
            contract));
        if (editP95 > contract.IdeEditP95Milliseconds)
            failures.Add(
                $"IDE edit p95 {Format(editP95)} ms exceeds " +
                $"{Format(contract.IdeEditP95Milliseconds)} ms.");
        if (editMaximum > contract.IdeEditMaximumMilliseconds)
            failures.Add(
                $"IDE edit maximum {Format(editMaximum)} ms exceeds " +
                $"{Format(contract.IdeEditMaximumMilliseconds)} ms.");
        failures.AddRange(editMeasurement.DiagnosticFailures);
        if (cancellationP95 > contract.CancellationP95Milliseconds)
            failures.Add(
                $"Worker cancellation p95 {Format(cancellationP95)} ms exceeds " +
                $"{Format(contract.CancellationP95Milliseconds)} ms.");
        if (forcedTermination > contract.ForcedTerminationMilliseconds)
            failures.Add(
                $"Launcher forced termination {Format(forcedTermination)} ms " +
                $"exceeds {Format(contract.ForcedTerminationMilliseconds)} ms.");

        return new PerformanceGateResult(
            failures.Count == 0,
            contract.Warmups,
            contract.Samples,
            medianRatio,
            p95Ratio,
            baselineRetained,
            defaultOffRetained,
            retainedRatio,
            retainedIncreaseMiB,
            enabledRetention.RetainedCompilationCount,
            enabledRetention.RetainedMemoryIncreaseMiB,
            contract.IdeEdits,
            editP95,
            editMaximum,
            editMeasurement.DiagnosticFailures.Length,
            cancellationP95,
            forcedTermination,
            failures.ToImmutable());
    }

    private static double MeasureNoAnalyzerBatch(
        string source,
        string kind,
        int iterations,
        CancellationToken cancellationToken) {
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++) {
            var compilation = CreateTimingCompilation(
                source,
                kind,
                index);
            _ = compilation.GetDiagnostics(cancellationToken);
        }
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds / iterations;
    }

    private static Compilation CreateTimingCompilation(
        string source,
        string kind,
        int index) =>
        AnalyzerGateHost.CreateCompilation(
            source,
            "SharpProof_" +
            kind +
            "_" +
            index.ToString(CultureInfo.InvariantCulture));

    private static void WarmRetentionPaths(
        Compilation compilation,
        int warmups,
        CancellationToken cancellationToken) {
        for (var index = 0; index < warmups; index++) {
            _ = compilation.GetDiagnostics(cancellationToken);
            _ = compilation.GetDiagnostics(cancellationToken);
        }
        ForceCollection();
    }

    private static long MeasureNoAnalyzerRetainedBytes(
        string source,
        string kind,
        CancellationToken cancellationToken) {
        ForceCollection();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var retained = new List<Compilation>(RetainedCompilationCount);
        for (var index = 0; index < RetainedCompilationCount; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = AnalyzerGateHost.CreateCompilation(
                source,
                $"Retained_{kind}_{index}");
            _ = compilation.GetDiagnostics(cancellationToken);
            retained.Add(compilation);
        }
        ForceCollection();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(retained);
        return Math.Max(1, after - before);
    }

    private static void WarmEnabledAnalyzerRetentionPaths(
        int warmups,
        CancellationToken cancellationToken) {
        for (var index = 0; index < warmups; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            _ = AnalyzeEnabledCompilation(
                CreateEnabledSource(index),
                $"EnabledRetentionWarmup_{index}",
                cancellationToken);
        }
        ForceCollection();
    }

    private static EnabledAnalyzerRetentionMeasurement
        MeasureEnabledAnalyzerRetention(
            CancellationToken cancellationToken) {
        ForceCollection();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var compilations =
            new List<WeakReference<Compilation>>(RetainedCompilationCount);
        for (var index = 0; index < RetainedCompilationCount; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            compilations.Add(
                AnalyzeEnabledCompilation(
                    CreateEnabledSource(index),
                    $"EnabledRetention_{index}",
                    cancellationToken));
        }
        ForceCollection();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        var retainedCompilationCount = compilations.Count(
            static compilation => compilation.TryGetTarget(out _));
        GC.KeepAlive(compilations);
        return new EnabledAnalyzerRetentionMeasurement(
            retainedCompilationCount,
            Math.Max(0, after - before) / (1024d * 1024d));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Compilation> AnalyzeEnabledCompilation(
        string source,
        string assemblyName,
        CancellationToken cancellationToken) {
        var compilation = AnalyzerGateHost.CreateCompilation(
            source,
            assemblyName);
        _ = AnalyzerGateHost.AnalyzeAsync(
                compilation,
                new SharpProofAnalyzer(),
                "effects",
                concurrentAnalysis: true,
                cancellationToken)
            .GetAwaiter()
            .GetResult();
        var reference = new WeakReference<Compilation>(compilation);
        GC.KeepAlive(compilation);
        return reference;
    }

    private static async Task<IdeEditMeasurement> MeasureIdeEditsAsync(
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken) {
        const string marker = "return null;";
        var source = """
            using SharpProof.Attributes;

            public static class IdeEditFixture {
                [ZeroAllocations]
                public static object Evaluate() {
                    return null;
                }
            }
            """;
        var compilation = AnalyzerGateHost.CreateCompilation(
            source,
            "IdeEditPerformance");
        var tree = compilation.SyntaxTrees.Single();
        var markerStart = source.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerStart < 0)
            throw new InvalidOperationException("IDE edit marker is missing.");
        var analyzer = new SharpProofAnalyzer();

        for (var index = 0; index < contract.Warmups; index++) {
            var allocates = (index & 1) != 0;
            var warmText = tree.GetText(cancellationToken).WithChanges(
                new TextChange(
                    new TextSpan(markerStart, marker.Length),
                    allocates ? "return new object();" : marker));
            var warmTree = tree.WithChangedText(warmText);
            var warmCompilation = compilation.ReplaceSyntaxTree(tree, warmTree);
            var diagnostics = await AnalyzerGateHost.AnalyzeAsync(
                    warmCompilation,
                    analyzer,
                    "effects",
                    concurrentAnalysis: true,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateIdeDiagnostics(diagnostics, allocates, index, "warmup");
        }

        var latencies = new double[contract.IdeEdits];
        var diagnosticFailures = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < latencies.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var allocates = (index & 1) != 0;
            var replacement = allocates ? "return new object();" : marker;
            var stopwatch = Stopwatch.StartNew();
            var changedText = tree.GetText(cancellationToken).WithChanges(
                new TextChange(
                    new TextSpan(markerStart, marker.Length),
                    replacement));
            var changedTree = tree.WithChangedText(changedText);
            var changedCompilation = compilation.ReplaceSyntaxTree(
                tree,
                changedTree);
            var diagnostics = await AnalyzerGateHost.AnalyzeAsync(
                    changedCompilation,
                    analyzer,
                    "effects",
                    concurrentAnalysis: true,
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            latencies[index] = stopwatch.Elapsed.TotalMilliseconds;
            try {
                ValidateIdeDiagnostics(
                    diagnostics,
                    allocates,
                    index,
                    "measured");
            }
            catch (InvalidOperationException exception) {
                diagnosticFailures.Add(exception.Message);
            }
        }
        return new IdeEditMeasurement(
            latencies,
            diagnosticFailures.ToImmutable());
    }

    private static void ValidateIdeDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        bool allocates,
        int editIndex,
        string phase) {
        var canonical = diagnostics
            .Select(static diagnostic =>
                diagnostic.Id + "|" +
                diagnostic.Location.SourceSpan.Start.ToString(
                    CultureInfo.InvariantCulture) + "|" +
                diagnostic.GetMessage(CultureInfo.InvariantCulture))
            .OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal)
            .ToImmutableArray();
        var duplicateCount = canonical.Length -
                             canonical.Distinct(StringComparer.Ordinal).Count();
        ImmutableArray<string> expectedIds = allocates
            ? ["SP0045"]
            : [];
        var actualIds = diagnostics
            .Select(static diagnostic => diagnostic.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToImmutableArray();
        if (duplicateCount == 0 &&
            actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            return;

        throw new InvalidOperationException(
            $"IDE {phase} edit {editIndex} produced stale or duplicate " +
            $"diagnostics: expected [{string.Join(", ", expectedIds)}], " +
            $"actual [{string.Join(", ", actualIds)}], duplicates " +
            $"{duplicateCount}.");
    }

    private static string CreateDefaultOffSource(int methodCount) {
        var builder = new StringBuilder();
        builder.AppendLine("public static class DefaultOffFixture {");
        for (var index = 0; index < methodCount; index++)
            builder.Append("    public static int M")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append("(int value) => value + ")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string CreateEnabledSource(int index) =>
        $$"""
        using SharpProof.Attributes;

        public static class EnabledRetentionFixture_{{index}} {
            [ZeroAllocations]
            public static int Evaluate(int value) {
                return value + {{index}};
            }
        }
        """;

    private static double Percentile(IEnumerable<double> values, double rank) {
        var sorted = values.OrderBy(static value => value).ToArray();
        if (sorted.Length == 0)
            throw new ArgumentException("At least one sample is required.", nameof(values));
        var index = Math.Clamp(
            (int)Math.Ceiling(rank * sorted.Length) - 1,
            0,
            sorted.Length - 1);
        return sorted[index];
    }

    private static double Ratio(double numerator, double denominator) =>
        denominator <= 0 ? double.PositiveInfinity : numerator / denominator;

    private static void ForceCollection() {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static ImmutableArray<string> EvaluateRetainedMemoryLimits(
        double retainedRatio,
        double retainedIncreaseMiB,
        AcceptancePerformanceContract contract) {
        var failures = ImmutableArray.CreateBuilder<string>();
        if (retainedRatio > contract.MaximumRetainedMemoryRatio)
            failures.Add(
                $"Default-off retained memory ratio {Format(retainedRatio)} " +
                $"exceeds {Format(contract.MaximumRetainedMemoryRatio)}.");
        if (retainedIncreaseMiB >
            contract.MaximumRetainedMemoryIncreaseMiB)
            failures.Add(
                $"Default-off retained memory increase " +
                $"{Format(retainedIncreaseMiB)} MiB exceeds " +
                $"{contract.MaximumRetainedMemoryIncreaseMiB} MiB.");
        return failures.ToImmutable();
    }

    internal static ImmutableArray<string>
        EvaluateEnabledAnalyzerRetentionLimits(
            int retainedCompilationCount,
            double retainedMemoryIncreaseMiB,
            AcceptancePerformanceContract contract) {
        var failures = ImmutableArray.CreateBuilder<string>();
        if (retainedCompilationCount >
            contract.MaximumEnabledRetainedCompilations)
            failures.Add(
                $"Enabled analyzer retained {retainedCompilationCount} " +
                $"compilation graph(s); maximum is " +
                $"{contract.MaximumEnabledRetainedCompilations}.");
        if (retainedMemoryIncreaseMiB >
            contract.MaximumEnabledRetainedMemoryIncreaseMiB)
            failures.Add(
                $"Enabled analyzer retained memory increase " +
                $"{Format(retainedMemoryIncreaseMiB)} MiB exceeds " +
                $"{contract.MaximumEnabledRetainedMemoryIncreaseMiB} MiB.");
        return failures.ToImmutable();
    }

    private static void ValidateContract(AcceptancePerformanceContract contract) {
        if (contract.Warmups != 5 ||
            contract.Samples != 30 ||
            contract.IdeEdits != 200)
            throw new InvalidDataException(
                "The v2 performance protocol is fixed at 5 warmups, " +
                "30 samples, and 200 IDE edits.");
        if (contract.MaximumMedianRatio <= 0 ||
            contract.MaximumP95Ratio <= 0 ||
            contract.MaximumRetainedMemoryRatio <= 0 ||
            contract.MaximumRetainedMemoryIncreaseMiB < 0 ||
            contract.MaximumEnabledRetainedCompilations < 0 ||
            contract.MaximumEnabledRetainedMemoryIncreaseMiB < 0 ||
            contract.IdeEditP95Milliseconds <= 0 ||
            contract.IdeEditMaximumMilliseconds <= 0 ||
            contract.CancellationP95Milliseconds <= 0 ||
            contract.ForcedTerminationMilliseconds <= 0)
            throw new InvalidDataException(
                "The v2 performance limits must be positive.");
    }

    private sealed record EnabledAnalyzerRetentionMeasurement(
        int RetainedCompilationCount,
        double RetainedMemoryIncreaseMiB);

    private sealed record IdeEditMeasurement(
        double[] Latencies,
        ImmutableArray<string> DiagnosticFailures);
}
