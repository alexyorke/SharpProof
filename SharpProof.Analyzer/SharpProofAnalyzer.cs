using System;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SharpProofAnalyzer : DiagnosticAnalyzer
    {

        public const string SP0002 = SharpProofDiagnostics.PurityNotVerifiedId;
        public const string SP0004 = SharpProofDiagnostics.MissingEnforcePureAttributeId;

        private static readonly ImmutableArray<Type> _ruleTypes = ImmutableArray.Create<Type>();

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
                                  SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(startContext =>
            {
                var config = Configuration.AnalyzerConfiguration.FromOptions(startContext.Options);
                var purityService = new Engine.CompilationPurityService(startContext.Compilation, config.SmtOptions);
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

                startContext.RegisterCompilationEndAction(_ => purityService.Dispose());

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
                    SyntaxKind.RemoveAccessorDeclaration,
                    SyntaxKind.SetAccessorDeclaration,
                    SyntaxKind.ConstructorDeclaration,
                    SyntaxKind.ConversionOperatorDeclaration,
                    SyntaxKind.OperatorDeclaration,
                    SyntaxKind.LocalFunctionStatement);
            });

            context.RegisterSyntaxNodeAction(AttributePlacementAnalyzer.AnalyzeNonMethodDeclaration, SyntaxKind.AttributeList);
        }
    }
}
