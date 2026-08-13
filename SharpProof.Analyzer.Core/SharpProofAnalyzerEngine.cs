using System.Reflection.Metadata;

namespace SharpProof.Analyzer;

internal sealed partial class SharpProofAnalyzerEngine
{
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

    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
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
        var configuration = AnalyzerConfiguration.FromOptions(context.Options);
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

        // A contract API that is referenced but unreadable disables every
        // contract silently, which is indistinguishable from "nothing to
        // report". Surface it instead.
        if (SharpProof.Frontend.ContractApiIdentityResolver
                .ForCompilation(context.Compilation)
                .UnreadableContractApiReason is { } unreadableContractApi)
        {
            context.RegisterCompilationEndAction(
                compilationContext => compilationContext.ReportDiagnostic(
                    Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.ContractApiUnverifiableRule,
                        Location.None,
                        unreadableContractApi)));
        }

        if (ContractRuntimePolicy.IsRuntimeEvaluationEnabled(
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
        if (configuration.ContractsEnabled)
        {
            context.RegisterCompilationEndAction(
                ValidateGeneratedContractForCompanions);
        }
        if (activation.RequiresSymbolAnalysis)
        {
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
        }
        if (activation.RequiresOperationAnalysis)
        {
            if (configuration.ContractsEnabled)
            {
                context.RegisterSyntaxNodeAction(
                    syntaxContext =>
                        AnalyzerFeaturePipeline.AnalyzePrimaryConstructor(
                            syntaxContext,
                            session),
                    SyntaxKind.ClassDeclaration,
                    SyntaxKind.StructDeclaration,
                    SyntaxKind.RecordDeclaration,
                    SyntaxKind.RecordStructDeclaration);
                context.RegisterSyntaxNodeAction(
                    syntaxContext =>
                        AnalyzerFeaturePipeline.AnalyzeMemberInitializer(
                            syntaxContext,
                            session),
                    SyntaxKind.EqualsValueClause);
            }
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

    private static void ValidateGeneratedContractForCompanions(
        CompilationAnalysisContext context)
    {
        var candidates = ContractForValidationEngine.FindCandidates(
            context.Compilation,
            tree => AnalyzerGeneratedCodePolicy.IsGenerated(
                tree,
                context.Compilation,
                context.CancellationToken),
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
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MayContainAdvisoryActivationSyntax(
                    tree.GetText(cancellationToken)))
            {
                continue;
            }

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
            }
        }

        var hasSharpProofAssemblyAttribute =
            compilation.Assembly.GetAttributes().Any(
            static attribute =>
                IsSharpProofAttributesNamespace(
                    attribute.AttributeClass?.ContainingNamespace));
        return hasSharpProofAssemblyAttribute
            ? AdvisoryActivation.Full
            : MayContainExternalClosedPreconditions(
                    compilation,
                    cancellationToken)
                ? AdvisoryActivation.Lightweight
                : AdvisoryActivation.None;
    }

    private static bool MayContainAdvisoryActivationSyntax(SourceText text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '[' ||
                (text[index] == '\\' &&
                 index + 1 < text.Length &&
                 text[index + 1] is 'u' or 'U'))
            {
                // Unicode escapes can spell any identifier. The syntax scan
                // compares decoded Identifier.ValueText before activation.
                return true;
            }
        }

        foreach (var candidate in
                 ContractApiMetadata.ContractMethodCandidateNames)
        {
            if (ContainsOrdinal(text, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsOrdinal(SourceText text, string value)
    {
        var lastStart = text.Length - value.Length;
        for (var start = 0; start <= lastStart; start++)
        {
            var matches = true;
            for (var offset = 0; offset < value.Length; offset++)
            {
                if (text[start + offset] == value[offset])
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
                var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
                if (symbol == null)
                {
                    return true;
                }

                var closedContractAttributes = GetClosedContractAttributes(
                    compilation);
                if (symbol is IAssemblySymbol assembly &&
                    NamespaceContainsClosedPrecondition(
                        assembly.GlobalNamespace,
                        closedContractAttributes,
                        cancellationToken))
                {
                    return true;
                }

                if (symbol is IModuleSymbol module &&
                    NamespaceContainsClosedPrecondition(
                        module.GlobalNamespace,
                        closedContractAttributes,
                        cancellationToken))
                {
                    return true;
                }

                continue;
            }

            return true;
        }

        return false;
    }

    private static ImmutableArray<INamedTypeSymbol>
        GetClosedContractAttributes(
        Compilation compilation)
    {
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

    private static bool PortableReferenceContainsClosedPrecondition(
        PortableExecutableReference reference)
    {
        try
        {
            return reference.GetMetadata() switch
            {
                AssemblyMetadata assembly => assembly.GetModules().Any(
                    static module =>
                        ModuleContainsClosedPrecondition(
                            module.GetMetadataReader())),
                ModuleMetadata module => ModuleContainsClosedPrecondition(
                    module.GetMetadataReader()),
                _ => true
            };
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (BadImageFormatException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (InvalidOperationException)
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
        var type = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference(
                    (MemberReferenceHandle)attribute.Constructor)
                .Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition(
                    (MethodDefinitionHandle)attribute.Constructor)
                .GetDeclaringType(),
            _ => default
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => IsClosedContractAttribute(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)type)
                    .Namespace,
                reader.GetTypeReference((TypeReferenceHandle)type)
                    .Name),
            HandleKind.TypeDefinition => IsClosedContractAttribute(
                reader,
                reader.GetTypeDefinition((TypeDefinitionHandle)type)
                    .Namespace,
                reader.GetTypeDefinition((TypeDefinitionHandle)type)
                    .Name),
            _ => false
        };
    }

    private static bool IsClosedContractAttribute(
        MetadataReader reader,
        StringHandle namespaceHandle,
        StringHandle nameHandle)
    {
        return ContractApiMetadata.IsClosedAttributeTypeName(
            reader.GetString(namespaceHandle),
            reader.GetString(nameHandle));
    }

    private static bool NamespaceContainsClosedPrecondition(
        INamespaceSymbol @namespace,
        ImmutableArray<INamedTypeSymbol> closedContractAttributes,
        CancellationToken cancellationToken)
    {
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
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (ContainsClosedContractAttribute(
                    method.GetReturnTypeAttributes(),
                    closedContractAttributes) ||
                method.Parameters.Any(parameter =>
                    ContainsClosedContractAttribute(
                        parameter.GetAttributes(),
                        closedContractAttributes)))
            {
                return true;
            }
        }

        return type.GetTypeMembers().Any(nested =>
            TypeContainsClosedPrecondition(
                nested,
                closedContractAttributes,
                cancellationToken));
    }

    private static bool ContainsClosedContractAttribute(
        ImmutableArray<AttributeData> attributes,
        ImmutableArray<INamedTypeSymbol> closedContractAttributes)
    {
        return attributes.Any(attribute =>
            closedContractAttributes.Any(expected =>
                SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass?.OriginalDefinition,
                    expected.OriginalDefinition)));
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
        return name != null &&
            ContractApiMetadata.IsContractMethodCandidateName(
                name.Identifier.ValueText);
    }

    // Decomposed deliberately: ToDisplayString and other string-based symbol
    // identity are banned in this layer (RS0030 / SPMETA001), so the namespace
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
        var target = (attribute.Parent as AttributeListSyntax)?
            .Target?.Identifier.Kind() ?? SyntaxKind.None;
        return target is
            SyntaxKind.AssemblyKeyword or
            SyntaxKind.ModuleKeyword;
    }

    internal static ImmutableArray<Diagnostic> GetConfigurationDiagnostics(
        Compilation compilation,
        AnalyzerOptions analyzerOptions,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var invalidValue in configuration.InvalidConfigurationValues)
        {
            diagnostics.Add(
                CreateInvalidConfigurationDiagnostic(invalidValue));
        }
        if (!configuration.InvalidConfigurationValues.IsEmpty)
        {
            return diagnostics.ToImmutable();
        }

        try
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var options = analyzerOptions.AnalyzerConfigOptionsProvider
                    .GetOptions(tree);
                var invalidValues =
                    AnalyzerConfiguration.GetInvalidTreeConfigurationValues(
                        options,
                        analyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions);
                var location = Location.Create(tree, new TextSpan(0, 0));
                foreach (var invalidValue in invalidValues)
                {
                    diagnostics.Add(
                        CreateInvalidConfigurationDiagnostic(
                            invalidValue,
                            location));
                }
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            diagnostics.Add(
                CreateInvalidConfigurationDiagnostic(
                    AnalyzerConfiguration.ProviderFailure(exception)));
        }

        return diagnostics.ToImmutable();
    }

    private static Action<CompilationAnalysisContext>
        CreateConfigurationReporter(
        ImmutableArray<Diagnostic> diagnostics)
    {
        return context =>
        {
            foreach (var diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        };
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
