using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer;

namespace SharpProof.Gates.Performance;

internal sealed record PerformanceGateResult(
    bool Passed,
    int Warmups,
    int Samples,
    int DefaultOffAnalyzerDriverRunCount,
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

internal static class PerformanceGate {
    private const int RetainedCompilationCount = 40;

    public static async Task<PerformanceGateResult> RunAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default) {
        var contract = AcceptancePerformanceContract.Load(repositoryRoot);
        ValidateContract(contract);
        var source = CreateDefaultOffSource(320);
        ValidateAdvisoryPackagePolicy(repositoryRoot);
        var configurationProbe = MeasureDefaultOffAnalyzerBatch(
            source,
            "DefaultOffConfigurationProbe",
            iterations: 1,
            cancellationToken);
        var defaultOffAnalyzerDriverRuns =
            configurationProbe.AnalyzerDriverRunCount;
        var packageBuildTiming =
            await MeasureDefaultOffPackageBuildsAsync(
                    repositoryRoot,
                    source,
                    contract.Warmups,
                    contract.Samples,
                    cancellationToken)
                .ConfigureAwait(false);
        var baselineTimes = packageBuildTiming.BaselineMilliseconds;
        var defaultOffTimes = packageBuildTiming.DefaultOffMilliseconds;

        var medianRatio = Ratio(
            Percentile(defaultOffTimes, 0.50),
            Percentile(baselineTimes, 0.50));
        var p95Ratio = Ratio(
            Percentile(defaultOffTimes, 0.95),
            Percentile(baselineTimes, 0.95));

        WarmRetentionPaths(
            source,
            contract.Warmups,
            cancellationToken);
        var baselineRetained = MeasureCompilerOnlyRetainedBytes(
            source,
            "Baseline",
            cancellationToken);
        var defaultOffRetained = MeasureDefaultOffAnalyzerRetainedBytes(
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
            defaultOffAnalyzerDriverRuns,
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

    internal static DefaultOffBatchMeasurement
        MeasureDefaultOffAnalyzerBatch(
            string source,
            string kind,
            int iterations,
            CancellationToken cancellationToken = default) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        var sessionFactory = new CountingSessionFactory();
        var analyzer = new SharpProofAnalyzer(sessionFactory);
        var diagnosticCount = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = CreateTimingCompilation(
                source,
                kind,
                index);
            _ = compilation.GetDiagnostics(cancellationToken);
            diagnosticCount += AnalyzeDefaultOff(
                compilation,
                analyzer,
                cancellationToken);
        }
        stopwatch.Stop();
        if (diagnosticCount != 0 || sessionFactory.CreateCount != iterations)
            throw new InvalidOperationException(
                "Unannotated advisory analysis must stay quiet and create " +
                "exactly one analysis session per compilation.");
        return new DefaultOffBatchMeasurement(
            stopwatch.Elapsed.TotalMilliseconds / iterations,
            iterations,
            diagnosticCount,
            sessionFactory.CreateCount);
    }

    private static CSharpCompilation CreateTimingCompilation(
        string source,
        string kind,
        int index) =>
        AnalyzerGateHost.CreateCompilation(
            source,
            "SharpProof_" +
            kind +
            "_" +
            index.ToString(CultureInfo.InvariantCulture));

    private static async Task<PackageBuildTiming>
        MeasureDefaultOffPackageBuildsAsync(
            string repositoryRoot,
            string source,
            int warmups,
            int samples,
            CancellationToken cancellationToken) {
        var probeParent = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Performance");
        var resolvedParent = Path.GetFullPath(probeParent);
        var resolvedRoot = Path.GetFullPath(
            Path.Combine(resolvedParent, Guid.NewGuid().ToString("N")));
        if (!resolvedRoot.StartsWith(
                resolvedParent + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusing to use an unexpected performance probe path.");
        var baselineDirectory = Path.Combine(resolvedRoot, "baseline");
        var defaultOffDirectory = Path.Combine(resolvedRoot, "default-off");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(defaultOffDirectory);
        try {
            var baselineProject = CreatePerformanceProbeProject(
                baselineDirectory,
                source,
                repositoryRoot,
                importSharpProof: false);
            var defaultOffProject = CreatePerformanceProbeProject(
                defaultOffDirectory,
                source,
                repositoryRoot,
                importSharpProof: true);
            await RunDotnetAsync(
                    baselineProject,
                    restore: true,
                    symbol: null,
                    cancellationToken)
                .ConfigureAwait(false);
            await RunDotnetAsync(
                    defaultOffProject,
                    restore: true,
                    symbol: null,
                    cancellationToken)
                .ConfigureAwait(false);
            for (var index = 0; index < warmups; index++)
                await RunBuildPairAsync(
                        baselineProject,
                        defaultOffProject,
                        $"SHARPPROOF_WARMUP_{index}",
                        defaultFirst: (index & 1) != 0,
                        cancellationToken)
                    .ConfigureAwait(false);

            var baseline = new double[samples];
            var defaultOff = new double[samples];
            for (var index = 0; index < samples; index++) {
                var pair = await RunBuildPairAsync(
                        baselineProject,
                        defaultOffProject,
                        $"SHARPPROOF_SAMPLE_{index}",
                        defaultFirst: (index & 1) != 0,
                        cancellationToken)
                    .ConfigureAwait(false);
                baseline[index] = pair.BaselineMilliseconds;
                defaultOff[index] = pair.DefaultOffMilliseconds;
            }
            return new PackageBuildTiming(baseline, defaultOff);
        }
        finally {
            if (Directory.Exists(resolvedRoot))
                Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private static string CreatePerformanceProbeProject(
        string directory,
        string source,
        string repositoryRoot,
        bool importSharpProof) {
        File.WriteAllText(
            Path.Combine(directory, "Subject.cs"),
            source,
            new UTF8Encoding(false));
        var props = System.Security.SecurityElement.Escape(Path.Combine(
            repositoryRoot, "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var targets = System.Security.SecurityElement.Escape(Path.Combine(
            repositoryRoot, "SharpProof.Package",
            "buildTransitive",
            "SharpProof.targets"));
        var configuration = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent!.Name;
        var analyzerDirectory = EscapeAnalyzerDirectory(repositoryRoot, configuration);
        var generatorPath = EscapePath(AppContext.BaseDirectory, "SharpProof.ContractForGenerator.dll");
        var imports = importSharpProof
            ? ($"""<Import Project="{props}" />""" + Environment.NewLine,
               Environment.NewLine + $"""<Import Project="{targets}" />""")
            : (string.Empty, string.Empty);
        var project = Path.Combine(directory, "Probe.csproj");
        File.WriteAllText(
            project,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <LangVersion>12.0</LangVersion>
                <Deterministic>true</Deterministic>
                <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                <SharpProofAnalyzerDirectory>{analyzerDirectory}</SharpProofAnalyzerDirectory>
                <SharpProofContractForGeneratorPath>{generatorPath}</SharpProofContractForGeneratorPath>
              </PropertyGroup>
              {imports.Item1}{imports.Item2}
            </Project>
            """,
            new UTF8Encoding(false));
        return project;
    }

    private static string? EscapeAnalyzerDirectory(string root, string configuration) =>
        EscapePath(root, "SharpProof.Analyzer", "bin", configuration, "netstandard2.0");

    private static string? EscapePath(params string[] segments) =>
        System.Security.SecurityElement.Escape(Path.Combine(segments));

    private static async Task<PackageBuildPair> RunBuildPairAsync(
        string baselineProject,
        string defaultOffProject,
        string symbol,
        bool defaultFirst,
        CancellationToken cancellationToken) {
        if (defaultFirst) {
            var defaultOff = await RunDotnetAsync(
                    defaultOffProject,
                    restore: false,
                    symbol,
                    cancellationToken)
                .ConfigureAwait(false);
            var baseline = await RunDotnetAsync(
                    baselineProject,
                    restore: false,
                    symbol,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PackageBuildPair(baseline, defaultOff);
        }
        else {
            var baseline = await RunDotnetAsync(
                    baselineProject,
                    restore: false,
                    symbol,
                    cancellationToken)
                .ConfigureAwait(false);
            var defaultOff = await RunDotnetAsync(
                    defaultOffProject,
                    restore: false,
                    symbol,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PackageBuildPair(baseline, defaultOff);
        }
    }

    private static async Task<double> RunDotnetAsync(
        string project,
        bool restore,
        string? symbol,
        CancellationToken cancellationToken) {
        var startInfo = new ProcessStartInfo {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(project)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(restore ? "restore" : "build");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("/nodeReuse:false");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        if (!restore) {
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("-t:Rebuild");
            startInfo.ArgumentList.Add("-p:DefineConstants=" + symbol);
        }
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The performance probe process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try {
            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        stopwatch.Stop();
        var output = (await standardOutput.ConfigureAwait(false)) +
                     Environment.NewLine +
                     (await standardError.ConfigureAwait(false));
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"The default-off package performance probe failed:{Environment.NewLine}" +
                output);
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static void WarmRetentionPaths(
        string source,
        int warmups,
        CancellationToken cancellationToken) {
        var sessionFactory = new CountingSessionFactory();
        var analyzer = new SharpProofAnalyzer(sessionFactory);
        for (var index = 0; index < warmups; index++) {
            var baseline = AnalyzerGateHost.CreateCompilation(
                source,
                $"RetentionBaselineWarmup_{index}");
            _ = baseline.GetDiagnostics(cancellationToken);
            var defaultOff = AnalyzerGateHost.CreateCompilation(
                source,
                $"RetentionDefaultOffWarmup_{index}");
            _ = defaultOff.GetDiagnostics(cancellationToken);
            _ = AnalyzeDefaultOff(
                defaultOff,
                analyzer,
                cancellationToken);
        }
        if (sessionFactory.CreateCount != warmups)
            throw new InvalidOperationException(
                "Advisory retention warmup created an unexpected number of sessions.");
        ForceCollection();
    }

    private static long MeasureCompilerOnlyRetainedBytes(
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

    private static long MeasureDefaultOffAnalyzerRetainedBytes(
        string source,
        string kind,
        CancellationToken cancellationToken) {
        ForceCollection();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var retained = new List<Compilation>(RetainedCompilationCount);
        var sessionFactory = new CountingSessionFactory();
        var analyzer = new SharpProofAnalyzer(sessionFactory);
        var diagnosticCount = 0;
        for (var index = 0; index < RetainedCompilationCount; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = AnalyzerGateHost.CreateCompilation(
                source,
                $"Retained_{kind}_{index}");
            _ = compilation.GetDiagnostics(cancellationToken);
            diagnosticCount += AnalyzeDefaultOff(
                compilation,
                analyzer,
                cancellationToken);
            retained.Add(compilation);
        }
        if (diagnosticCount != 0 ||
            sessionFactory.CreateCount != RetainedCompilationCount)
            throw new InvalidOperationException(
                "Unannotated advisory retention must stay quiet and create " +
                "exactly one analysis session per compilation.");
        ForceCollection();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(retained);
        GC.KeepAlive(analyzer);
        return Math.Max(1, after - before);
    }

    private static int AnalyzeDefaultOff(
        Compilation compilation,
        DiagnosticAnalyzer analyzer,
        CancellationToken cancellationToken) =>
        AnalyzerGateHost.AnalyzeAsync(
                compilation,
                analyzer,
                mode: null,
                concurrentAnalysis: true,
                cancellationToken)
            .GetAwaiter()
            .GetResult()
            .Length;

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
        var currentCompilation = compilation;
        var currentTree = tree;
        var currentlyAllocates = false;

        for (var index = 0; index < contract.Warmups; index++) {
            var allocates = !currentlyAllocates;
            var currentMarker = currentlyAllocates
                ? "return new object();"
                : marker;
            var warmSourceText = await currentTree.GetTextAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            var warmText = warmSourceText.WithChanges(
                new TextChange(
                    new TextSpan(markerStart, currentMarker.Length),
                    allocates ? "return new object();" : marker));
            var warmTree = currentTree.WithChangedText(warmText);
            var warmCompilation = currentCompilation.ReplaceSyntaxTree(
                currentTree,
                warmTree);
            var diagnostics = await AnalyzerGateHost.AnalyzeAsync(
                    warmCompilation,
                    analyzer,
                    "effects",
                    concurrentAnalysis: true,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateIdeDiagnostics(diagnostics, allocates, index, "warmup");
            currentTree = warmTree;
            currentCompilation = warmCompilation;
            currentlyAllocates = allocates;
        }

        var latencies = new double[contract.IdeEdits];
        var diagnosticFailures = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < latencies.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var allocates = !currentlyAllocates;
            var replacement = allocates ? "return new object();" : marker;
            var currentMarker = currentlyAllocates
                ? "return new object();"
                : marker;
            var stopwatch = Stopwatch.StartNew();
            var currentText = await currentTree.GetTextAsync(cancellationToken)
                .ConfigureAwait(false);
            var changedText = currentText.WithChanges(
                new TextChange(
                    new TextSpan(markerStart, currentMarker.Length),
                    replacement));
            var changedTree = currentTree.WithChangedText(changedText);
            var changedCompilation = currentCompilation.ReplaceSyntaxTree(
                currentTree,
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
            currentTree = changedTree;
            currentCompilation = changedCompilation;
            currentlyAllocates = allocates;
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
                "The performance protocol is fixed at 5 warmups, " +
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
                "The performance limits must be positive.");
    }

    internal static void ValidateAdvisoryPackagePolicy(
        string repositoryRoot) {
        var packageRoot = Path.Combine(
            repositoryRoot,
            "SharpProof.Package",
            "buildTransitive");
        var props = XDocument.Load(Path.Combine(
            packageRoot,
            "SharpProof.props"));
        var targets = XDocument.Load(Path.Combine(
            packageRoot,
            "SharpProof.targets"));
        ValidateAdvisoryPackagePolicy(props, targets);
    }

    internal static void ValidateAdvisoryPackagePolicy(
        XDocument props,
        XDocument targets) {
        var visibleProperties = props.Descendants("CompilerVisibleProperty")
            .Select(static element => (string?)element.Attribute("Include"))
            .ToHashSet(StringComparer.Ordinal);
        var profile = targets.Descendants("SharpProofProfile").SingleOrDefault(
            static element =>
                string.Equals(
                    (string?)element.Attribute("Condition"),
                    "'$(SharpProofProfile)' == ''",
                    StringComparison.Ordinal));
        var features = targets.Descendants("SharpProofFeatures").SingleOrDefault(
            static element =>
                string.Equals(
                    (string?)element.Attribute("Condition"),
                    "'$(SharpProofFeatures)' == ''",
                    StringComparison.Ordinal));
        var verify = targets.Descendants("SharpProofVerify").SingleOrDefault(
            static element =>
                string.Equals(
                    (string?)element.Attribute("Condition"),
                    "'$(SharpProofVerify)' == ''",
                    StringComparison.Ordinal));
        var analyzerGroup = targets.Descendants("ItemGroup")
            .SingleOrDefault(static group =>
                group.Elements("Analyzer").Any());
        var verifierTarget = targets.Descendants("Target")
            .SingleOrDefault(static target =>
                string.Equals(
                    (string?)target.Attribute("Name"),
                    "SharpProofVerify",
                    StringComparison.Ordinal));
        var verifierCore = targets.Descendants("Target")
            .SingleOrDefault(static target =>
                string.Equals(
                    (string?)target.Attribute("Name"),
                    "_SharpProofVerifyCore",
                    StringComparison.Ordinal));
        var normalizedCondition = NormalizeMsBuildCondition(
            (string?)analyzerGroup?.Attribute("Condition"));
        var normalizedVerifierCondition = NormalizeMsBuildCondition(
            (string?)verifierTarget?.Attribute("Condition"));
        const string expectedVerifierCondition =
            "'$(SharpProofVerify)'=='true'AND'$(_SharpProofProfileNormalized)'!='off'AND" +
            "'$(OS)'=='Windows_NT'AND" +
            "'$(DesignTimeBuild)'!='true'AND'$(BuildingProject)'!='false'";
        var unexpectedCoreDependency = targets.Descendants("Target")
            .Where(target => !ReferenceEquals(target, verifierTarget))
            .Any(target => SplitTargetList(
                    (string?)target.Attribute("DependsOnTargets"))
                .Contains(
                    "_SharpProofVerifyCore",
                    StringComparer.Ordinal));
        var callTargetInvokesCore = targets.Descendants("CallTarget")
            .Any(call => SplitTargetList(
                    (string?)call.Attribute("Targets"))
                .Contains(
                    "_SharpProofVerifyCore",
                    StringComparer.Ordinal));
        var verifierExec = targets.Descendants("Exec").ToArray();
        if (!visibleProperties.Contains("SharpProofProfile") ||
            !visibleProperties.Contains("SharpProofFeatures") ||
            !string.Equals(profile?.Value, "advisory", StringComparison.Ordinal) ||
            !string.Equals(features?.Value, "all", StringComparison.Ordinal) ||
            !string.Equals(verify?.Value, "false", StringComparison.Ordinal) ||
            !string.Equals(
                normalizedCondition,
                "'$(_SharpProofProfileNormalized)'!='off'",
                StringComparison.Ordinal) ||
            !string.Equals(
                normalizedVerifierCondition,
                expectedVerifierCondition,
                StringComparison.Ordinal) ||
            !string.Equals(
                (string?)verifierTarget?.Attribute("AfterTargets"),
                "CoreCompile",
                StringComparison.Ordinal) ||
            !string.Equals(
                (string?)verifierTarget?.Attribute("DependsOnTargets"),
                "_SharpProofInitializeVerify;_SharpProofVerifyCore",
                StringComparison.Ordinal) ||
            verifierCore == null ||
            verifierCore.Attribute("BeforeTargets") != null ||
            verifierCore.Attribute("AfterTargets") != null ||
            unexpectedCoreDependency ||
            callTargetInvokesCore ||
            verifierExec.Length != 1 ||
            !ReferenceEquals(
                verifierExec[0].Ancestors("Target").SingleOrDefault(),
                verifierCore)) {
            throw new InvalidDataException(
                "The package must run advisory analysis but omit the verifier " +
                "by default.");
        }
    }

    private static string NormalizeMsBuildCondition(string? condition) =>
        string.Concat((condition ?? string.Empty)
            .Where(static character => !char.IsWhiteSpace(character)));

    private static ImmutableArray<string> SplitTargetList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(
                    [';'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(static target => target.Trim())
                .Where(static target => target.Length != 0)];

    private sealed record EnabledAnalyzerRetentionMeasurement(
        int RetainedCompilationCount,
        double RetainedMemoryIncreaseMiB);

    private sealed record PackageBuildTiming(
        double[] BaselineMilliseconds,
        double[] DefaultOffMilliseconds);

    private readonly record struct PackageBuildPair(
        double BaselineMilliseconds,
        double DefaultOffMilliseconds);

    internal sealed record DefaultOffBatchMeasurement(
        double MeanMilliseconds,
        int AnalyzerDriverRunCount,
        int DiagnosticCount,
        int AnalysisSessionCreateCount);

    private sealed record IdeEditMeasurement(
        double[] Latencies,
        ImmutableArray<string> DiagnosticFailures);

    private sealed class CountingSessionFactory : IAnalyzerSessionFactory {
        internal int CreateCount { get; private set; }

        public AnalyzerSession Create(
            Compilation compilation,
            SharpProof.Analyzer.Configuration.AnalyzerConfiguration configuration,
            CancellationToken cancellationToken) {
            CreateCount++;
            return new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken);
        }
    }

}
