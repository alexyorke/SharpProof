using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer
{
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

            _purityService = new Lazy<CompilationPurityService>(
                () => new CompilationPurityService(
                    compilation,
                    Configuration.SmtOptions,
                    AttributePolicy),
                LazyThreadSafetyMode.ExecutionAndPublication);

            ExceptionSummaryCatalog = Features.Includes(AnalyzerFeatures.Exceptions) &&
                Configuration.EnableEffectSummaryJson
                    ? ExceptionSummaryCatalog.FromOptions(options, cancellationToken)
                    : ExceptionSummaryCatalog.Empty;
            GeneratedPurityCatalog = Features.Includes(AnalyzerFeatures.Purity) &&
                Configuration.EnableEffectSummaryJson
                    ? GeneratedPurityCatalog.FromOptions(options, cancellationToken)
                    : null;
        }

        internal AnalyzerFeatures Features { get; }

        internal AnalyzerConfiguration Configuration { get; }

        internal SharpProofAttributeIdentityPolicy AttributePolicy { get; }

        internal DiagnosticBaseline Baseline { get; }

        internal ExceptionSummaryCatalog ExceptionSummaryCatalog { get; }

        internal GeneratedPurityCatalog? GeneratedPurityCatalog { get; }

        internal CompilationPurityService PurityService => _purityService.Value;

        public void Dispose()
        {
            if (_purityService.IsValueCreated)
            {
                _purityService.Value.Dispose();
            }
        }
    }
}
