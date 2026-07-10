using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer;

internal sealed class AnalyzerSession : IDisposable
{
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

        ExceptionSummaryCatalog = Features.Includes(AnalyzerFeatures.Exceptions) &&
                                  Configuration.EnableEffectSummaryJson
            ? ExceptionSummaryCatalog.FromOptionsWithCompatibilityReporter(
                options,
                cancellationToken,
                EffectSummaryCompatibilityReporter)
            : ExceptionSummaryCatalog.Empty;
        GeneratedPurityCatalog = Features.Includes(AnalyzerFeatures.Purity) &&
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

    public void Dispose()
    {
        if (_purityService.IsValueCreated) _purityService.Value.Dispose();
    }
}
