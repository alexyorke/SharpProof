using System.Reflection.Metadata;

namespace SharpProof.Analyzer;

internal sealed partial class SharpProofAnalyzerEngine
{
    private const int ActivationCancellationCheckInterval = 256;
    private readonly IAnalyzerSessionFactory _sessionFactory;

    internal SharpProofAnalyzerEngine()
        : this(DefaultAnalyzerSessionFactory.Instance)
    {
    }

    internal SharpProofAnalyzerEngine(IAnalyzerSessionFactory sessionFactory)
    {
        _sessionFactory = ArgumentNullGuard.NotNull(
            sessionFactory, nameof(sessionFactory));
    }

    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [
            .. GeneratedDiagnosticDescriptors.SupportedDiagnostics,
            .. ContractForDiagnosticDescriptors.SupportedDiagnostics
        ];

    internal void RegisterActions(AnalysisContext context)
    {
        context = ArgumentNullGuard.NotNull(context, nameof(context));

        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var configuration = CompilerConfiguration.FromOptions(context.Options);
        var configurationDiagnostics = GetConfigurationDiagnostics(
            context.Compilation,
            context.Options,
            configuration,
            context.CancellationToken);
        if (configuration.Profile == SharpProofProfile.Off)
        {
            if (!configurationDiagnostics.IsEmpty)
            {
                context.RegisterCompilationEndAction(
                    CreateConfigurationReporter(
                        configurationDiagnostics));
            }

            return;
        }

        // Reconcile companions even when another configuration diagnostic
        // causes ordinary analysis to stop.  Configuration errors must not
        // hide independent malformed companion declarations.
        context.RegisterCompilationEndAction(
            ValidateContractForCompanions);

        // A contract API that is referenced but unreadable disables every
        // contract silently, which is indistinguishable from "nothing to
        // report". Surface it instead.
        if (SharpProof.Frontend.ContractApiIdentityResolver
                .ForCompilation(context.Compilation)
                .UnreadableContractApiReason is { } unreadableContractApi)
        {
            context.RegisterCompilationEndAction(
                compilationContext =>
                {
                    compilationContext = RepositoryParameterGuard.ValidateInitialized(compilationContext, compilationContext.Compilation is null, nameof(compilationContext), "Value must be initialized.");
                    compilationContext = RepositoryParameterGuard.ValidateInitialized(compilationContext, (compilationContext.Compilation is null), nameof(compilationContext), "Value must be initialized.");
                    compilationContext.ReportDiagnostic(
                                       Diagnostic.Create(
                                           GeneratedDiagnosticDescriptors.ContractApiUnverifiableRule,
                                           Location.None,
                                           unreadableContractApi));
                });
        }

        if (ContractRuntimePolicy.IsReservedSymbolDefined(
                context.Compilation,
                context.CancellationToken))
        {
            context.RegisterCompilationEndAction(
                CreateConfigurationReporter(
                    configurationDiagnostics.Add(
                        CreateInvalidConfigurationDiagnostic(
                            ContractRuntimePolicy.InvalidConfiguration()))));
            return;
        }

        if (!configurationDiagnostics.IsEmpty)
        {
            context.RegisterCompilationEndAction(
                CreateConfigurationReporter(
                    configurationDiagnostics));
            return;
        }

        ResourceBoundsDiagnostics.Register(context);

        var activation = configuration.Profile == SharpProofProfile.Advisory
            ? GetAdvisoryActivation(
                context.Compilation,
                context.CancellationToken)
            : AdvisoryActivation.Full;
        var analysisEnabled = activation.Any &
            (configuration.ContractsEnabled |
             activation.RequiresFullOperationAnalysis);
        if (!analysisEnabled)
        {
            return;
        }

        var session = _sessionFactory.Create(
            context.Compilation,
            configuration,
            context.CancellationToken);
        if (activation.RequiresSymbolAnalysis)
        {
            context.RegisterSyntaxNodeAction(
                syntaxContext =>
                    AnalyzerFeaturePipeline.ValidateNestedCallableDeclaration(
                        syntaxContext,
                        session),
                SyntaxKind.LocalFunctionStatement,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression);
            context.RegisterSymbolAction(
                symbolContext => AnalyzerFeaturePipeline.ValidateMethodAttributes(
                    symbolContext,
                    session),
                SymbolKind.Method);
            context.RegisterSymbolAction(
                symbolContext =>
                    SharpProofControlAttributePolicy.ValidateDeclaredScope(
                        symbolContext.Symbol,
                        session,
                        symbolContext.ReportDiagnostic,
                        symbolContext.CancellationToken),
                SymbolKind.NamedType);
            context.RegisterCompilationEndAction(
                compilationContext =>
                    SharpProofControlAttributePolicy.ValidateDeclaredScope(
                        compilationContext.Compilation.Assembly,
                        session,
                        compilationContext.ReportDiagnostic,
                        compilationContext.CancellationToken));
            context.RegisterCompilationEndAction(
                compilationContext =>
                    {
                        compilationContext = RepositoryParameterGuard.ValidateInitialized(compilationContext, compilationContext.Compilation is null, nameof(compilationContext), "Value must be initialized.");
                        compilationContext = RepositoryParameterGuard.ValidateInitialized(compilationContext, (compilationContext.Compilation is null), nameof(compilationContext), "Value must be initialized.");
                        AnalyzerFeaturePipeline.ReconcileSelectedSemicolonAccessors(
                                               compilationContext,
                                               session);
                    });
        }
        if (activation.RequiresOperationAnalysis)
        {
            if (configuration.ContractsEnabled || activation.RequiresFullOperationAnalysis)
            {
                context.RegisterSyntaxNodeAction(
                    syntaxContext =>
                        {
                            syntaxContext = RepositoryParameterGuard.ValidateInitialized(syntaxContext, syntaxContext.Node is null, nameof(syntaxContext), "Value must be initialized.");
                            syntaxContext = RepositoryParameterGuard.ValidateInitialized(syntaxContext, (syntaxContext.Node is null), nameof(syntaxContext), "Value must be initialized.");
                            AnalyzerFeaturePipeline.AnalyzePrimaryConstructor(
                                                       syntaxContext,
                                                       session);
                        },
                    SyntaxKind.ClassDeclaration,
                    SyntaxKind.StructDeclaration,
                    SyntaxKind.RecordDeclaration,
                    SyntaxKind.RecordStructDeclaration);
                context.RegisterSyntaxNodeAction(
                    syntaxContext =>
                        {
                            syntaxContext = RepositoryParameterGuard.ValidateInitialized(syntaxContext, syntaxContext.Node is null, nameof(syntaxContext), "Value must be initialized.");
                            syntaxContext = RepositoryParameterGuard.ValidateInitialized(syntaxContext, (syntaxContext.Node is null), nameof(syntaxContext), "Value must be initialized.");
                            AnalyzerFeaturePipeline.AnalyzeMemberInitializer(
                                                       syntaxContext,
                                                       session);
                        },
                    SyntaxKind.EqualsValueClause);
            }
            if (activation.RequiresFullOperationAnalysis)
            {
                context.RegisterSyntaxNodeAction(
                    syntaxContext =>
                        {
                            syntaxContext = RepositoryParameterGuard.ValidateInitialized(syntaxContext, syntaxContext.Node is null, nameof(syntaxContext), "Value must be initialized.");
                            syntaxContext = RepositoryParameterGuard.ValidateInitialized(syntaxContext, (syntaxContext.Node is null), nameof(syntaxContext), "Value must be initialized.");
                            AnalyzerFeaturePipeline.AnalyzeLambdaEffects(
                                                       syntaxContext,
                                                       session);
                        },
                    SyntaxKind.SimpleLambdaExpression,
                    SyntaxKind.ParenthesizedLambdaExpression,
                    SyntaxKind.AnonymousMethodExpression,
                    SyntaxKind.LocalFunctionStatement);
                context.RegisterOperationBlockAction(operationContext =>
                    {
                        RepositoryParameterGuard.RequireInitialized((operationContext.Equals(default(OperationBlockAnalysisContext))), nameof(operationContext), "Value must be initialized.");
                        AnalyzerFeaturePipeline.AnalyzeOperationBlock(
                        operationContext,
                        session);
                    });
            }
            else
            {
                context.RegisterOperationBlockAction(operationContext =>
                    {
                        RepositoryParameterGuard.RequireInitialized((operationContext.Equals(default(OperationBlockAnalysisContext))), nameof(operationContext), "Value must be initialized.");
                        AnalyzerFeaturePipeline.AnalyzeUnselectedOperationBlock(
                        operationContext,
                        session);
                    });
            }
        }
    }

    private static void ValidateContractForCompanions(
        CompilationAnalysisContext context)
    {
        var candidates = ContractForValidationEngine.FindCandidates(
            context.Compilation,
            static _ => true,
            context.CancellationToken);
        foreach (var diagnostic in ContractForValidationEngine.Validate(
                     context.Compilation,
                     candidates,
                     context.CancellationToken))
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static AdvisoryActivation GetAdvisoryActivation(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var hasContractApiCandidate = false;
        foreach (var tree in PotentiallyActivatedTrees(
                     compilation,
                     cancellationToken))
        {
            SemanticModel? semanticModel = null;
            foreach (var node in tree.GetRoot(cancellationToken)
                         .DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is AttributeSyntax attribute &&
                    !IsAssemblyOrModuleAttribute(attribute))
                {
                    if (IsSharpProofAttributeCandidate(attribute))
                    {
                        return AdvisoryActivation.Full;
                    }

                    if (attribute.Name is IdentifierNameSyntax name)
                    {
                        semanticModel ??=
                            SharpProof.Frontend.Host.CompilationModelProvider
                                .GetSemanticModel(compilation, tree);
                        if (IsSharpProofAttributeAlias(
                                semanticModel,
                                name,
                                cancellationToken))
                        {
                            return AdvisoryActivation.Full;
                        }
                    }
                }

                if (node is ExpressionSyntax expression &&
                    IsContractApiCandidate(expression))
                {
                    hasContractApiCandidate = true;
                }
            }
        }

        if (hasContractApiCandidate)
        {
            return new(
                RequiresSymbolAnalysis: false,
                RequiresOperationAnalysis: true,
                RequiresFullOperationAnalysis: true);
        }

        var hasSharpProofAssemblyAttribute =
            compilation.Assembly.GetAttributes().Any(
            static attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (IsSharpProofAttributesNamespace(
                    attribute.AttributeClass?.ContainingNamespace)));
        return hasSharpProofAssemblyAttribute
            ? AdvisoryActivation.Full
            : MayContainExternalClosedPreconditions(
                    compilation,
                    cancellationToken)
                ? AdvisoryActivation.Lightweight
                : AdvisoryActivation.None;
    }

    private static IEnumerable<SyntaxTree> PotentiallyActivatedTrees(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MayContainAdvisoryActivationSyntax(
                    tree.GetText(cancellationToken),
                    cancellationToken))
            {
                yield return tree;
            }
        }
    }

    private static bool MayContainAdvisoryActivationSyntax(
        SourceText text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (var index = 0; index < text.Length; index++)
        {
            if (index % ActivationCancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var character = text[index];
            if (character == '[' ||
                (character == '\\' &&
                 index + 1 < text.Length &&
                 text[index + 1] is 'u' or 'U'))
            {
                // Unicode escapes can spell any identifier. The syntax scan
                // compares decoded Identifier.ValueText before activation.
                return true;
            }
            foreach (var candidate in
                     ContractApiMetadata.ContractMethodCandidateNames)
            {
                if (candidate.Length == 0 ||
                    candidate[0] != character ||
                    candidate.Length > text.Length - index)
                {
                    continue;
                }

                var matches = true;
                for (var offset = 1; offset < candidate.Length; offset++)
                {
                    if (text[index + offset] == candidate[offset])
                    {
                        continue;
                    }

                    matches = false;
                    break;
                }

                if (matches)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MayContainExternalClosedPreconditions(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName(
                ContractApiMetadata.Contract) == null)
        {
            return false;
        }

        var closedContractAttributes = GetClosedContractAttributes(compilation);

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference is PortableExecutableReference portable)
            {
                if (PortableReferenceContainsClosedPrecondition(portable))
                {
                    return true;
                }

                continue;
            }

            if (reference is CompilationReference)
            {
                // Contract.Requires is Conditional and is omitted from a
                // compiled reference. Treat source-only clauses the same as
                // the emitted assembly so IDE and build diagnostics agree.
                var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
                if (symbol == null)
                {
                    return true;
                }

                if (symbol is IAssemblySymbol assembly &&
                    NamespaceContainsClosedPrecondition(
                        assembly.GlobalNamespace,
                        closedContractAttributes,
                        cancellationToken))
                {
                    return true;
                }

                continue;
            }

        }

        return false;
    }

    private static ImmutableArray<INamedTypeSymbol>
        GetClosedContractAttributes(
        Compilation compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        return ImmutableArray.Create(
                compilation.GetTypeByMetadataName(
                    ContractApiMetadata.NotNull),
                compilation.GetTypeByMetadataName(
                    ContractApiMetadata.Positive),
                compilation.GetTypeByMetadataName(
                    ContractApiMetadata.InRange))
            .Where(static symbol => symbol != null)
            .Select(static symbol => symbol!)
            .ToImmutableArray();
    }

    internal static bool PortableReferenceContainsClosedPrecondition(
        PortableExecutableReference reference)
    {
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
        try
        {
            var metadata = reference.GetMetadata();
            // The pinned compiler returns only sealed AssemblyMetadata or ModuleMetadata copies.
            return metadata is AssemblyMetadata assembly
                ? assembly.GetModules().Any(static module =>
                    module is null ? throw new System.ArgumentNullException(nameof(module)) : (ModuleContainsClosedPrecondition(module.GetMetadataReader())))
                : ModuleContainsClosedPrecondition(((ModuleMetadata)metadata).GetMetadataReader());
        }
        catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or
            IOException or InvalidOperationException)
        {
            return true;
        }
    }

    private static bool ModuleContainsClosedPrecondition(
        MetadataReader reader)
    {
        foreach (var handle in reader.CustomAttributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Parent.Kind == HandleKind.Parameter &&
                IsClosedContractAttribute(reader, attribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClosedContractAttribute(
        MetadataReader reader,
        CustomAttribute attribute)
    {
        reader = RepositoryParameterGuard.NotNull(reader, nameof(reader));
        return ApiSpecResolver.TryGetAttributeTypeName(
                reader,
                attribute,
                out var namespaceHandle,
                out var nameHandle) &&
            ContractApiMetadata.IsClosedAttributeTypeName(
                reader.GetString(namespaceHandle),
                reader.GetString(nameHandle));
    }

    private static bool NamespaceContainsClosedPrecondition(
        INamespaceSymbol @namespace,
        ImmutableArray<INamedTypeSymbol> closedContractAttributes,
        CancellationToken cancellationToken)
    {
        @namespace = RepositoryParameterGuard.NotNull(@namespace, nameof(@namespace));
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var type in @namespace.GetTypeMembers())
        {
            if (TypeContainsClosedPrecondition(
                    type,
                    closedContractAttributes,
                    cancellationToken))
            {
                return true;
            }
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            if (NamespaceContainsClosedPrecondition(
                    child,
                    closedContractAttributes,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeContainsClosedPrecondition(
        INamedTypeSymbol type,
        ImmutableArray<INamedTypeSymbol> closedContractAttributes,
        CancellationToken cancellationToken)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (ContainsClosedContractAttribute(
                    method.GetReturnTypeAttributes(),
                    closedContractAttributes) ||
                method.Parameters.Any(parameter =>
                    parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (ContainsClosedContractAttribute(
                        parameter.GetAttributes(),
                        closedContractAttributes))))
            {
                return true;
            }
        }

        return type.GetTypeMembers().Any(nested =>
            nested is null ? throw new System.ArgumentNullException(nameof(nested)) : (TypeContainsClosedPrecondition(
                nested,
                closedContractAttributes,
                cancellationToken)));
    }

    private static bool ContainsClosedContractAttribute(
        ImmutableArray<AttributeData> attributes,
        ImmutableArray<INamedTypeSymbol> closedContractAttributes)
    {
        return attributes.Any(attribute =>
            attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (closedContractAttributes.Any(expected =>
                expected is null ? throw new System.ArgumentNullException(nameof(expected)) : (SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass?.OriginalDefinition,
                    expected.OriginalDefinition)))));
    }

    private static bool IsContractApiCandidate(ExpressionSyntax expression)
    {
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        SimpleNameSyntax? name = expression switch
        {
            SimpleNameSyntax simple => simple,
            MemberAccessExpressionSyntax member => member.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null
        };
        return name != null &&
            ContractApiMetadata.IsContractMethodCandidateName(
                name.Identifier.ValueText);
    }

    // Decomposed deliberately: ToDisplayString and other string-based symbol
    // identity are banned in this layer (RS0030), so the namespace
    // is matched structurally rather than compared against
    // ContractApiMetadata.AttributesNamespace as a string.
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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        var target = (attribute.Parent as AttributeListSyntax)?
            .Target?.Identifier.Kind() ?? SyntaxKind.None;
        return target is
            SyntaxKind.AssemblyKeyword or
            SyntaxKind.ModuleKeyword;
    }

    private static bool IsSharpProofAttributeCandidate(
        AttributeSyntax attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        var simpleName = attribute.Name.GetLastToken().ValueText;
        return ContractApiMetadata.Attributes.Any(descriptor =>
            string.Equals(
                simpleName,
                descriptor.TypeName,
                StringComparison.Ordinal) ||
            descriptor.TypeName.EndsWith(
                "Attribute",
                StringComparison.Ordinal) &&
            string.Equals(
                simpleName,
                descriptor.TypeName.Substring(
                    0,
                    descriptor.TypeName.Length - "Attribute".Length),
                StringComparison.Ordinal));
    }

    private static bool IsSharpProofAttributeAlias(
        SemanticModel semanticModel,
        IdentifierNameSyntax name,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetAliasInfo(name, cancellationToken)?.Target is
            not INamedTypeSymbol attributeType ||
            !IsSharpProofAttributesNamespace(
                attributeType.ContainingNamespace))
        {
            return false;
        }

        return ContractApiMetadata.Attributes.Any(descriptor =>
            string.Equals(
                attributeType.MetadataName,
                descriptor.TypeName,
                StringComparison.Ordinal));
    }

    internal static ImmutableArray<Diagnostic> GetConfigurationDiagnostics(
        Compilation compilation,
        AnalyzerOptions analyzerOptions,
        CompilerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        analyzerOptions = RepositoryParameterGuard.NotNull(analyzerOptions, nameof(analyzerOptions));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        configuration = RepositoryParameterGuard.NotNull(configuration, nameof(configuration));
        return [.. CompilerConfigurationValidation.GetIssues(compilation, analyzerOptions, configuration, cancellationToken)
            .Select(static issue => issue is null ? throw new ArgumentNullException(nameof(issue)) : CreateInvalidConfigurationDiagnostic(issue.Value, issue.Location))];
    }

    private static Action<CompilationAnalysisContext>
        CreateConfigurationReporter(
        ImmutableArray<Diagnostic> diagnostics)
    {

        RepositoryParameterGuard.ThrowIfInvalid(diagnostics.IsDefault, nameof(diagnostics));
        return context =>
        {
            context = RepositoryParameterGuard.ValidateInitialized(context, context.Compilation is null, nameof(context), "Value must be initialized.");

            foreach (var diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        };
    }

    private static Diagnostic CreateInvalidConfigurationDiagnostic(
        InvalidCompilerConfigurationValue invalidValue,
        Location? location = null)
    {
        invalidValue = RepositoryParameterGuard.ValidateInitialized(invalidValue, invalidValue.Key is null || invalidValue.Value is null || invalidValue.Reason is null, nameof(invalidValue), "Value must be initialized.");

        return Diagnostic.Create(
            GeneratedDiagnosticDescriptors.InvalidAnalyzerConfigurationRule,
            location ?? Location.None,
            invalidValue.Key,
            invalidValue.Value,
            invalidValue.Reason);
    }

    private readonly partial record struct AdvisoryActivation
    {
        internal static AdvisoryActivation Full { get; } =
            new(
                RequiresSymbolAnalysis: true,
                RequiresOperationAnalysis: true,
                RequiresFullOperationAnalysis: true);
        internal static AdvisoryActivation Lightweight { get; } =
            new(
                RequiresSymbolAnalysis: false,
                RequiresOperationAnalysis: true,
                RequiresFullOperationAnalysis: false);
        internal static AdvisoryActivation None => default;

        internal bool Any =>
            RequiresSymbolAnalysis || RequiresOperationAnalysis;
    }
}
