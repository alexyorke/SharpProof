namespace SharpProof.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharpProofAnalyzer : DiagnosticAnalyzer
{
    private readonly IAnalyzerSessionFactory _sessionFactory;

    public SharpProofAnalyzer()
        : this(DefaultAnalyzerSessionFactory.Instance)
    {
    }

    internal SharpProofAnalyzer(IAnalyzerSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ??
            throw new ArgumentNullException(nameof(sessionFactory));
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        GeneratedDiagnosticDescriptors.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var configuration = AnalyzerConfiguration.FromOptions(context.Options);
        context.RegisterSyntaxTreeAction(AnalyzeTreeConfiguration);
        if (configuration.Profile == SharpProofProfile.Off)
        {
            context.RegisterCompilationEndAction(endContext =>
                ReportInvalidConfiguration(endContext, configuration));
            return;
        }

        var session = _sessionFactory.Create(
            context.Compilation,
            configuration,
            context.CancellationToken);
        context.RegisterSymbolAction(
            symbolContext => AnalyzerFeaturePipeline.ValidateMethodAttributes(symbolContext, session),
            SymbolKind.Method);
        context.RegisterOperationBlockAction(operationContext =>
            AnalyzerFeaturePipeline.AnalyzeOperationBlock(operationContext, session));
        context.RegisterCompilationEndAction(endContext =>
        {
            ReportInvalidConfiguration(endContext, configuration);
            FinalCompilationCollector.Collect(endContext, configuration);
        });
    }

    private static void ReportInvalidConfiguration(
        CompilationAnalysisContext context,
        AnalyzerConfiguration configuration)
    {
        foreach (var invalidValue in configuration.InvalidConfigurationValues)
        {
            context.ReportDiagnostic(CreateInvalidConfigurationDiagnostic(invalidValue));
        }
    }

    private static void AnalyzeTreeConfiguration(SyntaxTreeAnalysisContext context)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
        var invalidValues = AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
            options,
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        var location = Location.Create(context.Tree, new TextSpan(0, 0));
        foreach (var invalidValue in invalidValues)
        {
            context.ReportDiagnostic(
                CreateInvalidConfigurationDiagnostic(invalidValue, location));
        }
    }

    private static Diagnostic CreateInvalidConfigurationDiagnostic(
        InvalidAnalyzerConfigurationValue invalidValue,
        Location? location = null)
    {
        return Diagnostic.Create(
            GeneratedDiagnosticDescriptors.InvalidAnalyzerConfigurationRule,
            location ?? Location.None,
            invalidValue.Key,
            invalidValue.Value,
            invalidValue.Reason);
    }
}
