using System;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace PurelySharp.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PurelySharpAnalyzer : DiagnosticAnalyzer
    {

        public const string PS0002 = PurelySharpDiagnostics.PurityNotVerifiedId;
        public const string PS0004 = PurelySharpDiagnostics.MissingEnforcePureAttributeId;

        private static readonly ImmutableArray<Type> _ruleTypes = ImmutableArray.Create<Type>();

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(PurelySharpDiagnostics.PurityNotVerifiedRule,
                                  PurelySharpDiagnostics.MisplacedAttributeRule,
                                  PurelySharpDiagnostics.MissingEnforcePureAttributeRule,
                                  PurelySharpDiagnostics.ConflictingPurityAttributesRule,
                                  PurelySharpDiagnostics.AllowSynchronizationWithoutPurityAttributeRule,
                                  PurelySharpDiagnostics.MisplacedAllowSynchronizationAttributeRule,
                                  PurelySharpDiagnostics.RedundantAllowSynchronizationRule,
                                  PurelySharpDiagnostics.PurityExplanationRule,
                                  PurelySharpDiagnostics.ExceptionSummaryRule,
                                  PurelySharpDiagnostics.UncaughtExceptionSiteRule);

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
                var baseline = Configuration.DiagnosticBaseline.FromOptions(startContext.Options, startContext.CancellationToken);
                var needsExceptionSummaryCatalog = config.EnableEffectSummaryJson &&
                    (config.ReportExceptions || config.CheckedExceptions);
                var exceptionSummaryCatalog = needsExceptionSummaryCatalog
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
                        MethodPurityAnalyzer.AnalyzeSymbolForPurity(c, purityService, missingPuritySuggestions, emitExplanations, baseline);
                        ExceptionFlowAnalyzer.AnalyzeSymbolForExceptions(c, config.ReportExceptions, config.CheckedExceptions, exceptionSummaryCatalog, purityService);
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
