using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;
using SharpProof.Attributes;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public class AnalyzerHostConcurrencyStressTests
{
    private const string IsolationSourcePath = "src/AnalyzerHostIsolation.cs";

    private static readonly ImmutableDictionary<string, string> BaseOptions =
        ImmutableDictionary<string, string>.Empty
            .Add("sharpproof_suggest_missing_enforce_pure", "false");

    [Test]
    public async Task ConcurrentAnalyzerRuns_ReportDeterministicDiagnosticFingerprints()
    {
        const int methodCount = 12;
        const int runCount = 6;
        var source = CreateDeterminismSource(methodCount);

        var tasks = Enumerable.Range(0, runCount)
            .Select(index => AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                globalOptions: BaseOptions,
                sourcePath: "src/AnalyzerHostDeterminism.cs",
                concurrentAnalysis: true,
                compilationName: $"AnalyzerHostDeterminism{index}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var expectedFingerprint = GetDiagnosticFingerprint(results[0]);

        foreach (var diagnostics in results)
        {
            AssertNoAnalyzerFailures(diagnostics);
            Assert.That(
                diagnostics.Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
                Is.EqualTo(methodCount));
            Assert.That(
                diagnostics.Count(diagnostic =>
                    diagnostic.Id == SharpProofDiagnostics.AllocationInZeroAllocationMethodId),
                Is.EqualTo(methodCount));
            Assert.That(
                diagnostics.Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.EnsuresNotProvenId),
                Is.EqualTo(methodCount));
            Assert.That(GetDiagnosticFingerprint(diagnostics), Is.EqualTo(expectedFingerprint));
        }
    }

    [Test]
    public async Task ConcurrentCompilations_IsolateConfigurationCatalogsBaselinesAndEffectSummaries()
    {
        const int runCount = 12;
        const string tickCountSymbol = "System.Environment.get_TickCount()";
        var pureTickCountSummary = GeneratedPurityTestSupport.CreatePuritySummaryAdditionalText(
            "Pure.TickCount.SharpProof.EffectSummary.json",
            typeof(Environment).Assembly.Location,
            tickCountSymbol,
            "pure",
            "[]");
        var unknownTickCountSummary = GeneratedPurityTestSupport.CreatePuritySummaryAdditionalText(
            "Unknown.TickCount.SharpProof.EffectSummary.json",
            typeof(Environment).Assembly.Location,
            tickCountSymbol,
            "conservative_unknown",
            "[\"metadata_only_or_external\"]");
        var timeoutSummary = new AnalyzerTestHost.InMemoryAdditionalText(
            "Timeout.ThrowIfNull.SharpProof.EffectSummary.json",
            GeneratedPurityTestSupport.CreateEffectSummaryJson(
                typeof(ArgumentNullException).Assembly.Location,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                Array.Empty<string>(),
                "System.TimeoutException"));
        var baseline = new AnalyzerTestHost.InMemoryAdditionalText(
            "SharpProof.Baseline.json",
            """
            {
              "version": 1,
              "evidenceSchemaVersion": 2,
              "evidenceSchemaCompatibility": "exact-v2",
              "diagnostics": [
                {
                  "id": "SP0002",
                  "symbol": "M:IsolationTarget.BaselineTarget",
                  "path": "src/AnalyzerHostIsolation.cs",
                  "evidenceSchemaVersion": 2,
                  "evidenceSchemaCompatibility": "exact-v2"
                }
              ]
            }
            """);

        var configuredDangerKey = ConfiguredMemberKeyTestFactory.Method("Configured", "Danger");
        var quietOptions = BaseOptions
            .Add("sharpproof_known_pure_methods", configuredDangerKey)
            .Add("sharpproof_report_exceptions", "true");
        var loudOptions = BaseOptions
            .Add("sharpproof_known_impure_methods", configuredDangerKey)
            .Add("sharpproof_report_exceptions", "true");
        var quietFiles = ImmutableArray.Create<AdditionalText>(pureTickCountSummary, baseline);
        var loudFiles = ImmutableArray.Create<AdditionalText>(unknownTickCountSummary, timeoutSummary);

        var tasks = Enumerable.Range(0, runCount)
            .Select(index => AnalyzerTestHost.GetDiagnosticsAsync(
                IsolationSource,
                globalOptions: index % 2 == 0 ? quietOptions : loudOptions,
                additionalFiles: index % 2 == 0 ? quietFiles : loudFiles,
                sourcePath: IsolationSourcePath,
                concurrentAnalysis: true,
                compilationName: $"AnalyzerHostIsolation{index}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        for (var index = 0; index < results.Length; index++)
        {
            var diagnostics = results[index];
            var purityDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .ToImmutableArray();
            var reportsTimeout = diagnostics.Any(diagnostic =>
                diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId &&
                diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionTypesProperty, out var types) &&
                types != null &&
                types.Contains("System.TimeoutException", StringComparison.Ordinal));

            AssertNoAnalyzerFailures(diagnostics);
            Assert.That(
                diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.InvalidAdditionalFileId),
                Is.False);
            if (index % 2 == 0)
            {
                Assert.That(purityDiagnostics, Is.Empty,
                    $"Quiet compilation {index} observed state from a loud compilation.");
                Assert.That(reportsTimeout, Is.False,
                    $"Quiet compilation {index} observed another compilation's exception summary.");
            }
            else
            {
                Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
                Assert.That(
                    purityDiagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                    Has.Some.Contains("TickCountTarget"));
                Assert.That(
                    purityDiagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                    Has.Some.Contains("ConfiguredTarget"));
                Assert.That(
                    purityDiagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                    Has.Some.Contains("BaselineTarget"));
                Assert.That(reportsTimeout, Is.True,
                    $"Loud compilation {index} lost its exception-summary state.");
            }
        }
    }

    [Test]
    public async Task CompilationPurityService_ConcurrentPurityAndSmtQueriesShareLiveState()
    {
        const int methodCount = 20;
        const int smtQueryCount = 12;
        var syntaxTrees = Enumerable.Range(0, methodCount)
            .Select(index => CSharpSyntaxTree.ParseText(
                $$"""
                  using SharpProof.Attributes;

                  public sealed class PurityTarget{{index}}
                  {
                      [EnforcePure]
                      public int Evaluate(int value) => value + {{index}};
                  }
                  """,
                new CSharpParseOptions(LanguageVersion.Preview),
                $"src/PurityTarget{index}.cs"))
            .ToImmutableArray();
        var references = AnalyzerTestHost.GetTrustedPlatformReferences()
            .Add(MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SharedCompilationPurityService",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var enforcePureAttribute = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var requests = syntaxTrees
            .Select((tree, index) =>
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var containingType = compilation.GetTypeByMetadataName($"PurityTarget{index}")!;
                var method = containingType.GetMembers("Evaluate").OfType<IMethodSymbol>().Single();
                return (Method: method, SemanticModel: semanticModel);
            })
            .ToImmutableArray();

        using var service = new CompilationPurityService(compilation);
        using var start = new ManualResetEventSlim(false);
        var purityTasks = requests
            .Select(request => Task.Run(() =>
            {
                start.Wait();
                return service.GetPurity(
                    request.Method,
                    request.SemanticModel,
                    enforcePureAttribute,
                    null,
                    CancellationToken.None);
            }))
            .ToArray();
        var smtTasks = Enumerable.Range(0, smtQueryCount)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                var left = new SmtVariable($"shared_left_{index}", SmtValueKind.Int);
                var right = new SmtVariable($"shared_right_{index}", SmtValueKind.Int);
                var one = new SmtIntegerConstant(1);
                return service.SmtAnalysis.ClassifyPathFeasibility(new SmtFormula[]
                {
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        left,
                        new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, right, one)),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        right,
                        new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, left, one))
                });
            }))
            .ToArray();

        start.Set();
        var purityResults = await Task.WhenAll(purityTasks);
        var smtResults = await Task.WhenAll(smtTasks);

        Assert.That(purityResults.Select(result => result.IsPure), Is.All.True);
        Assert.That(smtResults.Select(result => result.Reason), Has.None.EqualTo("smt_disposed"));
        Assert.That(service.SmtAnalysis.Health.State, Is.Not.EqualTo(SmtAnalysisHealthState.Disposed));

        var serviceType = typeof(CompilationPurityService);
        var callGraph = serviceType
            .GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service);
        var fixedPoint = serviceType
            .GetField("_fixedPoint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service);
        var purityCache = serviceType
            .GetField("_purityCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;

        Assert.That(callGraph, Is.Not.Null);
        Assert.That(fixedPoint, Is.Not.Null);
        Assert.That(GetCount(purityCache), Is.EqualTo(methodCount));
    }

    [Test]
    public async Task CanceledConcurrentCallbacks_DoNotPoisonLaterAnalyzerRuns()
    {
        const int methodCount = 48;
        var source = CreateCancellationSource(methodCount);
        var analyzer = new SharpProofAnalyzer();
        var blockingOptions = new CoordinatedAnalyzerConfigOptionsProvider(BaseOptions, blockOnTreeRead: 1);
        var canceledCompilation = CreateCompilation(source, "CanceledAnalyzerCallbacks");
        using var cancellation = new CancellationTokenSource();
        var canceledAnalysis = AnalyzeAsync(
            canceledCompilation,
            analyzer,
            blockingOptions,
            cancellation.Token);

        try
        {
            await blockingOptions.Blocked.WaitAsync(TimeSpan.FromSeconds(10));
            await blockingOptions.ConcurrentReadObserved.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            blockingOptions.Release();
        }

        OperationCanceledException? cancellationException = null;
        try
        {
            await canceledAnalysis;
        }
        catch (OperationCanceledException exception)
        {
            cancellationException = exception;
        }

        Assert.That(cancellationException, Is.Not.Null);
        Assert.That(blockingOptions.TreeReadCount, Is.GreaterThan(1),
            "The cancellation must occur while analyzer callbacks overlap.");

        var healthyTasks = Enumerable.Range(0, 2)
            .Select(index => AnalyzeAsync(
                CreateCompilation(source, $"RecoveredAnalyzerCallbacks{index}"),
                analyzer,
                new CoordinatedAnalyzerConfigOptionsProvider(BaseOptions),
                CancellationToken.None))
            .ToArray();
        var healthyResults = await Task.WhenAll(healthyTasks);
        var expectedFingerprint = GetDiagnosticFingerprint(healthyResults[0]);

        foreach (var diagnostics in healthyResults)
        {
            AssertNoAnalyzerFailures(diagnostics);
            Assert.That(
                diagnostics.Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
                Is.EqualTo(methodCount));
            Assert.That(GetDiagnosticFingerprint(diagnostics), Is.EqualTo(expectedFingerprint));
        }
    }

    private static string CreateDeterminismSource(int methodCount)
    {
        var methods = string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(0, methodCount).Select(index => $$"""
                [EnforcePure]
                public void Impure{{index}}()
                {
                    Console.WriteLine({{index}});
                }

                [ZeroAllocations]
                public object Allocate{{index}}() => new object();

                [Ensures("result > 0")]
                public int Ensure{{index}}() => -1;

                [EnforcePure]
                public void Unreachable{{index}}(int value)
                {
                    if (value > {{index}} && value <= {{index}})
                    {
                        Console.WriteLine(value);
                    }
                }
                """));

        return $$"""
            using System;
            using SharpProof.Attributes;

            public sealed class AnalyzerHostDeterminismTarget
            {
            {{methods}}
            }
            """;
    }

    private static string CreateCancellationSource(int methodCount)
    {
        var methods = string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(0, methodCount).Select(index => $$"""
                [EnforcePure]
                public void Impure{{index}}()
                {
                    Console.WriteLine({{index}});
                }
                """));

        return $$"""
            using System;
            using SharpProof.Attributes;

            public sealed class CancellationTarget
            {
            {{methods}}
            }
            """;
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "src/CancellationTarget.cs");
        var references = AnalyzerTestHost.GetTrustedPlatformReferences()
            .Add(MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        Compilation compilation,
        DiagnosticAnalyzer analyzer,
        AnalyzerConfigOptionsProvider optionsProvider,
        CancellationToken cancellationToken)
    {
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, optionsProvider);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer),
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                null,
                true,
                false,
                false));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
    }

    private static ImmutableArray<string> GetDiagnosticFingerprint(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Select(diagnostic =>
            {
                var lineSpan = diagnostic.Location.GetLineSpan();
                var properties = string.Join(
                    ";",
                    diagnostic.Properties
                        .OrderBy(property => property.Key, StringComparer.Ordinal)
                        .Select(property => property.Key + "=" + property.Value));
                return string.Join(
                    "|",
                    diagnostic.Id,
                    diagnostic.Severity,
                    lineSpan.Path ?? string.Empty,
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length,
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    properties);
            })
            .OrderBy(fingerprint => fingerprint, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AssertNoAnalyzerFailures(ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.That(
            diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("AD", StringComparison.Ordinal)),
            Is.Empty);
    }

    private static int GetCount(object instance)
    {
        return (int)instance.GetType().GetProperty("Count")!.GetValue(instance)!;
    }

    private const string IsolationSource = """
        using System;
        using SharpProof.Attributes;

        public static class Configured
        {
            public static void Danger()
            {
            }
        }

        public sealed class IsolationTarget
        {
            [EnforcePure]
            public int TickCountTarget() => Environment.TickCount;

            [EnforcePure]
            public void ConfiguredTarget() => Configured.Danger();

            [EnforcePure]
            public void BaselineTarget() => Console.WriteLine("baseline");

            public void ExceptionTarget(object value) => ArgumentNullException.ThrowIfNull(value);
        }
        """;

    private sealed class CoordinatedAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly int? _blockOnTreeRead;
        private readonly TaskCompletionSource _blockedSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _concurrentReadSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private readonly AnalyzerConfigOptions _treeOptions = new DictionaryAnalyzerConfigOptions(
            ImmutableDictionary<string, string>.Empty);
        private int _isBlocked;
        private int _treeReadCount;

        public CoordinatedAnalyzerConfigOptionsProvider(
            ImmutableDictionary<string, string> globalOptions,
            int? blockOnTreeRead = null)
        {
            GlobalOptions = new DictionaryAnalyzerConfigOptions(globalOptions);
            _blockOnTreeRead = blockOnTreeRead;
        }

        public Task Blocked => _blockedSource.Task;

        public Task ConcurrentReadObserved => _concurrentReadSource.Task;

        public int TreeReadCount => Volatile.Read(ref _treeReadCount);

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            var read = Interlocked.Increment(ref _treeReadCount);
            if (_blockOnTreeRead == read)
            {
                Volatile.Write(ref _isBlocked, 1);
                _blockedSource.TrySetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Timed out waiting to release a blocked analyzer callback.");
                Volatile.Write(ref _isBlocked, 0);
            }
            else if (Volatile.Read(ref _isBlocked) != 0)
            {
                _concurrentReadSource.TrySetResult();
            }

            return _treeOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _treeOptions;
        }

        public void Release()
        {
            _release.Set();
        }
    }

    private sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _options;

        public DictionaryAnalyzerConfigOptions(ImmutableDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_options.TryGetValue(key, out var configuredValue))
            {
                value = configuredValue;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
