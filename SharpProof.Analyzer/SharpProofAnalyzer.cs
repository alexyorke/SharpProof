using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SharpProofAnalyzer : DiagnosticAnalyzer
{
    public const string SP0002 = SharpProofDiagnostics.PurityNotVerifiedId;
    public const string SP0004 = SharpProofDiagnostics.MissingEnforcePureAttributeId;

    public SharpProofAnalyzer()
        : this(AnalyzerFeatures.All)
    {
    }

    internal SharpProofAnalyzer(AnalyzerFeatures features)
    {
        Features = AnalyzerFeatureDependencies.Expand(features);
    }

    internal AnalyzerFeatures Features { get; }

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
            SharpProofDiagnostics.UnknownRuntimeHazardRule,
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
            SharpProofDiagnostics.InvalidAdditionalFileRule,
            SharpProofDiagnostics.UnrecognizedAttributeIdentityRule,
            SharpProofDiagnostics.RequiresNotProvenRule,
            SharpProofDiagnostics.RequiresUnsupportedRule,
            SharpProofDiagnostics.MisplacedRequiresAttributeRule,
            SharpProofDiagnostics.ExceptionContractViolationRule,
            SharpProofDiagnostics.MisplacedExceptionContractAttributeRule,
            SharpProofDiagnostics.SuggestZeroAllocationsRule,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesRule,
            SharpProofDiagnostics.SuggestExpectedComplexityRule,
            SharpProofDiagnostics.SuggestExceptionContractRule,
            SharpProofDiagnostics.SuggestEnsuresRule,
            SharpProofDiagnostics.SuggestRequiresRule,
            SharpProofDiagnostics.TrustedBoundaryReviewRule,
            SharpProofDiagnostics.NullableReturnContractViolationRule,
            SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
            SharpProofDiagnostics.NullableMemberContractViolationRule,
            SharpProofDiagnostics.UnsafeNullForgivingOperatorRule,
            SharpProofDiagnostics.UnnecessaryNullForgivingOperatorRule,
            SharpProofDiagnostics.SuggestNullableContractRule,
            SharpProofDiagnostics.NullableVerificationInconclusiveRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            SmtNativeLibraryBootstrap.TryLoadFromAnalyzerLocatorPaths(
                startContext.Options.AdditionalFiles.Select(static file => file.Path));
            var additionalFileIssues = AnalyzerAdditionalFileValidator.Validate(
                startContext.Options,
                startContext.CancellationToken);
            var session = new AnalyzerSession(
                startContext.Compilation,
                startContext.Options,
                startContext.CancellationToken,
                Features);

            startContext.RegisterCompilationEndAction(endContext =>
            {
                try
                {
                    foreach (var invalidConfigurationValue in session.Configuration.InvalidConfigurationValues)
                    {
                        var diagnostic = CreateInvalidConfigurationDiagnostic(invalidConfigurationValue);
                        if (!session.Baseline.IsSuppressed(diagnostic)) endContext.ReportDiagnostic(diagnostic);
                    }

                    foreach (var additionalFileIssue in additionalFileIssues)
                        endContext.ReportDiagnostic(CreateInvalidAdditionalFileDiagnostic(additionalFileIssue));

                    foreach (var compatibilityIssue in session.EffectSummaryCompatibilityReporter.GetIssues())
                        endContext.ReportDiagnostic(CreateInvalidAdditionalFileDiagnostic(compatibilityIssue));

                    TrustedBoundaryReviewAnalyzer.ReportDiagnostics(endContext, session);
                }
                finally
                {
                    session.Dispose();
                }
            });

            if ((session.Features & AnalyzerFeatures.Callable) != 0)
            {
                startContext.RegisterOperationBlockAction(
                    c => AnalyzerFeaturePipeline.AnalyzeOperationBlock(c, session));
                startContext.RegisterSyntaxNodeAction(
                    c => AnalyzerFeaturePipeline.AnalyzeSyntaxFallback(c, session),
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
            }

            if (session.Features.Includes(AnalyzerFeatures.Placement))
                startContext.RegisterSyntaxNodeAction(
                    c => AttributePlacementAnalyzer.AnalyzeNonMethodDeclaration(
                        c,
                        session.Baseline,
                        session.AttributePolicy),
                    SyntaxKind.AttributeList);

            if (session.Features.Includes(AnalyzerFeatures.Requires))
                startContext.RegisterOperationAction(
                    c => MethodRequiresAnalyzer.AnalyzeCallSiteForRequires(
                        c,
                        session.PurityService,
                        session.Baseline,
                        session.AttributePolicy),
                    OperationKind.Invocation,
                    OperationKind.ObjectCreation,
                    OperationKind.PropertyReference,
                    OperationKind.SimpleAssignment,
                    OperationKind.CompoundAssignment,
                    OperationKind.Increment,
                    OperationKind.Decrement,
                    OperationKind.Binary,
                    OperationKind.Unary,
                    OperationKind.Conversion);

            startContext.RegisterSyntaxTreeAction(c => AnalyzeTreeConfiguration(c, session.Baseline));
        });
    }

    private static void AnalyzeTreeConfiguration(
        SyntaxTreeAnalysisContext context,
        DiagnosticBaseline baseline)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
        var invalidConfigurationValues = AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
            options,
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        var location = Location.Create(context.Tree, new TextSpan(0, 0));
        foreach (var invalidConfigurationValue in invalidConfigurationValues)
        {
            var diagnostic = CreateInvalidConfigurationDiagnostic(invalidConfigurationValue, location, context.Tree);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }

    private static Diagnostic CreateInvalidConfigurationDiagnostic(
        InvalidAnalyzerConfigurationValue invalidConfigurationValue,
        Location? location = null,
        SyntaxTree? syntaxTree = null)
    {
        var path = syntaxTree?.FilePath ?? "<global>";
        var properties = BaselineDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ConfigurationKeyProperty, invalidConfigurationValue.Key)
                .Add(SharpProofDiagnostics.ConfigurationValueProperty, invalidConfigurationValue.Value)
                .Add(SharpProofDiagnostics.ConfigurationInvalidReasonProperty, invalidConfigurationValue.Reason),
            "<configuration>",
            path,
            "AnalyzerConfiguration",
            invalidConfigurationValue.Key,
            invalidConfigurationValue.Key + ":" + invalidConfigurationValue.Value + ":" +
            invalidConfigurationValue.Reason);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            invalidConfigurationValue.Key,
            "invalid",
            invalidConfigurationValue.Reason);

        return Diagnostic.Create(
            SharpProofDiagnostics.InvalidAnalyzerConfigurationRule,
            location ?? Location.None,
            null,
            properties,
            new object[]
            {
                invalidConfigurationValue.Key,
                invalidConfigurationValue.Value,
                invalidConfigurationValue.Reason
            });
    }

    private static Diagnostic CreateInvalidAdditionalFileDiagnostic(
        AnalyzerAdditionalFileIssue issue)
    {
        var path = string.IsNullOrWhiteSpace(issue.Path) ? "<unknown>" : issue.Path;
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.AdditionalFilePathProperty, path)
            .Add(SharpProofDiagnostics.AdditionalFileReasonProperty, issue.Reason)
            .Add(SharpProofDiagnostics.AdditionalFileReasonCodeProperty, issue.ReasonCode);

        return Diagnostic.Create(
            SharpProofDiagnostics.InvalidAdditionalFileRule,
            Location.None,
            null,
            properties,
            new object[] { path, issue.Reason });
    }
}
