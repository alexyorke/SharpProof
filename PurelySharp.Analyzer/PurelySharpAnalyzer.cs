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
                var purityService = new Engine.CompilationPurityService(startContext.Compilation);
                var config = Configuration.AnalyzerConfiguration.FromOptions(startContext.Options);
                var missingPuritySuggestions = config.MissingPuritySuggestions;
                var emitExplanations = config.EmitExplanations;
                var baseline = Configuration.DiagnosticBaseline.FromOptions(startContext.Options, startContext.CancellationToken);
                var exceptionSummaryCatalog = ExceptionSummaryCatalog.FromOptions(startContext.Options, startContext.CancellationToken);
                var generatedPurityCatalog = GeneratedPurityCatalog.FromOptions(startContext.Options, startContext.CancellationToken);

                startContext.RegisterSyntaxNodeAction(c =>
                {
                    using (GeneratedPurityCatalog.UseCurrent(generatedPurityCatalog))
                    using (Engine.ImpurityCatalog.UseConfiguredOverrides(config))
                    {
                        MethodPurityAnalyzer.AnalyzeSymbolForPurity(c, purityService, missingPuritySuggestions, emitExplanations, baseline);
                        ExceptionFlowAnalyzer.AnalyzeSymbolForExceptions(c, config.ReportExceptions, exceptionSummaryCatalog);
                    }
                },
                    SyntaxKind.MethodDeclaration,
                    SyntaxKind.GetAccessorDeclaration,
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
