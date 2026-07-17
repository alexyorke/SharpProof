using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal sealed class AnalyzerSession : IDisposable
{
    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<MethodBodyAnalysisState>> _methodBodyAnalyses =
        new(SymbolEq.Default);

    private readonly ConcurrentDictionary<string, TrustedBoundaryReviewFinding> _trustedBoundaryFindings =
        new(StringComparer.Ordinal);

    private readonly Lazy<CompilationPurityService> _purityService;

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        AnalyzerFeatures requestedFeatures)
    {
        Features = AnalyzerFeatureDependencies.Expand(requestedFeatures);
        Configuration = AnalyzerConfiguration.FromOptions(options);
        AttributePolicy = SharpProofAttributeIdentityPolicy.Create(Configuration.AttributeStubNamespaces);
        Baseline = DiagnosticBaseline.FromOptions(options, cancellationToken);
        EffectSummaryCompatibilityReporter = new EffectSummaryCompatibilityReporter();

        _purityService = new Lazy<CompilationPurityService>(
            () => new CompilationPurityService(
                compilation,
                Configuration.SmtOptions,
                AttributePolicy,
                Configuration.AnalysisLimits),
            LazyThreadSafetyMode.ExecutionAndPublication);

        ExceptionSummaryCatalog = (Features.Includes(AnalyzerFeatures.Exceptions) ||
                                   Features.Includes(AnalyzerFeatures.Suggestions)) &&
                                  Configuration.EnableEffectSummaryJson
            ? ExceptionSummaryCatalog.FromOptionsWithCompatibilityReporter(
                options,
                cancellationToken,
                EffectSummaryCompatibilityReporter)
            : ExceptionSummaryCatalog.Empty;
        GeneratedPurityCatalog = (Features.Includes(AnalyzerFeatures.Purity) ||
                                  Configuration.TrustedBoundaryReviewMode != TrustedBoundaryReviewMode.Off) &&
                                 Configuration.EnableEffectSummaryJson
            ? GeneratedPurityCatalog.FromOptionsWithCompatibilityReporter(
                options,
                cancellationToken,
                EffectSummaryCompatibilityReporter)
            : null;
    }

    internal AnalyzerFeatures Features { get; }

    internal AnalyzerConfiguration Configuration { get; }

    internal SharpProofAttributeIdentityPolicy AttributePolicy { get; }

    internal DiagnosticBaseline Baseline { get; }

    internal EffectSummaryCompatibilityReporter EffectSummaryCompatibilityReporter { get; }

    internal ExceptionSummaryCatalog ExceptionSummaryCatalog { get; }

    internal GeneratedPurityCatalog? GeneratedPurityCatalog { get; }

    internal CompilationPurityService PurityService => _purityService.Value;

    internal int MethodBodyAnalysisCount => _methodBodyAnalyses.Count;

    internal void RecordTrustedBoundaryFinding(TrustedBoundaryReviewFinding finding)
    {
        _trustedBoundaryFindings.AddOrUpdate(
            finding.Key,
            finding,
            (_, existing) => CompareFindingLocation(finding, existing) < 0 ? finding : existing);
    }

    internal ImmutableArray<TrustedBoundaryReviewFinding> GetTrustedBoundaryFindings()
    {
        return _trustedBoundaryFindings.Values
            .OrderBy(static finding => finding.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Location.SourceSpan.Start)
            .ThenBy(static finding => finding.SymbolDisplay, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Source, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal MethodBodyAnalysisState GetOrCreateMethodBodyAnalysis(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {
        var lazy = _methodBodyAnalyses.GetOrAdd(
            methodSymbol,
            _ => new Lazy<MethodBodyAnalysisState>(
                () => new MethodBodyAnalysisState(
                    methodSymbol,
                    declaration,
                    semanticModel,
                    operationBlocks,
                    MethodBodyOperationResolver.GetMethodBodyRootOperation(
                        declaration,
                        semanticModel,
                        cancellationToken),
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            if (_methodBodyAnalyses.TryGetValue(methodSymbol, out var current) &&
                ReferenceEquals(current, lazy))
                _methodBodyAnalyses.TryRemove(methodSymbol, out _);

            throw;
        }
    }

    public void Dispose()
    {
        if (_purityService.IsValueCreated) _purityService.Value.Dispose();
        _methodBodyAnalyses.Clear();
        _trustedBoundaryFindings.Clear();
    }

    private static int CompareFindingLocation(
        TrustedBoundaryReviewFinding left,
        TrustedBoundaryReviewFinding right)
    {
        var pathComparison = string.CompareOrdinal(
            left.Location.SourceTree?.FilePath ?? string.Empty,
            right.Location.SourceTree?.FilePath ?? string.Empty);
        if (pathComparison != 0) return pathComparison;

        var startComparison = left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
        return startComparison != 0
            ? startComparison
            : left.Location.SourceSpan.Length.CompareTo(right.Location.SourceSpan.Length);
    }
}
