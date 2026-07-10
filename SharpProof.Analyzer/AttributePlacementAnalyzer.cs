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

            var isAllowedPurityTarget = IsAllowedPurityTarget(attributeTarget);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "EnforcePureAttribute", "EnforcePure",
                SharpProofDiagnostics.MisplacedAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                IsAllowedPureAttributeTarget(attributeTarget),
                "PureAttribute", "Pure",
                SharpProofDiagnostics.MisplacedAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "AllowSynchronizationAttribute", "AllowSynchronization",
                SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "ZeroAllocationsAttribute", "ZeroAllocations",
                SharpProofDiagnostics.MisplacedZeroAllocationsAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "AllowedCapabilitiesAttribute", "AllowedCapabilities",
                SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "EnsuresAttribute", "Ensures",
                SharpProofDiagnostics.MisplacedEnsuresAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "RequiresAttribute", "Requires",
                SharpProofDiagnostics.MisplacedRequiresAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "DoesNotThrowAttribute", "DoesNotThrow",
                SharpProofDiagnostics.MisplacedExceptionContractAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "AllowedExceptionsAttribute", "AllowedExceptions",
                SharpProofDiagnostics.MisplacedExceptionContractAttributeRule);
            ReportMisplacedAttributes(
                context, baseline, attributePolicy, attributeList, attributeTarget,
                isAllowedPurityTarget,
                "ExpectedComplexityAttribute", "ExpectedComplexity",
                SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule);
        }

        private static void ReportMisplacedAttributes(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline,
            SharpProofAttributeIdentityPolicy attributePolicy,
            AttributeListSyntax attributeList,
            SyntaxNode? attributeTarget,
            bool isAllowedTarget,
            string attributeTypeName,
            string attributeName,
            DiagnosticDescriptor descriptor)
        {
            if (isAllowedTarget)
            {
                return;
            }

            foreach (var attribute in attributeList.Attributes)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var attributeClass = GetAttributeClass(attribute, context.SemanticModel, context.CancellationToken);
                if (!attributePolicy.IsAccepted(attributeClass, attributeTypeName))
                {
                    continue;
                }

                var diagnostic = CreateMisplacedAttributeDiagnostic(
                    descriptor,
                    attribute.GetLocation(),
                    attributeName,
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
