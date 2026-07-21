namespace SharpProof.Analyzer;

internal static class AttributePlacementAnalyzer {
    private static readonly ImmutableArray<AttributePlacementRule> PlacementRules =
        ImmutableArray.Create(
            new AttributePlacementRule(
                "EnforcePureAttribute", "EnforcePure",
                AnalyzerDiagnosticCatalog.Get("MisplacedAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "ZeroAllocationsAttribute", "ZeroAllocations",
                AnalyzerDiagnosticCatalog.Get("MisplacedZeroAllocationsAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "AllowedCapabilitiesAttribute", "AllowedCapabilities",
                AnalyzerDiagnosticCatalog.Get("MisplacedAllowedCapabilitiesAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "EnsuresAttribute", "Ensures",
                AnalyzerDiagnosticCatalog.Get("MisplacedEnsuresAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "RequiresAttribute", "Requires",
                AnalyzerDiagnosticCatalog.Get("MisplacedRequiresAttributeRule"),
                AttributeTargetPolicy.PurityOnly),
            new AttributePlacementRule(
                "DoesNotThrowAttribute", "DoesNotThrow",
                AnalyzerDiagnosticCatalog.Get("MisplacedExceptionContractAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "AllowedExceptionsAttribute", "AllowedExceptions",
                AnalyzerDiagnosticCatalog.Get("MisplacedExceptionContractAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "ExpectedComplexityAttribute", "ExpectedComplexity",
                AnalyzerDiagnosticCatalog.Get("MisplacedExpectedComplexityAttributeRule"),
                AttributeTargetPolicy.PurityOrGetterAlias));

    internal static void AnalyzeNonMethodDeclaration(
        SyntaxNodeAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        if (context.Node is not AttributeListSyntax attributeList) return;

        ReportUnrecognizedAttributeIdentities(context, baseline, attributePolicy, attributeList);
        var attributeTarget = attributeList.Parent;

        var isAllowedPurityTarget = IsAllowedPurityTarget(attributeTarget);
        var isAllowedGetterAliasTarget = isAllowedPurityTarget ||
                                         AttributeTargetSyntaxFacts.IsGetterAliasTarget(attributeTarget);
        foreach (var rule in PlacementRules) {
            var isAllowedTarget = rule.TargetPolicy == AttributeTargetPolicy.PurityOnly
                ? isAllowedPurityTarget
                : isAllowedGetterAliasTarget;
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedTarget,
                rule);
        }
    }

    private static void ReportMisplacedAttributes(
        SyntaxNodeAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy,
        AttributeListSyntax attributeList,
        SyntaxNode? attributeTarget,
        bool isAllowedTarget,
        AttributePlacementRule rule) {
        if (isAllowedTarget) return;

        foreach (var attribute in attributeList.Attributes) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var attributeClass = GetAttributeClass(attribute, context.SemanticModel, context.CancellationToken);
            if (!attributePolicy.IsAccepted(attributeClass, rule.AttributeTypeName)) continue;

            var diagnostic = CreateMisplacedAttributeDiagnostic(
                rule.Descriptor,
                attribute.GetLocation(),
                rule.AttributeName,
                attributeTarget,
                context);
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
        }
    }

    private static void ReportUnrecognizedAttributeIdentities(
        SyntaxNodeAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy,
        AttributeListSyntax attributeList) {
        foreach (var attribute in attributeList.Attributes) {
            var attributeClass = GetAttributeClass(attribute, context.SemanticModel, context.CancellationToken);
            if (attributeClass == null ||
                !attributePolicy.IsUnrecognizedSharpProofLikeAttribute(attributeClass))
                continue;

            var location = attribute.Name.GetLocation();
            var diagnostic = CreateUnrecognizedAttributeIdentityDiagnostic(
                attributeClass,
                location,
                attributePolicy,
                context);
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
        }
    }

    private static Diagnostic CreateMisplacedAttributeDiagnostic(
        DiagnosticDescriptor descriptor,
        Location location,
        string attributeName,
        SyntaxNode? attributeTarget,
        SyntaxNodeAnalysisContext context) {
        var operationKind = "MisplacedAttribute";
        var evidenceKey = descriptor.Id + ":" + attributeName + ":" + attributeTarget?.Kind();
        var properties = ImmutableDictionary<string, string?>.Empty;
        if (attributeTarget != null &&
            context.SemanticModel.GetDeclaredSymbol(attributeTarget, context.CancellationToken) is ISymbol targetSymbol)
            properties = BaselineDiagnosticProperties.Add(
                properties,
                targetSymbol,
                context.Node.SyntaxTree,
                operationKind,
                attributeName,
                evidenceKey);
        else
            properties = BaselineDiagnosticProperties.Add(
                properties,
                attributeTarget?.Kind().ToString() ?? "<attribute-target>",
                context.Node.SyntaxTree.FilePath ?? string.Empty,
                operationKind,
                attributeName,
                evidenceKey);

        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            attributeName,
            "invalid",
            "misplaced_attribute");

        return Diagnostic.Create(
            descriptor,
            location,
            null,
            properties);
    }

    private static Diagnostic CreateUnrecognizedAttributeIdentityDiagnostic(
        INamedTypeSymbol attributeClass,
        Location location,
        SharpProofAttributeIdentityPolicy attributePolicy,
        SyntaxNodeAnalysisContext context) {
        var attributeName = attributeClass.Name;
        var displayName = SharpProofAttributeIdentityPolicy.GetDisplayName(attributeClass);
        var namespaceName = SharpProofAttributeIdentityPolicy.GetNamespaceName(attributeClass);
        var evidenceKey = "unrecognized_attribute_identity:" + displayName;
        var properties = BaselineDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add("sharpproof.attribute_identity.name", attributeName)
                .Add("sharpproof.attribute_identity.namespace",
                    namespaceName.Length == 0 ? SharpProofAttributeIdentityPolicy.GlobalNamespaceToken : namespaceName)
                .Add("sharpproof.attribute_identity.accepted_namespaces",
                    attributePolicy.AcceptedNamespacesDisplay),
            displayName,
            context.Node.SyntaxTree.FilePath ?? string.Empty,
            "AttributeIdentity",
            attributeName,
            evidenceKey);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            attributeName,
            "ignored",
            "unrecognized_attribute_identity");

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("UnrecognizedAttributeIdentityRule"),
            location,
            null,
            properties,
            [attributeName, displayName]);
    }

    private static INamedTypeSymbol? GetAttributeClass(
        AttributeSyntax attribute,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbolInfo = semanticModel.GetSymbolInfo(attribute, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol attributeConstructorSymbol)
            return attributeConstructorSymbol.ContainingType;

        if (symbolInfo.Symbol is INamedTypeSymbol directAttributeSymbol) return directAttributeSymbol;

        return semanticModel.GetTypeInfo(attribute, cancellationToken).Type as INamedTypeSymbol;
    }

    private static bool IsAllowedPurityTarget(SyntaxNode? node) {
        return node is MethodDeclarationSyntax ||
               node is AccessorDeclarationSyntax ||
               node is ConstructorDeclarationSyntax ||
               node is ConversionOperatorDeclarationSyntax ||
               node is OperatorDeclarationSyntax ||
               node is LocalFunctionStatementSyntax;
    }

    enum AttributeTargetPolicy {
        PurityOnly,
        PurityOrGetterAlias
    }

    readonly record struct AttributePlacementRule(
        string AttributeTypeName,
        string AttributeName,
        DiagnosticDescriptor Descriptor,
        AttributeTargetPolicy TargetPolicy);
}
