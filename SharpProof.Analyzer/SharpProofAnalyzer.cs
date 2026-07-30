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

        if (ContractRuntimePolicy.IsRuntimeEvaluationEnabled(
                context.Compilation,
                context.CancellationToken))
        {
            context.RegisterCompilationEndAction(endContext =>
            {
                ReportInvalidConfiguration(endContext, configuration);
                endContext.ReportDiagnostic(CreateInvalidConfigurationDiagnostic(
                    ContractRuntimePolicy.InvalidConfiguration()));
            });
            return;
        }

        context.RegisterCompilationEndAction(endContext =>
        {
            ReportInvalidConfiguration(endContext, configuration);
            FinalCompilationCollector.Collect(endContext, configuration);
        });
        if (configuration.Profile == SharpProofProfile.Advisory &&
            !RequiresSemanticAnalysis(
                context.Compilation,
                context.CancellationToken))
        {
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
    }

    private static bool RequiresSemanticAnalysis(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var node in tree.GetRoot(cancellationToken)
                         .DescendantNodes())
            {
                if (node is ArgumentListSyntax or
                    BaseObjectCreationExpressionSyntax or
                    BaseListSyntax or
                    ConstructorDeclarationSyntax or
                    StatementSyntax or
                    QueryExpressionSyntax or
                    AwaitExpressionSyntax or
                    InterpolatedStringExpressionSyntax or
                    WithExpressionSyntax or
                    CollectionExpressionSyntax or
                    RecursivePatternSyntax)
                {
                    return true;
                }

                if (node is AttributeSyntax attribute &&
                    !IsAssemblyOrModuleAttribute(attribute))
                {
                    return true;
                }
            }
        }

        return compilation.Assembly.GetAttributes().Any(
            static attribute =>
                IsSharpProofAttributesNamespace(
                    attribute.AttributeClass?.ContainingNamespace));
    }

    private static bool IsSharpProofAttributesNamespace(
        INamespaceSymbol? @namespace)
    {
        return @namespace is
        {
            Name: "Attributes",
            ContainingNamespace:
            {
                Name: "SharpProof",
                ContainingNamespace:
                {
                    IsGlobalNamespace: true
                }
            }
        };
    }

    private static bool IsAssemblyOrModuleAttribute(
        AttributeSyntax attribute)
    {
        var target = (attribute.Parent as AttributeListSyntax)?
            .Target?.Identifier.Kind() ?? SyntaxKind.None;
        return target is
            SyntaxKind.AssemblyKeyword or
            SyntaxKind.ModuleKeyword;
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
