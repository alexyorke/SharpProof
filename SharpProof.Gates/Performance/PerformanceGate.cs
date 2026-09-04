using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
    string PackageBuildEstimatorVersion,
    PackageBuildSdkIdentity PackageBuildSdk,
    ImmutableArray<PackageBuildSample> PackageBuildSamples,
    ImmutableArray<double> OrderBalancedRatios,
    int UnannotatedAdvisoryAnalyzerDriverRunCount,
    int UnannotatedAdvisoryAnalysisSessionCreateCount,
    int UnannotatedAdvisoryApiSpecCreateCount,
    int UnannotatedAdvisoryEffectAnalysisCreateCount,
    double OrderBalancedMedianRatio,
    double RawMedianRatio,
    double BaselineFirstMedianRatio,
    double UnannotatedAdvisoryFirstMedianRatio,
    double RawP95Ratio,
    long BaselineRetainedBytes,
    long UnannotatedAdvisoryRetainedBytes,
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

internal sealed record PerformanceSmokeResult(
    bool Passed,
    int Warmups,
    int Samples,
    double MaximumAllowedRatio,
    double MaximumObservedRatio,
    PackageBuildSdkIdentity PackageBuildSdk,
    ImmutableArray<PackageBuildSample> PackageBuildSamples,
    int UnannotatedAdvisoryAnalyzerDriverRunCount,
    int UnannotatedAdvisoryAnalysisSessionCreateCount,
    double ForcedTerminationMilliseconds,
    ImmutableArray<string> Failures);

internal static class PerformanceGate
{
    private const int RetainedCompilationCount = 40;
    private static readonly TimeSpan PackageBuildProcessTimeout =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessTerminationTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8WithoutBom =
        new(encoderShouldEmitUTF8Identifier: false);

    public static Task<PerformanceGateResult> RunAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var contract = AcceptancePerformanceContract.Load(repositoryRoot);
        ValidateContract(contract);
        return RunValidatedAsync(
            repositoryRoot,
            contract,
            cancellationToken);
    }

    internal static Task<PerformanceGateResult> RunStructuralCoverageAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var contract = AcceptancePerformanceContract.Load(repositoryRoot);
        ValidateContract(contract);
        return RunValidatedAsync(
            repositoryRoot,
            contract with
            {
                Warmups = 1,
                Samples = 2,
                IdeEdits = 2
            },
            cancellationToken);
    }

    private static async Task<PerformanceGateResult> RunValidatedAsync(
        string repositoryRoot,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken)
    {
        var callBearingSource =
            CreateCallBearingUnannotatedAdvisorySource(320);
        var callFreeSource =
            CreateCallFreeUnannotatedAdvisorySource(320);
        ValidateAdvisoryPackagePolicy(repositoryRoot);
        var configurationProbe = MeasureUnannotatedAdvisoryAnalyzerBatch(
            callBearingSource,
            "UnannotatedAdvisoryConfigurationProbe",
            iterations: 1,
            cancellationToken);
        var unannotatedAdvisoryAnalyzerDriverRuns =
            configurationProbe.AnalyzerDriverRunCount;
        var unannotatedAdvisoryAnalysisSessionCreates =
            configurationProbe.AnalysisSessionCreateCount;
        if (unannotatedAdvisoryAnalysisSessionCreates != 0)
        {
            throw new InvalidOperationException(
                "The call-bearing advisory performance probe must not create " +
                "a semantic analysis session when neither source nor referenced " +
                "metadata contains SharpProof contracts.");
        }
        if (configurationProbe.ApiSpecCreateCount != 0 ||
            configurationProbe.EffectAnalysisCreateCount != 0)
        {
            throw new InvalidOperationException(
                "Unselected advisory code must not create API-spec or " +
                "effect-analysis state.");
        }
        var packageBuildTiming =
            await MeasureUnannotatedAdvisoryPackageBuildsAsync(
                    repositoryRoot,
                    callBearingSource,
                    contract.Warmups,
                    contract.Samples,
                    cancellationToken)
                .ConfigureAwait(false);
        var packageBuildStatistics = PackageBuildEstimator.Estimate(
            packageBuildTiming.Samples);
        var medianRatio =
            packageBuildStatistics.OrderBalancedMedianRatio;
        var p95Ratio = packageBuildStatistics.P95Ratio;

        WarmRetentionPaths(
            callFreeSource,
            contract.Warmups,
            cancellationToken);
        var baselineRetained = MeasureRetainedBytes(
            callFreeSource,
            "Baseline",
            runAnalyzer: false,
            cancellationToken);
        var unannotatedAdvisoryRetained =
            MeasureRetainedBytes(
            callFreeSource,
            "UnannotatedAdvisory",
            runAnalyzer: true,
            cancellationToken);
        var retainedRatio = Ratio(
            unannotatedAdvisoryRetained,
            baselineRetained);
        var retainedIncreaseMiB =
            (unannotatedAdvisoryRetained - baselineRetained) /
            (1024d * 1024d);
        var enabledRetention = MeasureEnabledAnalyzerRetention(
            contract.Warmups,
            cancellationToken);

        var editMeasurement = await MeasureIdeEditsAsync(
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        var editP95 = PackageBuildEstimator.NearestRankPercentile(
            editMeasurement.Latencies,
            0.95,
            requireFinitePositive: false);
        var editMaximum = editMeasurement.Latencies.Max();
        var workerMeasurements = await WorkerPerformanceProbe.MeasureAsync(
                repositoryRoot,
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        var cancellationP95 = PackageBuildEstimator.NearestRankPercentile(
            workerMeasurements.CancellationLatencies,
            0.95,
            requireFinitePositive: false);
        var forcedTermination =
            workerMeasurements.ForcedTerminationMilliseconds;

        var failures = ImmutableArray.CreateBuilder<string>();
        if (medianRatio > contract.MaximumMedianRatio)
        {
            failures.Add(
                "Unannotated advisory order-balanced median ratio " +
                $"{Format(medianRatio)} exceeds " +
                $"{Format(contract.MaximumMedianRatio)}.");
        }

        if (p95Ratio > contract.MaximumP95Ratio)
        {
            failures.Add(
                $"Unannotated advisory paired p95 ratio {Format(p95Ratio)} exceeds " +
                $"{Format(contract.MaximumP95Ratio)}.");
        }

        failures.AddRange(EvaluateRetainedMemoryLimits(
            retainedRatio,
            retainedIncreaseMiB,
            contract));
        failures.AddRange(EvaluateEnabledAnalyzerRetentionLimits(
            enabledRetention.RetainedCompilationCount,
            enabledRetention.RetainedMemoryIncreaseMiB,
            contract));
        if (editP95 > contract.IdeEditP95Milliseconds)
        {
            failures.Add(
                $"IDE edit p95 {Format(editP95)} ms exceeds " +
                $"{Format(contract.IdeEditP95Milliseconds)} ms.");
        }

        if (editMaximum > contract.IdeEditMaximumMilliseconds)
        {
            failures.Add(
                $"IDE edit maximum {Format(editMaximum)} ms exceeds " +
                $"{Format(contract.IdeEditMaximumMilliseconds)} ms.");
        }

        failures.AddRange(editMeasurement.DiagnosticFailures);
        if (cancellationP95 > contract.CancellationP95Milliseconds)
        {
            failures.Add(
                $"Worker cancellation p95 {Format(cancellationP95)} ms exceeds " +
                $"{Format(contract.CancellationP95Milliseconds)} ms.");
        }

        if (forcedTermination > contract.ForcedTerminationMilliseconds)
        {
            failures.Add(
                $"Launcher forced termination {Format(forcedTermination)} ms " +
                $"exceeds {Format(contract.ForcedTerminationMilliseconds)} ms.");
        }

        return new PerformanceGateResult(
            failures.Count == 0,
            contract.Warmups,
            contract.Samples,
            PackageBuildEstimator.Version,
            packageBuildTiming.Sdk,
            packageBuildTiming.Samples,
            packageBuildStatistics.OrderBalancedRatios,
            unannotatedAdvisoryAnalyzerDriverRuns,
            unannotatedAdvisoryAnalysisSessionCreates,
            configurationProbe.ApiSpecCreateCount,
            configurationProbe.EffectAnalysisCreateCount,
            medianRatio,
            packageBuildStatistics.RawMedianRatio,
            packageBuildStatistics.BaselineFirstMedianRatio,
            packageBuildStatistics.UnannotatedAdvisoryFirstMedianRatio,
            p95Ratio,
            baselineRetained,
            unannotatedAdvisoryRetained,
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

    public static async Task<PerformanceSmokeResult> RunSmokeAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var contract = AcceptancePerformanceContract.Load(repositoryRoot);
        ValidateContract(contract);
        var source = CreateCallBearingUnannotatedAdvisorySource(320);
        ValidateAdvisoryPackagePolicy(repositoryRoot);
        var configurationProbe = MeasureUnannotatedAdvisoryAnalyzerBatch(
            source,
            "UnannotatedAdvisorySmokeConfigurationProbe",
            iterations: 1,
            cancellationToken);
        var timing = await MeasureUnannotatedAdvisoryPackageBuildsAsync(
                repositoryRoot,
                source,
                contract.SmokeWarmups,
                contract.SmokeSamples,
                cancellationToken)
            .ConfigureAwait(false);
        var ratios = timing.Samples
            .Select(static sample =>
                sample.UnannotatedAdvisoryMilliseconds /
                sample.BaselineMilliseconds)
            .ToImmutableArray();
        var maximumObservedRatio = ratios.Max();
        var forcedTermination =
            await WorkerPerformanceProbe.MeasureForcedTerminationAsync(
                    repositoryRoot,
                    contract,
                    cancellationToken)
                .ConfigureAwait(false);
        var failures = ImmutableArray.CreateBuilder<string>();
        if (configurationProbe.AnalysisSessionCreateCount != 0 ||
            configurationProbe.ApiSpecCreateCount != 0 ||
            configurationProbe.EffectAnalysisCreateCount != 0)
        {
            failures.Add(
                "Unselected advisory code created semantic analysis state.");
        }
        if (maximumObservedRatio > contract.SmokeMaximumRatio)
        {
            failures.Add(
                "Unannotated advisory smoke ratio " +
                $"{Format(maximumObservedRatio)} exceeds " +
                $"{Format(contract.SmokeMaximumRatio)}.");
        }
        if (forcedTermination > contract.ForcedTerminationMilliseconds)
        {
            failures.Add(
                $"Launcher forced termination {Format(forcedTermination)} ms " +
                $"exceeds {Format(contract.ForcedTerminationMilliseconds)} ms.");
        }

        return new PerformanceSmokeResult(
            failures.Count == 0,
            contract.SmokeWarmups,
            contract.SmokeSamples,
            contract.SmokeMaximumRatio,
            maximumObservedRatio,
            timing.Sdk,
            timing.Samples,
            configurationProbe.AnalyzerDriverRunCount,
            configurationProbe.AnalysisSessionCreateCount,
            forcedTermination,
            failures.ToImmutable());
    }

    internal static UnannotatedAdvisoryBatchMeasurement
        MeasureUnannotatedAdvisoryAnalyzerBatch(
            string source,
            string kind,
            int iterations,
            CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        var sessionFactory = new CountingSessionFactory();
        var analyzer = new SharpProofAnalyzer(sessionFactory);
        var diagnosticCount = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = CreateTimingCompilation(
                source,
                kind,
                index);
            _ = compilation.GetDiagnostics(cancellationToken);
            diagnosticCount += AnalyzeUnannotatedAdvisory(
                compilation,
                analyzer,
                cancellationToken);
        }
        stopwatch.Stop();
        if (diagnosticCount != 0)
        {
            throw new InvalidOperationException(
                "Unannotated advisory analysis must stay quiet.");
        }

        return new UnannotatedAdvisoryBatchMeasurement(
            stopwatch.Elapsed.TotalMilliseconds / iterations,
            iterations,
            diagnosticCount,
            sessionFactory.CreateCount,
            sessionFactory.ApiSpecCreateCount,
            sessionFactory.EffectAnalysisCreateCount);
    }

    private static CSharpCompilation CreateTimingCompilation(
        string source,
        string kind,
        int index)
    {
        return AnalyzerGateHost.CreateCompilation(
            source,
            "SharpProof_" +
            kind +
            "_" +
            index.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task<PackageBuildTiming>
        MeasureUnannotatedAdvisoryPackageBuildsAsync(
            string repositoryRoot,
            string source,
            int warmups,
            int samples,
            CancellationToken cancellationToken)
    {
        var probeParent = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Performance");
        var resolvedParent = Path.GetFullPath(probeParent);
        var resolvedRoot = Path.GetFullPath(
            Path.Combine(resolvedParent, Guid.NewGuid().ToString("N")));
        if (!resolvedRoot.StartsWith(
                resolvedParent + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to use an unexpected performance probe path.");
        }

        var baselineDirectory = Path.Combine(resolvedRoot, "baseline");
        var unannotatedAdvisoryDirectory = Path.Combine(
            resolvedRoot,
            "unannotated-advisory");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(unannotatedAdvisoryDirectory);
        try
        {
            var sdk = await PackageBuildSdkPin.PinAndValidateAsync(
                    repositoryRoot,
                    resolvedRoot,
                    cancellationToken)
                .ConfigureAwait(false);
            var baselineProject = CreatePerformanceProbeProject(
                baselineDirectory,
                source,
                repositoryRoot,
                importSharpProof: false);
            var unannotatedAdvisoryProject =
                CreatePerformanceProbeProject(
                unannotatedAdvisoryDirectory,
                source,
                repositoryRoot,
                importSharpProof: true);
            await RunDotnetAsync(
                    baselineProject,
                    restore: true,
                    symbol: null,
                    PackageBuildProcessTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            await RunDotnetAsync(
                    unannotatedAdvisoryProject,
                    restore: true,
                    symbol: null,
                    PackageBuildProcessTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            for (var index = 0; index < warmups; index++)
            {
                await RunBuildPairAsync(
                        baselineProject,
                        unannotatedAdvisoryProject,
                        $"SHARPPROOF_WARMUP_{index}",
                        unannotatedAdvisoryFirst: (index & 1) != 0,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var measurements =
                ImmutableArray.CreateBuilder<PackageBuildSample>(samples);
            for (var index = 0; index < samples; index++)
            {
                var unannotatedAdvisoryFirst = (index & 1) != 0;
                var pair = await RunBuildPairAsync(
                        baselineProject,
                        unannotatedAdvisoryProject,
                        $"SHARPPROOF_SAMPLE_{index}",
                        unannotatedAdvisoryFirst,
                        cancellationToken)
                    .ConfigureAwait(false);
                measurements.Add(new PackageBuildSample(
                    index,
                    unannotatedAdvisoryFirst,
                    pair.BaselineMilliseconds,
                    pair.UnannotatedAdvisoryMilliseconds));
            }
            return new PackageBuildTiming(
                sdk,
                measurements.MoveToImmutable());
        }
        finally
        {
            if (Directory.Exists(resolvedRoot))
            {
                Directory.Delete(resolvedRoot, recursive: true);
            }
        }
    }

    private static string CreatePerformanceProbeProject(
        string directory,
        string source,
        string repositoryRoot,
        bool importSharpProof)
    {
        File.WriteAllText(
            Path.Combine(directory, "Subject.cs"),
            source,
            Utf8WithoutBom);
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
        var generatorPath = EscapePath(
            repositoryRoot,
            "SharpProof.ContractForGenerator",
            "bin",
            configuration,
            "netstandard2.0",
            "SharpProof.ContractForGenerator.dll");
        var sharedDirectory = EscapePath(
            repositoryRoot,
            "SharpProof.Analyzer.Core",
            "bin",
            configuration,
            "netstandard2.0");
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
                <WarningsAsErrors>$(WarningsAsErrors);AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
                <SharpProofAnalyzerDirectory>{analyzerDirectory}</SharpProofAnalyzerDirectory>
                <_SharpProofContractForGeneratorPath>{generatorPath}</_SharpProofContractForGeneratorPath>
                <_SharpProofSharedDirectory>{sharedDirectory}</_SharpProofSharedDirectory>
              </PropertyGroup>
            {imports.Item1}{imports.Item2}
            </Project>
            """,
            Utf8WithoutBom);
        return project;
    }

    private static string? EscapeAnalyzerDirectory(string root, string configuration)
    {
        return EscapePath(
            root,
            "SharpProof.Analyzer",
            "bin",
            configuration,
            "netstandard2.0");
    }

    private static string? EscapePath(params string[] segments)
    {
        return System.Security.SecurityElement.Escape(Path.Combine(segments));
    }

    private static async Task<PackageBuildPair> RunBuildPairAsync(
        string baselineProject,
        string unannotatedAdvisoryProject,
        string symbol,
        bool unannotatedAdvisoryFirst,
        CancellationToken cancellationToken)
    {
        Task<double> Run(string project)
        {
            return RunDotnetAsync(
                project,
                restore: false,
                symbol,
                PackageBuildProcessTimeout,
                cancellationToken);
        }

        double baseline;
        double unannotatedAdvisory;
        if (unannotatedAdvisoryFirst)
        {
            unannotatedAdvisory = await Run(unannotatedAdvisoryProject)
                .ConfigureAwait(false);
            baseline = await Run(baselineProject)
                .ConfigureAwait(false);
        }
        else
        {
            baseline = await Run(baselineProject)
                .ConfigureAwait(false);
            unannotatedAdvisory = await Run(unannotatedAdvisoryProject)
                .ConfigureAwait(false);
        }

        return new PackageBuildPair(baseline, unannotatedAdvisory);
    }

    private static async Task<double> RunDotnetAsync(
        string project,
        bool restore,
        string? symbol,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The process timeout must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
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
        if (!restore)
        {
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
            CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(
            CancellationToken.None);
        using var boundary = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        boundary.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await process.WaitForExitAsync(boundary.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested &&
                  boundary.IsCancellationRequested)
        {
            await TerminateProcessAsync(process).ConfigureAwait(false);
            throw new TimeoutException(
                "The package performance " +
                (restore ? "restore" : "build") +
                " probe exceeded its " +
                timeout.TotalSeconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) +
                "-second wall-time limit.",
                exception);
        }
        catch
        {
            await TerminateProcessAsync(process).ConfigureAwait(false);
            throw;
        }
        stopwatch.Stop();
        var output = (await standardOutput.ConfigureAwait(false)) +
                     Environment.NewLine +
                     (await standardError.ConfigureAwait(false));
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The unannotated advisory package performance probe failed:" +
                Environment.NewLine +
                output);
        }

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static async Task TerminateProcessAsync(Process process)
    {
        GateProcess.KillTree(process);

        if (process.HasExited)
        {
            return;
        }

        using var termination = new CancellationTokenSource(
            ProcessTerminationTimeout);
        try
        {
            await process.WaitForExitAsync(termination.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (termination.IsCancellationRequested)
        {
        }
    }

    private static void WarmRetentionPaths(
        string source,
        int warmups,
        CancellationToken cancellationToken)
    {
        var sessionFactory = new CountingSessionFactory();
        var analyzer = new SharpProofAnalyzer(sessionFactory);
        for (var index = 0; index < warmups; index++)
        {
            var baseline = AnalyzerGateHost.CreateCompilation(
                source,
                $"RetentionBaselineWarmup_{index}");
            _ = baseline.GetDiagnostics(cancellationToken);
            var unannotatedAdvisory = AnalyzerGateHost.CreateCompilation(
                source,
                $"RetentionUnannotatedAdvisoryWarmup_{index}");
            _ = unannotatedAdvisory.GetDiagnostics(cancellationToken);
            _ = AnalyzeUnannotatedAdvisory(
                unannotatedAdvisory,
                analyzer,
                cancellationToken);
        }
        if (sessionFactory.CreateCount != 0)
        {
            throw new InvalidOperationException(
                "Call-free advisory retention warmup created an analysis session.");
        }

        ForceCollection();
    }

    private static long MeasureRetainedBytes(
        string source,
        string kind,
        bool runAnalyzer,
        CancellationToken cancellationToken)
    {
        ForceCollection();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var retained = new List<Compilation>(RetainedCompilationCount);
        var sessionFactory = runAnalyzer ? new CountingSessionFactory() : null;
        var analyzer = runAnalyzer ? new SharpProofAnalyzer(sessionFactory!) : null;
        var diagnosticCount = 0;
        for (var index = 0; index < RetainedCompilationCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = AnalyzerGateHost.CreateCompilation(
                source,
                $"Retained_{kind}_{index}");
            _ = compilation.GetDiagnostics(cancellationToken);
            if (analyzer != null)
            {
                diagnosticCount += AnalyzeUnannotatedAdvisory(
                    compilation,
                    analyzer,
                    cancellationToken);
            }
            retained.Add(compilation);
        }
        if (analyzer != null &&
            (diagnosticCount != 0 || sessionFactory!.CreateCount != 0))
        {
            throw new InvalidOperationException(
                "Unannotated call-free advisory retention must stay quiet " +
                "and avoid analysis-session construction.");
        }

        ForceCollection();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(retained);
        GC.KeepAlive(analyzer);
        return Math.Max(1, after - before);
    }

    private static int AnalyzeUnannotatedAdvisory(
        Compilation compilation,
        DiagnosticAnalyzer analyzer,
        CancellationToken cancellationToken)
    {
        return AnalyzerGateHost.AnalyzeAsync(
                compilation,
                analyzer,
                mode: null,
                concurrentAnalysis: true,
                cancellationToken)
            .GetAwaiter()
            .GetResult()
            .Length;
    }

    private static ImmutableArray<WeakReference<Compilation>>
        WarmEnabledAnalyzerRetentionPaths(
        int warmups,
        CancellationToken cancellationToken)
    {
        var compilations =
            ImmutableArray.CreateBuilder<WeakReference<Compilation>>(warmups);
        var analyzer = new SharpProofAnalyzer();
        for (var index = 0; index < warmups; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            compilations.Add(AnalyzeEnabledCompilation(
                CreateEnabledSource(index),
                $"EnabledRetentionWarmup_{index}",
                analyzer,
                cancellationToken));
        }
        ForceCollection();
        GC.KeepAlive(analyzer);
        return compilations.ToImmutable();
    }

    private static EnabledAnalyzerRetentionMeasurement
        MeasureEnabledAnalyzerRetention(
            int warmups,
            CancellationToken cancellationToken)
    {
        var analyzer = new SharpProofAnalyzer();
        ForceCollection();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var compilations =
            new List<WeakReference<Compilation>>(
                warmups + RetainedCompilationCount);
        compilations.AddRange(
            WarmEnabledAnalyzerRetentionPaths(warmups, cancellationToken));
        for (var index = 0; index < RetainedCompilationCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            compilations.Add(
                AnalyzeEnabledCompilation(
                    CreateEnabledSource(index),
                    $"EnabledRetention_{index}",
                    analyzer,
                    cancellationToken));
        }
        ForceCollection();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        var retainedCompilationCount = compilations.Count(
            static compilation => compilation.TryGetTarget(out _));
        GC.KeepAlive(compilations);
        GC.KeepAlive(analyzer);
        return new EnabledAnalyzerRetentionMeasurement(
            retainedCompilationCount,
            Math.Max(0, after - before) / (1024d * 1024d));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Compilation> AnalyzeEnabledCompilation(
        string source,
        string assemblyName,
        DiagnosticAnalyzer analyzer,
        CancellationToken cancellationToken)
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            source,
            assemblyName);
        _ = AnalyzerGateHost.AnalyzeAsync(
                compilation,
                analyzer,
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
        CancellationToken cancellationToken)
    {
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
        {
            throw new InvalidOperationException("IDE edit marker is missing.");
        }

        var analyzer = new SharpProofAnalyzer();
        var currentCompilation = compilation;
        var currentTree = tree;
        var currentlyAllocates = false;

        for (var index = 0; index < contract.Warmups; index++)
        {
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
        for (var index = 0; index < latencies.Length; index++)
        {
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
            try
            {
                ValidateIdeDiagnostics(
                    diagnostics,
                    allocates,
                    index,
                    "measured");
            }
            catch (InvalidOperationException exception)
            {
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
        string phase)
    {
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
        {
            return;
        }

        throw new InvalidOperationException(
            $"IDE {phase} edit {editIndex} produced stale or duplicate " +
            $"diagnostics: expected [{string.Join(", ", expectedIds)}], " +
            $"actual [{string.Join(", ", actualIds)}], duplicates " +
            $"{duplicateCount}.");
    }

    internal static string CreateCallBearingUnannotatedAdvisorySource(
        int methodCount)
    {
        return CreateUnannotatedAdvisorySource(methodCount, callsMath: true);
    }

    private static string CreateCallFreeUnannotatedAdvisorySource(
        int methodCount)
    {
        return CreateUnannotatedAdvisorySource(methodCount, callsMath: false);
    }

    private static string CreateUnannotatedAdvisorySource(
        int methodCount,
        bool callsMath)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "public static class UnannotatedAdvisoryFixture {");
        if (callsMath)
        {
            builder.AppendLine(
                "    private static int Normalize(int value) => value;");
        }
        for (var index = 0; index < methodCount; index++)
        {
            var indexText = index.ToString(CultureInfo.InvariantCulture);
            builder.Append("    public static int M")
                .Append(indexText)
                .Append(callsMath
                    ? "(int value) => System.Math.Max(Normalize(value), "
                    : "(int value) => value + ")
                .Append(indexText)
                .AppendLine(callsMath ? ");" : ";");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string CreateEnabledSource(int index)
    {
        return $$"""
        using SharpProof.Attributes;

        public static class EnabledRetentionFixture_{{index}} {
            [ZeroAllocations]
            public static int Evaluate(int value) {
                return value + {{index}};
            }
        }
        """;
    }

    private static double Ratio(double numerator, double denominator)
    {
        return denominator <= 0 ? double.PositiveInfinity : numerator / denominator;
    }

    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static ImmutableArray<string> EvaluateRetainedMemoryLimits(
        double retainedRatio,
        double retainedIncreaseMiB,
        AcceptancePerformanceContract contract)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        if (retainedRatio > contract.MaximumRetainedMemoryRatio)
        {
            failures.Add(
                "Unannotated advisory retained memory ratio " +
                $"{Format(retainedRatio)} " +
                $"exceeds {Format(contract.MaximumRetainedMemoryRatio)}.");
        }

        if (retainedIncreaseMiB >
            contract.MaximumRetainedMemoryIncreaseMiB)
        {
            failures.Add(
                $"Unannotated advisory retained memory increase " +
                $"{Format(retainedIncreaseMiB)} MiB exceeds " +
                $"{contract.MaximumRetainedMemoryIncreaseMiB} MiB.");
        }

        return failures.ToImmutable();
    }

    internal static ImmutableArray<string>
        EvaluateEnabledAnalyzerRetentionLimits(
            int retainedCompilationCount,
            double retainedMemoryIncreaseMiB,
            AcceptancePerformanceContract contract)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        if (retainedCompilationCount >
            contract.MaximumEnabledRetainedCompilations)
        {
            failures.Add(
                $"Enabled analyzer retained {retainedCompilationCount} " +
                $"compilation graph(s); maximum is " +
                $"{contract.MaximumEnabledRetainedCompilations}.");
        }

        if (retainedMemoryIncreaseMiB >
            contract.MaximumEnabledRetainedMemoryIncreaseMiB)
        {
            failures.Add(
                $"Enabled analyzer retained memory increase " +
                $"{Format(retainedMemoryIncreaseMiB)} MiB exceeds " +
                $"{contract.MaximumEnabledRetainedMemoryIncreaseMiB} MiB.");
        }

        return failures.ToImmutable();
    }

    private static void ValidateContract(AcceptancePerformanceContract contract)
    {
        if (contract.Warmups != 5 ||
            contract.Samples != 30 ||
            contract.IdeEdits != 200)
        {
            throw new InvalidDataException(
                "The performance protocol is fixed at 5 warmups, " +
                "30 samples, and 200 IDE edits.");
        }

        if (contract.SmokeWarmups < 1 ||
            contract.SmokeSamples < 2 ||
            (contract.SmokeSamples & 1) != 0)
        {
            throw new InvalidDataException(
                "The performance smoke protocol requires positive warmups, " +
                "and a positive even sample count.");
        }

        if (contract.MaximumRetainedMemoryIncreaseMiB < 0 ||
            contract.MaximumEnabledRetainedCompilations < 0 ||
            contract.MaximumEnabledRetainedMemoryIncreaseMiB < 0)
        {
            throw new InvalidDataException(
                "The performance limits must be positive.");
        }
    }

    internal static void ValidateAdvisoryPackagePolicy(
        string repositoryRoot)
    {
        var portableRoot = Path.Combine(
            repositoryRoot,
            "SharpProof.Package",
            "buildTransitive");
        var verifierRoot = Path.Combine(
            repositoryRoot,
            "SharpProof.Verifier",
            "buildTransitive");
        var portableProps = XDocument.Load(Path.Combine(
            portableRoot,
            "SharpProof.props"));
        var portableTargets = XDocument.Load(Path.Combine(
            portableRoot,
            "SharpProof.targets"));
        var portableContract = XDocument.Load(Path.Combine(
            portableRoot,
            "SharpProof.ConsumerContract.props"));
        var verifierProps = XDocument.Load(Path.Combine(
            verifierRoot,
            "SharpProof.Verifier.props"));
        var verifierTargets = XDocument.Load(Path.Combine(
            verifierRoot,
            "SharpProof.Verifier.targets"));
        ValidateClosedPackagePolicy(
            portableProps,
            portableTargets,
            portableContract,
            verifierProps,
            verifierTargets);
        ValidateAdvisoryPackagePolicy(
            portableProps,
            portableTargets,
            portableContract,
            verifierProps,
            verifierTargets);
        ValidateEvaluatedAdvisoryPackagePolicy(
            portableRoot,
            verifierRoot);
    }

    private static void ValidateClosedPackagePolicy(
        XDocument portableProps,
        XDocument portableTargets,
        XDocument portableContract,
        XDocument verifierProps,
        XDocument verifierTargets)
    {
        var portableImports = portableTargets.Descendants("Import").ToArray();
        if (portableImports.Length != 1 ||
            !string.Equals(
                (string?)portableImports[0].Attribute("Project"),
                "$(_SharpProofConsumerContractPath)",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The portable package must import only its canonical " +
                "consumer contract.");
        }

        foreach (var document in new[]
                 {
                     portableProps,
                     portableContract,
                     verifierProps,
                     verifierTargets
                 })
        {
            if (document.Root?.Attribute("Sdk") != null ||
                document.Descendants().Any(static element =>
                    element.Name.LocalName is
                        "Import" or "ImportGroup" or "Sdk"))
            {
                throw new InvalidDataException(
                    "Package policy files must be closed over their MSBuild " +
                    "behavior and cannot import additional projects or SDKs.");
            }
        }

        foreach (var props in new[] { portableProps, verifierProps })
        {
            if (props.Descendants().Any(static element =>
                    element.Name.LocalName is "Target" or "UsingTask"))
            {
                throw new InvalidDataException(
                    "Package props files cannot register executable targets " +
                    "or tasks.");
            }
        }
    }

    private static void ValidateEvaluatedAdvisoryPackagePolicy(
        string portableRoot,
        string verifierRoot)
    {
        var temporary = Directory.CreateTempSubdirectory(
            "sharpproof-evaluated-package-policy-");
        try
        {
            var project = Path.Combine(temporary.FullName, "Policy.proj");
            new XDocument(
                new XElement(
                    "Project",
                    Import(portableRoot, "SharpProof.props"),
                    Import(verifierRoot, "SharpProof.Verifier.props"),
                    Import(portableRoot, "SharpProof.targets"),
                    Import(verifierRoot, "SharpProof.Verifier.targets")))
                .Save(project);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = temporary.FullName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(project);
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add(
                "-getProperty:SharpProofProfile,SharpProofFeatures," +
                "SharpProofVerify,_SharpProofProfileNormalized," +
                "SharpProofVerifyPolicy,SharpProofAssumptionPolicy," +
                "_SharpProofPortablePackagePresent," +
                "_SharpProofVerifierPackagePresent");
            startInfo.ArgumentList.Add("-getItem:Analyzer");
            foreach (var name in new[]
                     {
                         "SharpProofProfile",
                         "SharpProofFeatures",
                         "SharpProofVerify",
                         "SharpProofVerifyPolicy",
                         "SharpProofAssumptionPolicy",
                         "DesignTimeBuild",
                         "BuildingProject",
                         "_SharpProofProfileNormalized"
                     })
            {
                foreach (var key in startInfo.Environment.Keys
                             .Where(key => string.Equals(
                                 key,
                                 name,
                                 StringComparison.OrdinalIgnoreCase))
                             .ToArray())
                {
                    startInfo.Environment.Remove(key);
                }
            }
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            startInfo.Environment["DOTNET_NOLOGO"] = "1";

            using var process = Process.Start(startInfo) ??
                throw new InvalidDataException(
                    "The evaluated package policy probe did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(milliseconds: 30000))
            {
                GateProcess.KillTree(process);
                process.WaitForExit();
                throw new InvalidDataException(
                    "The evaluated package policy probe exceeded 30 seconds.");
            }
            var output = standardOutput.GetAwaiter().GetResult();
            var error = standardError.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    "The evaluated package policy probe failed: " +
                    TruncateProbeOutput(output + Environment.NewLine + error));
            }

            ValidateEvaluatedAdvisoryPackagePolicy(output);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }

        static XElement Import(string root, string file)
        {
            return new XElement(
                "Import",
                new XAttribute("Project", Path.Combine(root, file)));
        }
    }

    private static void ValidateEvaluatedAdvisoryPackagePolicy(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var properties = root.GetProperty("Properties");
            var analyzers = root.GetProperty("Items")
                .GetProperty("Analyzer")
                .EnumerateArray()
                .Select(static item => new
                {
                    Identity = item.GetProperty("Identity").GetString(),
                    Role = item.TryGetProperty(
                            "SharpProofAnalyzerRole",
                            out var role)
                        ? role.GetString()
                        : null,
                    Visible = item.TryGetProperty("Visible", out var visible)
                        ? visible.GetString()
                        : null
                })
                .ToArray();
            var entryPoint = analyzers.Where(static analyzer =>
                    string.Equals(
                        analyzer.Role,
                        "EntryPoint",
                        StringComparison.Ordinal))
                .ToArray();
            var generator = analyzers.Where(static analyzer =>
                    string.Equals(
                        analyzer.Role,
                        "Generator",
                        StringComparison.Ordinal))
                .ToArray();
            var dependencies = analyzers.Where(static analyzer =>
                    string.Equals(
                        analyzer.Role,
                        "Dependency",
                        StringComparison.Ordinal))
                .ToArray();
            if (!PropertyEquals(properties, "SharpProofProfile", "advisory") ||
                !PropertyEquals(properties, "SharpProofFeatures", "all") ||
                !PropertyEquals(properties, "SharpProofVerify", "false") ||
                !PropertyEquals(
                    properties,
                    "_SharpProofProfileNormalized",
                    "advisory") ||
                !PropertyEquals(
                    properties,
                    "SharpProofVerifyPolicy",
                    "advisory") ||
                !PropertyEquals(
                    properties,
                    "SharpProofAssumptionPolicy",
                    "allow") ||
                !PropertyEquals(
                    properties,
                    "_SharpProofPortablePackagePresent",
                    "true") ||
                !PropertyEquals(
                    properties,
                    "_SharpProofVerifierPackagePresent",
                    "true") ||
                analyzers.Length != 17 ||
                entryPoint.Length != 1 ||
                generator.Length != 1 ||
                dependencies.Length != 15 ||
                analyzers.Any(static analyzer => analyzer.Role is not
                    ("EntryPoint" or "Generator" or "Dependency")) ||
                !string.Equals(
                    Path.GetFileName(entryPoint[0].Identity),
                    "SharpProof.Analyzer.dll",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFileName(generator[0].Identity),
                    "SharpProof.ContractForGenerator.dll",
                    StringComparison.Ordinal) ||
                dependencies.Any(static dependency => !string.Equals(
                    dependency.Visible,
                    "false",
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "Evaluated package behavior must enable advisory analysis " +
                    "and omit verifier work by default.");
            }
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or
                InvalidOperationException)
        {
            throw new InvalidDataException(
                "The evaluated package policy probe returned malformed data.",
                exception);
        }
    }

    private static bool PropertyEquals(
        JsonElement properties,
        string name,
        string expected)
    {
        return properties.TryGetProperty(name, out var property) &&
            string.Equals(
                property.GetString(),
                expected,
                StringComparison.Ordinal);
    }

    private static string TruncateProbeOutput(string value)
    {
        const int maximumLength = 2000;
        return value.Length <= maximumLength
            ? value
            : value[..maximumLength] + "...";
    }

    internal static void ValidateAdvisoryPackagePolicy(
        XDocument portableProps,
        XDocument portableTargets,
        XDocument portableContract,
        XDocument verifierProps,
        XDocument verifierTargets)
    {
        var visibleProperties = portableProps
            .Descendants("CompilerVisibleProperty")
            .SelectMany(static element => SplitMsBuildList(
                (string?)element.Attribute("Include")))
            .ToHashSet(StringComparer.Ordinal);
        var profile = FindDefaultProperty(
            portableContract,
            "SharpProofProfile");
        var features = FindDefaultProperty(
            portableContract,
            "SharpProofFeatures");
        var verify = FindDefaultProperty(
            portableContract,
            "SharpProofVerify");
        var analyzerGroups = portableTargets.Descendants("ItemGroup")
            .Where(static group =>
                group.Elements("Analyzer").Any())
            .ToArray();
        var analyzerGroup = analyzerGroups.SingleOrDefault(group =>
            string.Equals(
                NormalizeMsBuildCondition(
                    (string?)group.Attribute("Condition")),
                "'$(_SharpProofProfileNormalized)'!='off'",
                StringComparison.Ordinal));
        var collectorGroup = analyzerGroups.SingleOrDefault(group =>
            string.Equals(
                NormalizeMsBuildCondition(
                    (string?)group.Attribute("Condition")),
                "'$(SharpProofVerify)'=='true'AND" +
                "'$(_SharpProofProfileNormalized)'!='off'AND" +
                "'$(DesignTimeBuild)'!='true'",
                StringComparison.Ordinal));
        const string analyzerDependencies =
            "$(_SharpProofSharedDirectory)/SharpProof.Analyzer.Core.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Contracts.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Dataflow.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Effects.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Frontend.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Ir.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Specs.dll;" +
            "$(_SharpProofSharedDirectory)/System.Buffers.dll;" +
            "$(_SharpProofSharedDirectory)/System.Collections.Immutable.dll;" +
            "$(_SharpProofSharedDirectory)/System.Memory.dll;" +
            "$(_SharpProofSharedDirectory)/System.Numerics.Vectors.dll;" +
            "$(_SharpProofSharedDirectory)/System.Reflection.Metadata.dll;" +
            "$(_SharpProofSharedDirectory)/System.Runtime.CompilerServices.Unsafe.dll;" +
            "$(_SharpProofSharedDirectory)/System.Text.Encoding.CodePages.dll;" +
            "$(_SharpProofSharedDirectory)/System.Threading.Tasks.Extensions.dll";
        const string collectorDependencies =
            "$(_SharpProofSharedDirectory)/Microsoft.Bcl.AsyncInterfaces.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.CompilerArtifact.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Summaries.dll;" +
            "$(_SharpProofSharedDirectory)/SharpProof.Worker.Protocol.dll;" +
            "$(_SharpProofSharedDirectory)/System.IO.Pipelines.dll;" +
            "$(_SharpProofSharedDirectory)/System.Text.Encodings.Web.dll;" +
            "$(_SharpProofSharedDirectory)/System.Text.Json.dll";
        var analyzerItemsValid =
            analyzerGroup?.Elements("Analyzer").Count() == 3 &&
            HasAnalyzerItem(
                analyzerGroup,
                "$(_SharpProofAnalyzerPath)",
                "EntryPoint") &&
            HasAnalyzerItem(
                analyzerGroup,
                "$(_SharpProofContractForGeneratorPath)",
                "Generator") &&
            HasAnalyzerItem(
                analyzerGroup,
                analyzerDependencies,
                "Dependency",
                "false");
        var collectorItemsValid =
            collectorGroup?.Elements("Analyzer").Count() == 2 &&
            HasAnalyzerItem(
                collectorGroup,
                "$(SharpProofCompilerCollectorPath)",
                "Collector") &&
            HasAnalyzerItem(
                collectorGroup,
                collectorDependencies,
                "CollectorDependency",
                "false");
        var verifierMarker = verifierProps
            .Descendants("_SharpProofVerifierPackagePresent")
            .SingleOrDefault();
        var verifierHost = verifierProps
            .Descendants("_SharpProofVerifierHostSupported")
            .SingleOrDefault();
        var verifyPolicy = FindDefaultProperty(
            verifierTargets,
            "SharpProofVerifyPolicy");
        var assumptionPolicy = FindDefaultProperty(
            verifierTargets,
            "SharpProofAssumptionPolicy");
        var verifierTarget = verifierTargets.Descendants("Target")
            .SingleOrDefault(static target =>
                string.Equals(
                    (string?)target.Attribute("Name"),
                    "SharpProofVerify",
                    StringComparison.Ordinal));
        var verifierCore = verifierTargets.Descendants("Target")
            .SingleOrDefault(static target =>
                string.Equals(
                    (string?)target.Attribute("Name"),
                    "_SharpProofVerifyCore",
                    StringComparison.Ordinal));
        var normalizedCondition = NormalizeMsBuildCondition(
            (string?)analyzerGroup?.Attribute("Condition"));
        var normalizedVerifierCondition = NormalizeMsBuildCondition(
            (string?)verifierTarget?.Attribute("Condition"));
        var normalizedHostCondition = NormalizeMsBuildCondition(
            (string?)verifierHost?.Attribute("Condition"));
        const string expectedHostCondition =
            "'$(MSBuildRuntimeType)'=='Core'AND" +
            "'$(SHARPPROOF_CONTAINER)'=='1'AND" +
            "'$(_SharpProofVerifierHostArchitecture)'=='X64'AND" +
            "'$(_SharpProofVerifierProcessArchitecture)'=='X64'";
        const string expectedVerifierCondition =
            "'$(_SharpProofVerifyActive)'=='true'AND" +
            "'$(_SharpProofVerifierHostSupported)'=='true'";
        var unexpectedCoreDependency = verifierTargets.Descendants("Target")
            .Where(target => !ReferenceEquals(target, verifierTarget))
            .Any(target => SplitMsBuildList(
                    (string?)target.Attribute("DependsOnTargets"))
                .Contains(
                    "_SharpProofVerifyCore",
                    StringComparer.Ordinal));
        var callTargetInvokesCore = verifierTargets.Descendants("CallTarget")
            .Any(call => SplitMsBuildList(
                    (string?)call.Attribute("Targets"))
                .Contains(
                    "_SharpProofVerifyCore",
                    StringComparer.Ordinal));
        var verifierExec = verifierTargets.Descendants("Exec").ToArray();
        var verifierRun = verifierTargets
            .Descendants("SharpProof.BuildTasks.RunVerifier")
            .ToArray();
        var verifierRunnerTask = verifierTargets.Descendants("UsingTask")
            .SingleOrDefault(static task => string.Equals(
                (string?)task.Attribute("TaskName"),
                "SharpProof.BuildTasks.RunVerifier",
                StringComparison.Ordinal));
        var inlineTaskFactories = verifierTargets.Descendants("UsingTask")
            .Where(static task => task.Attribute("TaskFactory") != null)
            .ToArray();
        var portableContainsVerifierWork =
            portableTargets.Descendants("Exec").Any() ||
            portableTargets.Descendants("SharpProof.BuildTasks.RunVerifier").Any() ||
            portableTargets.Descendants("Target").Any(static target =>
                (string?)target.Attribute("Name") is
                    "SharpProofVerify" or "_SharpProofVerifyCore");
        if (!visibleProperties.Contains("SharpProofProfile") ||
            !visibleProperties.Contains("SharpProofFeatures") ||
            !string.Equals(profile?.Value, "advisory", StringComparison.Ordinal) ||
            !string.Equals(features?.Value, "all", StringComparison.Ordinal) ||
            !string.Equals(verify?.Value, "false", StringComparison.Ordinal) ||
            portableContainsVerifierWork ||
            analyzerGroups.Length != 2 ||
            collectorGroup == null ||
            !analyzerItemsValid ||
            !collectorItemsValid ||
            !string.Equals(
                normalizedCondition,
                "'$(_SharpProofProfileNormalized)'!='off'",
                StringComparison.Ordinal) ||
            !string.Equals(
                verifierMarker?.Value,
                "true",
                StringComparison.Ordinal) ||
            !string.Equals(
                normalizedHostCondition,
                expectedHostCondition,
                StringComparison.Ordinal) ||
            !string.Equals(
                verifyPolicy?.Value,
                "advisory",
                StringComparison.Ordinal) ||
            !string.Equals(
                assumptionPolicy?.Value,
                "allow",
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
                "_SharpProofVerifyCore",
                StringComparison.Ordinal) ||
            verifierCore == null ||
            verifierCore.Attribute("BeforeTargets") != null ||
            verifierCore.Attribute("AfterTargets") != null ||
            unexpectedCoreDependency ||
            callTargetInvokesCore ||
            verifierExec.Length != 0 ||
            verifierRun.Length != 1 ||
            inlineTaskFactories.Length != 0 ||
            verifierRunnerTask == null ||
            !string.Equals(
                (string?)verifierRunnerTask.Attribute("AssemblyFile"),
                "$(_SharpProofBuildTasksPath)",
                StringComparison.Ordinal) ||
            !ReferenceEquals(
                verifierRun[0].Ancestors("Target").SingleOrDefault(),
                verifierCore))
        {
            throw new InvalidDataException(
                "The package must run advisory analysis but omit the verifier " +
                "by default.");
        }
    }

    private static XElement? FindDefaultProperty(
        XDocument document,
        string name)
    {
        return document.Descendants(name).SingleOrDefault(element =>
            string.Equals(
                (string?)element.Attribute("Condition"),
                $"'$({name})' == ''",
                StringComparison.Ordinal));
    }

    private static string NormalizeMsBuildCondition(string? condition)
    {
        return string.Concat((condition ?? string.Empty)
            .Where(static character => !char.IsWhiteSpace(character)));
    }

    private static bool HasAnalyzerItem(
        XElement? group,
        string expectedInclude,
        string expectedRole,
        string? expectedVisibility = null)
    {
        var expectedPaths = SplitMsBuildList(expectedInclude);
        return group?.Elements("Analyzer").Count(analyzer =>
            SplitMsBuildList((string?)analyzer.Attribute("Include"))
                .SequenceEqual(expectedPaths, StringComparer.Ordinal) &&
            string.Equals(
                analyzer.Element("SharpProofAnalyzerRole")?.Value,
                expectedRole,
                StringComparison.Ordinal) &&
            string.Equals(
                analyzer.Element("Visible")?.Value,
                expectedVisibility,
                StringComparison.Ordinal)) == 1;
    }

    private static ImmutableArray<string> SplitMsBuildList(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(
                    [';'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => item.Trim())
                .Where(static item => item.Length != 0)];
    }

    private sealed record EnabledAnalyzerRetentionMeasurement(
        int RetainedCompilationCount,
        double RetainedMemoryIncreaseMiB);

    private sealed record PackageBuildTiming(
        PackageBuildSdkIdentity Sdk,
        ImmutableArray<PackageBuildSample> Samples);

    private readonly record struct PackageBuildPair(
        double BaselineMilliseconds,
        double UnannotatedAdvisoryMilliseconds);

    internal sealed record UnannotatedAdvisoryBatchMeasurement(
        double MeanMilliseconds,
        int AnalyzerDriverRunCount,
        int DiagnosticCount,
        int AnalysisSessionCreateCount,
        int ApiSpecCreateCount,
        int EffectAnalysisCreateCount);

    private sealed record IdeEditMeasurement(
        double[] Latencies,
        ImmutableArray<string> DiagnosticFailures);

    private sealed class CountingSessionFactory : IAnalyzerSessionFactory
    {
        private readonly List<AnalyzerSession> _sessions = [];

        internal int CreateCount
        {
            get; private set;
        }
        internal int ApiSpecCreateCount =>
            _sessions.Count(static session => session.HasCreatedApiSpecs);
        internal int EffectAnalysisCreateCount =>
            _sessions.Count(static session => session.HasCreatedEffectAnalysis);

        public AnalyzerSession Create(
            Compilation compilation,
            SharpProof.Analyzer.Configuration.AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            var session = new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken);
            _sessions.Add(session);
            return session;
        }
    }

}
