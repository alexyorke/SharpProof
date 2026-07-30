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
        var activation = configuration.Profile == SharpProofProfile.Advisory
            ? GetAdvisoryActivation(
                context.Compilation,
                context.CancellationToken)
            : AdvisoryActivation.Full;
        if (!activation.Any ||
            !configuration.ContractsEnabled &&
            !activation.RequiresFullOperationAnalysis)
        {
            return;
        }

        var session = _sessionFactory.Create(
            context.Compilation,
            configuration,
            context.CancellationToken);
        if (activation.RequiresSymbolAnalysis)
        {
            context.RegisterSymbolAction(
                symbolContext => AnalyzerFeaturePipeline.ValidateMethodAttributes(
                    symbolContext,
                    session),
                SymbolKind.Method);
        }
        if (activation.RequiresOperationAnalysis)
        {
            if (activation.RequiresFullOperationAnalysis)
            {
                context.RegisterOperationBlockAction(operationContext =>
                    AnalyzerFeaturePipeline.AnalyzeOperationBlock(
                        operationContext,
                        session));
            }
            else
            {
                context.RegisterOperationBlockAction(operationContext =>
                    AnalyzerFeaturePipeline.AnalyzeUnselectedOperationBlock(
                        operationContext,
                        session));
            }
        }
    }

    private static AdvisoryActivation GetAdvisoryActivation(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var requiresOperationAnalysis = false;
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var node in tree.GetRoot(cancellationToken)
                         .DescendantNodes())
            {
                if (node is AttributeSyntax attribute &&
                    !IsAssemblyOrModuleAttribute(attribute))
                {
                    return AdvisoryActivation.Full;
                }

                if (node is InvocationExpressionSyntax invocation &&
                    IsContractApiCandidate(invocation.Expression))
                {
                    return new(
                        RequiresSymbolAnalysis: false,
                        RequiresOperationAnalysis: true,
                        RequiresFullOperationAnalysis: true);
                }

                if (IsPotentialOperation(node))
                {
                    requiresOperationAnalysis = true;
                }
            }
        }

        var hasSharpProofAssemblyAttribute =
            compilation.Assembly.GetAttributes().Any(
            static attribute =>
                IsSharpProofAttributesNamespace(
                    attribute.AttributeClass?.ContainingNamespace));
        return hasSharpProofAssemblyAttribute
            ? AdvisoryActivation.Full
            : new(
                RequiresSymbolAnalysis: false,
                RequiresOperationAnalysis: requiresOperationAnalysis,
                RequiresFullOperationAnalysis: false);
    }

    private static bool IsPotentialOperation(SyntaxNode node)
    {
        return node is ArgumentListSyntax or
            BaseObjectCreationExpressionSyntax or
            BaseListSyntax or
            ConstructorDeclarationSyntax or
            StatementSyntax or
            QueryExpressionSyntax or
            AwaitExpressionSyntax or
            InterpolatedStringExpressionSyntax or
            WithExpressionSyntax or
            CollectionExpressionSyntax or
            RecursivePatternSyntax;
    }

    private static bool IsContractApiCandidate(ExpressionSyntax expression)
    {
        SimpleNameSyntax? name = expression switch
        {
            SimpleNameSyntax simple => simple,
            MemberAccessExpressionSyntax member => member.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null
        };
        return name?.Identifier.ValueText is
            "Requires" or
            "Ensures" or
            "Assume" or
            "Old" or
            "Result";
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

    private readonly record struct AdvisoryActivation(
        bool RequiresSymbolAnalysis,
        bool RequiresOperationAnalysis,
        bool RequiresFullOperationAnalysis)
    {
        internal static AdvisoryActivation Full { get; } =
            new(
                RequiresSymbolAnalysis: true,
                RequiresOperationAnalysis: true,
                RequiresFullOperationAnalysis: true);

        internal bool Any =>
            RequiresSymbolAnalysis || RequiresOperationAnalysis;
    }
}
