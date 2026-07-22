namespace SharpProof.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SharpProofAnalyzer : DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        AnalyzerDiagnosticCatalog.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext => {
            SmtNativeLibraryBootstrap.TryLoadFromAnalyzerLocatorPaths(startContext.Options.AdditionalFiles.Select(static
                file => file.Path));
            var session = new AnalyzerSession(startContext.Compilation, startContext.Options, startContext.CancellationToken);

            startContext.RegisterCompilationEndAction(endContext => {
                try {
                    foreach (var invalidConfigurationValue in session.Configuration.InvalidConfigurationValues) {
                        var diagnostic = CreateInvalidConfigurationDiagnostic(invalidConfigurationValue);
                        endContext.ReportDiagnostic(diagnostic);
                    }
                }
                finally {
                    session.Dispose();
                }
            });

            startContext.RegisterOperationBlockAction(c => AnalyzerFeaturePipeline.AnalyzeOperationBlock(c, session));
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

            startContext.RegisterSyntaxTreeAction(AnalyzeTreeConfiguration);
        });
    }
    private static void AnalyzeTreeConfiguration(SyntaxTreeAnalysisContext context) {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
        var invalidConfigurationValues = AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
            options,
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        var location = Location.Create(context.Tree, new TextSpan(0, 0));
        foreach (var invalidConfigurationValue in invalidConfigurationValues) {
            var diagnostic = CreateInvalidConfigurationDiagnostic(invalidConfigurationValue, location);
            context.ReportDiagnostic(diagnostic);
        }
    }
    private static Diagnostic CreateInvalidConfigurationDiagnostic(
        InvalidAnalyzerConfigurationValue invalidConfigurationValue,
        Location? location = null) => Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("InvalidAnalyzerConfigurationRule"),
            location ?? Location.None,
            invalidConfigurationValue.Key,
            invalidConfigurationValue.Value,
            invalidConfigurationValue.Reason);
}
