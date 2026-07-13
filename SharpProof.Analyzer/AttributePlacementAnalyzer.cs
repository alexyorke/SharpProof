using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer;

internal static class AttributePlacementAnalyzer
{
    private static readonly ImmutableArray<AttributePlacementRule> PlacementRules =
        ImmutableArray.Create(
            new AttributePlacementRule(
                "EnforcePureAttribute", "EnforcePure",
                SharpProofDiagnostics.MisplacedAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "PureAttribute", "Pure",
                SharpProofDiagnostics.MisplacedAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "AllowSynchronizationAttribute", "AllowSynchronization",
                SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeRule,
                AttributeTargetPolicy.PurityOnly),
            new AttributePlacementRule(
                "ZeroAllocationsAttribute", "ZeroAllocations",
                SharpProofDiagnostics.MisplacedZeroAllocationsAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "AllowedCapabilitiesAttribute", "AllowedCapabilities",
                SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "EnsuresAttribute", "Ensures",
                SharpProofDiagnostics.MisplacedEnsuresAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "RequiresAttribute", "Requires",
                SharpProofDiagnostics.MisplacedRequiresAttributeRule,
                AttributeTargetPolicy.PurityOnly),
            new AttributePlacementRule(
                "DoesNotThrowAttribute", "DoesNotThrow",
                SharpProofDiagnostics.MisplacedExceptionContractAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "AllowedExceptionsAttribute", "AllowedExceptions",
                SharpProofDiagnostics.MisplacedExceptionContractAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias),
            new AttributePlacementRule(
                "ExpectedComplexityAttribute", "ExpectedComplexity",
                SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule,
                AttributeTargetPolicy.PurityOrGetterAlias));

    internal static void AnalyzeNonMethodDeclaration(
        SyntaxNodeAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        if (context.Node is not AttributeListSyntax attributeList) return;

        ReportUnrecognizedAttributeIdentities(context, baseline, attributePolicy, attributeList);
        var attributeTarget = attributeList.Parent;

        var isAllowedPurityTarget = IsAllowedPurityTarget(attributeTarget);
        var isAllowedGetterAliasTarget = isAllowedPurityTarget ||
                                         AttributeTargetSyntaxFacts.IsGetterAliasTarget(attributeTarget);
        foreach (var rule in PlacementRules)
        {
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
        AttributePlacementRule rule)
    {
        if (isAllowedTarget) return;

        foreach (var attribute in attributeList.Attributes)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var attributeClass = GetAttributeClass(attribute, context.SemanticModel, context.CancellationToken);
            if (!attributePolicy.IsAccepted(attributeClass, rule.AttributeTypeName)) continue;

            var diagnostic = CreateMisplacedAttributeDiagnostic(
                rule.Descriptor,
                attribute.GetLocation(),
                rule.AttributeName,
                attributeTarget,
                context);
            ReportIfNotSuppressed(context, baseline, diagnostic);
        }
    }

    private static void ReportUnrecognizedAttributeIdentities(
        SyntaxNodeAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy,
        AttributeListSyntax attributeList)
    {
        foreach (var attribute in attributeList.Attributes)
        {
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
            ReportIfNotSuppressed(context, baseline, diagnostic);
        }
    }

    private static Diagnostic CreateMisplacedAttributeDiagnostic(
        DiagnosticDescriptor descriptor,
        Location location,
        string attributeName,
        SyntaxNode? attributeTarget,
        SyntaxNodeAnalysisContext context)
    {
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
        SyntaxNodeAnalysisContext context)
    {
        var attributeName = attributeClass.Name;
        var displayName = SharpProofAttributeIdentityPolicy.GetDisplayName(attributeClass);
        var namespaceName = SharpProofAttributeIdentityPolicy.GetNamespaceName(attributeClass);
        var evidenceKey = "unrecognized_attribute_identity:" + displayName;
        var properties = BaselineDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.AttributeIdentityNameProperty, attributeName)
                .Add(SharpProofDiagnostics.AttributeIdentityNamespaceProperty,
                    namespaceName.Length == 0 ? SharpProofAttributeIdentityPolicy.GlobalNamespaceToken : namespaceName)
                .Add(SharpProofDiagnostics.AttributeIdentityAcceptedNamespacesProperty,
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
            SharpProofDiagnostics.UnrecognizedAttributeIdentityRule,
            location,
            null,
            properties,
            new object[]
            {
                attributeName,
                displayName
            });
    }

    private static void ReportIfNotSuppressed(
        SyntaxNodeAnalysisContext context,
        DiagnosticBaseline baseline,
        Diagnostic diagnostic)
    {
        if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static INamedTypeSymbol? GetAttributeClass(
        AttributeSyntax attribute,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(attribute, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol attributeConstructorSymbol)
            return attributeConstructorSymbol.ContainingType;

        if (symbolInfo.Symbol is INamedTypeSymbol directAttributeSymbol) return directAttributeSymbol;

        return semanticModel.GetTypeInfo(attribute, cancellationToken).Type as INamedTypeSymbol;
    }

    private static bool IsAllowedPurityTarget(SyntaxNode? node)
    {
        return node is MethodDeclarationSyntax ||
               node is AccessorDeclarationSyntax ||
               node is ConstructorDeclarationSyntax ||
               node is ConversionOperatorDeclarationSyntax ||
               node is OperatorDeclarationSyntax ||
               node is LocalFunctionStatementSyntax;
    }

    private enum AttributeTargetPolicy
    {
        PurityOnly,
        PurityOrGetterAlias
    }

    private readonly record struct AttributePlacementRule(
        string AttributeTypeName,
        string AttributeName,
        DiagnosticDescriptor Descriptor,
        AttributeTargetPolicy TargetPolicy);
}
