using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SharpProof.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SharpProofAnalyzer : DiagnosticAnalyzer
    {

        public const string SP0002 = SharpProofDiagnostics.PurityNotVerifiedId;
        public const string SP0004 = SharpProofDiagnostics.MissingEnforcePureAttributeId;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(SharpProofDiagnostics.PurityNotVerifiedRule,
                                  SharpProofDiagnostics.MisplacedAttributeRule,
                                  SharpProofDiagnostics.MissingEnforcePureAttributeRule,
                                  SharpProofDiagnostics.ConflictingPurityAttributesRule,
                                  SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeRule,
                                  SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeRule,
                                  SharpProofDiagnostics.RedundantAllowSynchronizationRule,
                                  SharpProofDiagnostics.PurityExplanationRule,
                                  SharpProofDiagnostics.ExceptionSummaryRule,
                                  SharpProofDiagnostics.UncaughtExceptionSiteRule,
                                  SharpProofDiagnostics.BclFallbackGuessRule,
                                  SharpProofDiagnostics.AllocationInZeroAllocationMethodRule,
                                  SharpProofDiagnostics.MisplacedZeroAllocationsAttributeRule,
                                  SharpProofDiagnostics.CapabilityViolationRule,
                                  SharpProofDiagnostics.CapabilityUnknownRule,
                                  SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeRule,
                                  SharpProofDiagnostics.EnsuresNotProvenRule,
                                  SharpProofDiagnostics.EnsuresUnsupportedRule,
                                  SharpProofDiagnostics.MisplacedEnsuresAttributeRule,
                                  SharpProofDiagnostics.ComplexityExceededRule,
                                  SharpProofDiagnostics.ComplexityCouldNotBeVerifiedRule,
                                  SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule,
                                  SharpProofDiagnostics.InvalidContractArgumentRule,
                                  SharpProofDiagnostics.InvalidAnalyzerConfigurationRule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(startContext =>
            {
                var config = Configuration.AnalyzerConfiguration.FromOptions(startContext.Options);
                var purityService = new Engine.CompilationPurityService(startContext.Compilation, config.SmtOptions);
                var invalidConfigurationValues = config.InvalidConfigurationValues;
                var missingPuritySuggestions = config.MissingPuritySuggestions;
                var emitExplanations = config.EmitExplanations;
                var reportBclFallbackGuesses = config.ReportBclFallbackGuesses;
                var baseline = Configuration.DiagnosticBaseline.FromOptions(startContext.Options, startContext.CancellationToken);
                var exceptionSummaryCatalog = config.EnableEffectSummaryJson
                    ? ExceptionSummaryCatalog.FromOptions(startContext.Options, startContext.CancellationToken)
                    : ExceptionSummaryCatalog.Empty;
                var generatedPurityCatalog = config.EnableEffectSummaryJson
                    ? GeneratedPurityCatalog.FromOptions(startContext.Options, startContext.CancellationToken)
                    : null;

                startContext.RegisterCompilationEndAction(endContext =>
                {
                    foreach (var invalidConfigurationValue in invalidConfigurationValues)
                    {
                        endContext.ReportDiagnostic(CreateInvalidConfigurationDiagnostic(invalidConfigurationValue));
                    }

                    purityService.Dispose();
                });

                startContext.RegisterSyntaxNodeAction(c =>
                {
                    using (generatedPurityCatalog == null ? null : GeneratedPurityCatalog.UseCurrent(generatedPurityCatalog))
                    using (Engine.ImpurityCatalog.UseConfiguredOverrides(config))
                    {
                        MethodPurityAnalyzer.AnalyzeSymbolForPurity(c, purityService, missingPuritySuggestions, emitExplanations, reportBclFallbackGuesses, baseline);
                        MethodAllocationAnalyzer.AnalyzeSymbolForZeroAllocations(c, baseline);
                        MethodCapabilityAnalyzer.AnalyzeSymbolForCapabilities(c, baseline);
                        MethodEnsuresAnalyzer.AnalyzeSymbolForEnsures(c, purityService, baseline);
                        MethodExpectedComplexityAnalyzer.AnalyzeSymbolForExpectedComplexity(c, baseline);
                        ExceptionFlowAnalyzer.AnalyzeSymbolForExceptions(c, config, exceptionSummaryCatalog, purityService);
                    }
                },
                    SyntaxKind.AddAccessorDeclaration,
                    SyntaxKind.MethodDeclaration,
                    SyntaxKind.GetAccessorDeclaration,
                    SyntaxKind.InitAccessorDeclaration,
                    SyntaxKind.IndexerDeclaration,
                    SyntaxKind.RemoveAccessorDeclaration,
                    SyntaxKind.PropertyDeclaration,
                    SyntaxKind.SetAccessorDeclaration,
                    SyntaxKind.ConstructorDeclaration,
                    SyntaxKind.ConversionOperatorDeclaration,
                    SyntaxKind.OperatorDeclaration,
                    SyntaxKind.LocalFunctionStatement);
            });

            context.RegisterSyntaxNodeAction(AttributePlacementAnalyzer.AnalyzeNonMethodDeclaration, SyntaxKind.AttributeList);
            context.RegisterSyntaxTreeAction(AnalyzeTreeConfiguration);
        }

        private static void AnalyzeTreeConfiguration(SyntaxTreeAnalysisContext context)
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
            var invalidConfigurationValues = Configuration.AnalyzerConfiguration.GetInvalidTreeConfigurationValues(options);
            var location = Location.Create(context.Tree, new TextSpan(0, 0));
            foreach (var invalidConfigurationValue in invalidConfigurationValues)
            {
                context.ReportDiagnostic(CreateInvalidConfigurationDiagnostic(invalidConfigurationValue, location));
            }
        }

        private static Diagnostic CreateInvalidConfigurationDiagnostic(
            Configuration.InvalidAnalyzerConfigurationValue invalidConfigurationValue,
            Location? location = null)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ConfigurationKeyProperty, invalidConfigurationValue.Key)
                .Add(SharpProofDiagnostics.ConfigurationValueProperty, invalidConfigurationValue.Value)
                .Add(SharpProofDiagnostics.ConfigurationInvalidReasonProperty, invalidConfigurationValue.Reason);

            return Diagnostic.Create(
                SharpProofDiagnostics.InvalidAnalyzerConfigurationRule,
                location ?? Location.None,
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[]
                {
                    invalidConfigurationValue.Key,
                    invalidConfigurationValue.Value,
                    invalidConfigurationValue.Reason,
                });
        }
    }
}
