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
                                  SharpProofDiagnostics.InvalidAnalyzerConfigurationRule,
                                  SharpProofDiagnostics.UnrecognizedAttributeIdentityRule,
                                  SharpProofDiagnostics.RequiresNotProvenRule,
                                  SharpProofDiagnostics.RequiresUnsupportedRule,
                                  SharpProofDiagnostics.MisplacedRequiresAttributeRule,
                                  SharpProofDiagnostics.ExceptionContractViolationRule,
                                  SharpProofDiagnostics.MisplacedExceptionContractAttributeRule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(startContext =>
            {
                var config = Configuration.AnalyzerConfiguration.FromOptions(startContext.Options);
                var attributePolicy = SharpProofAttributeIdentityPolicy.Create(config.AttributeStubNamespaces);
                var purityService = new Engine.CompilationPurityService(startContext.Compilation, config.SmtOptions, attributePolicy);
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
                        var diagnostic = CreateInvalidConfigurationDiagnostic(invalidConfigurationValue);
                        if (!baseline.IsSuppressed(diagnostic))
                        {
                            endContext.ReportDiagnostic(diagnostic);
                        }
                    }

                    purityService.Dispose();
                });

                startContext.RegisterSyntaxNodeAction(c =>
                {
                    using (generatedPurityCatalog == null ? null : GeneratedPurityCatalog.UseCurrent(generatedPurityCatalog))
                    using (Engine.ImpurityCatalog.UseConfiguredOverrides(config))
                    {
                        MethodPurityAnalyzer.AnalyzeSymbolForPurity(c, purityService, missingPuritySuggestions, emitExplanations, reportBclFallbackGuesses, baseline, attributePolicy);
                        MethodAllocationAnalyzer.AnalyzeSymbolForZeroAllocations(c, baseline, attributePolicy);
                        MethodCapabilityAnalyzer.AnalyzeSymbolForCapabilities(c, baseline, attributePolicy);
                        MethodRequiresAnalyzer.AnalyzeSymbolForRequires(c, baseline, attributePolicy);
                        MethodEnsuresAnalyzer.AnalyzeSymbolForEnsures(c, purityService, baseline, attributePolicy);
                        MethodExpectedComplexityAnalyzer.AnalyzeSymbolForExpectedComplexity(c, baseline, attributePolicy);
                        ExceptionFlowAnalyzer.AnalyzeSymbolForExceptions(c, config, exceptionSummaryCatalog, purityService, baseline, attributePolicy);
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

                startContext.RegisterSyntaxNodeAction(
                    c => AttributePlacementAnalyzer.AnalyzeNonMethodDeclaration(c, baseline, attributePolicy),
                    SyntaxKind.AttributeList);
                startContext.RegisterSyntaxNodeAction(
                    c => MethodRequiresAnalyzer.AnalyzeCallSiteForRequires(c, purityService, baseline, attributePolicy),
                    SyntaxKind.InvocationExpression,
                    SyntaxKind.ObjectCreationExpression);
                startContext.RegisterSyntaxTreeAction(c => AnalyzeTreeConfiguration(c, baseline));
            });
        }

        private static void AnalyzeTreeConfiguration(
            SyntaxTreeAnalysisContext context,
            Configuration.DiagnosticBaseline baseline)
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
            var invalidConfigurationValues = Configuration.AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
                options,
                context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            var location = Location.Create(context.Tree, new TextSpan(0, 0));
            foreach (var invalidConfigurationValue in invalidConfigurationValues)
            {
                var diagnostic = CreateInvalidConfigurationDiagnostic(invalidConfigurationValue, location, context.Tree);
                if (!baseline.IsSuppressed(diagnostic))
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static Diagnostic CreateInvalidConfigurationDiagnostic(
            Configuration.InvalidAnalyzerConfigurationValue invalidConfigurationValue,
            Location? location = null,
            SyntaxTree? syntaxTree = null)
        {
            var path = syntaxTree?.FilePath ?? "<global>";
            var properties = Configuration.BaselineDiagnosticProperties.Add(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.ConfigurationKeyProperty, invalidConfigurationValue.Key)
                    .Add(SharpProofDiagnostics.ConfigurationValueProperty, invalidConfigurationValue.Value)
                    .Add(SharpProofDiagnostics.ConfigurationInvalidReasonProperty, invalidConfigurationValue.Reason),
                "<configuration>",
                path,
                "AnalyzerConfiguration",
                invalidConfigurationValue.Key,
                invalidConfigurationValue.Key + ":" + invalidConfigurationValue.Value + ":" + invalidConfigurationValue.Reason);
            properties = Configuration.ExplainDiagnosticProperties.Add(
                properties,
                location,
                invalidConfigurationValue.Key,
                "invalid",
                invalidConfigurationValue.Reason);

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
