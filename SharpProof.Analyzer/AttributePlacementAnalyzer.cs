using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer
{
    internal static class AttributePlacementAnalyzer
    {
        internal static void AnalyzeNonMethodDeclaration(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline,
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
            if (context.Node is not AttributeListSyntax attributeList)
            {
                return;
            }

            ReportUnrecognizedAttributeIdentities(context, baseline, attributePolicy, attributeList);
            var attributeTarget = attributeList.Parent;

            var enforcePureAttributeLocation = FindAttributeLocation(attributeList, "EnforcePureAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (enforcePureAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
            {
                var diagnostic = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedAttributeRule,
                    enforcePureAttributeLocation,
                    "EnforcePure",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diagnostic);
            }

            var pureAttributeLocation = FindAttributeLocation(attributeList, "PureAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (pureAttributeLocation != null && !IsAllowedPureAttributeTarget(attributeTarget))
            {
                var diagnostic = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedAttributeRule,
                    pureAttributeLocation,
                    "Pure",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diagnostic);
            }

            var allowSynchronizationAttributeLocation = FindAttributeLocation(attributeList, "AllowSynchronizationAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (allowSynchronizationAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
            {
                var diag = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeRule,
                    allowSynchronizationAttributeLocation,
                    "AllowSynchronization",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diag);
            }

            var zeroAllocationsAttributeLocation = FindAttributeLocation(attributeList, "ZeroAllocationsAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (zeroAllocationsAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
            {
                var diag = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedZeroAllocationsAttributeRule,
                    zeroAllocationsAttributeLocation,
                    "ZeroAllocations",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diag);
            }

            var allowedCapabilitiesAttributeLocation = FindAttributeLocation(attributeList, "AllowedCapabilitiesAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (allowedCapabilitiesAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
            {
                var diag = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeRule,
                    allowedCapabilitiesAttributeLocation,
                    "AllowedCapabilities",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diag);
            }

            var ensuresAttributeLocation = FindAttributeLocation(attributeList, "EnsuresAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (ensuresAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
            {
                var diag = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedEnsuresAttributeRule,
                    ensuresAttributeLocation,
                    "Ensures",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diag);
            }

            var expectedComplexityAttributeLocation = FindAttributeLocation(attributeList, "ExpectedComplexityAttribute", attributePolicy, context.SemanticModel, context.CancellationToken);
            if (expectedComplexityAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
            {
                var diag = CreateMisplacedAttributeDiagnostic(
                    SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule,
                    expectedComplexityAttributeLocation,
                    "ExpectedComplexity",
                    attributeTarget,
                    context);
                ReportIfNotSuppressed(context, baseline, diag);
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
                {
                    continue;
                }

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
            var evidenceKey = descriptor.Id + ":" + attributeName + ":" + attributeTarget?.Kind().ToString();
            var properties = ImmutableDictionary<string, string?>.Empty;
            if (attributeTarget != null &&
                context.SemanticModel.GetDeclaredSymbol(attributeTarget, context.CancellationToken) is ISymbol targetSymbol)
            {
                properties = BaselineDiagnosticProperties.Add(
                    properties,
                    targetSymbol,
                    context.Node.SyntaxTree,
                    operationKind,
                    attributeName,
                    evidenceKey);
            }
            else
            {
                properties = BaselineDiagnosticProperties.Add(
                    properties,
                    attributeTarget?.Kind().ToString() ?? "<attribute-target>",
                    context.Node.SyntaxTree.FilePath ?? string.Empty,
                    operationKind,
                    attributeName,
                    evidenceKey);
            }

            properties = ExplainDiagnosticProperties.Add(
                properties,
                location,
                attributeName,
                "invalid",
                "misplaced_attribute");

            return Diagnostic.Create(
                descriptor,
                location,
                additionalLocations: null,
                properties: properties);
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
                    .Add(SharpProofDiagnostics.AttributeIdentityNamespaceProperty, namespaceName.Length == 0 ? SharpProofAttributeIdentityPolicy.GlobalNamespaceToken : namespaceName)
                    .Add(SharpProofDiagnostics.AttributeIdentityAcceptedNamespacesProperty, attributePolicy.AcceptedNamespacesDisplay),
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
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[]
                {
                    attributeName,
                    displayName,
                });
        }

        private static void ReportIfNotSuppressed(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline,
            Diagnostic diagnostic)
        {
            if (!baseline.IsSuppressed(diagnostic))
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        private static Location? FindAttributeLocation(
            AttributeListSyntax attributeList,
            string attributeTypeName,
            SharpProofAttributeIdentityPolicy attributePolicy,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributeClass = GetAttributeClass(attribute, semanticModel, cancellationToken);
                if (attributePolicy.IsAccepted(attributeClass, attributeTypeName))
                {
                    return attribute.GetLocation();
                }
            }
            return null;
        }

        private static INamedTypeSymbol? GetAttributeClass(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(attribute, cancellationToken);
            if (symbolInfo.Symbol is IMethodSymbol attributeConstructorSymbol)
            {
                return attributeConstructorSymbol.ContainingType;
            }

            if (symbolInfo.Symbol is INamedTypeSymbol directAttributeSymbol)
            {
                return directAttributeSymbol;
            }

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

        private static bool IsAllowedPureAttributeTarget(SyntaxNode? node)
        {
            return IsAllowedPurityTarget(node) ||
                   node is PropertyDeclarationSyntax ||
                   node is IndexerDeclarationSyntax;
        }

    }
}
