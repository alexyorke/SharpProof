using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer
{
    internal static class AttributePlacementAnalyzer
    {
        internal static void AnalyzeNonMethodDeclaration(SyntaxNodeAnalysisContext context)
        {
            var enforcePureAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.EnforcePureAttribute", "EnforcePureAttribute");
            var pureAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.PureAttribute", "PureAttribute");
            var allowSynchronizationAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.AllowSynchronizationAttribute", "AllowSynchronizationAttribute");
            var zeroAllocationsAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.ZeroAllocationsAttribute", "ZeroAllocationsAttribute");
            var allowedCapabilitiesAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.AllowedCapabilitiesAttribute", "AllowedCapabilitiesAttribute");
            var ensuresAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.EnsuresAttribute", "EnsuresAttribute");
            var expectedComplexityAttributeSymbol = AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.ExpectedComplexityAttribute", "ExpectedComplexityAttribute");

            if (enforcePureAttributeSymbol == null &&
                pureAttributeSymbol == null &&
                allowSynchronizationAttributeSymbol == null &&
                zeroAllocationsAttributeSymbol == null &&
                allowedCapabilitiesAttributeSymbol == null &&
                ensuresAttributeSymbol == null &&
                expectedComplexityAttributeSymbol == null)
            {
                return;
            }

            if (context.Node is not AttributeListSyntax attributeList)
            {
                return;
            }

            var attributeTarget = attributeList.Parent;

            if (enforcePureAttributeSymbol != null)
            {
                var enforcePureAttributeLocation = FindAttributeLocation(attributeList, enforcePureAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (enforcePureAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
                {
                    var diagnostic = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedAttributeRule,
                        enforcePureAttributeLocation
                    );
                    context.ReportDiagnostic(diagnostic);
                }
            }

            if (pureAttributeSymbol != null)
            {
                var pureAttributeLocation = FindAttributeLocation(attributeList, pureAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (pureAttributeLocation != null && !IsAllowedPureAttributeTarget(attributeTarget))
                {
                    var diagnostic = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedAttributeRule,
                        pureAttributeLocation
                    );
                    context.ReportDiagnostic(diagnostic);
                }
            }

            if (allowSynchronizationAttributeSymbol != null)
            {
                var allowSynchronizationAttributeLocation = FindAttributeLocation(attributeList, allowSynchronizationAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (allowSynchronizationAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
                {
                    var diag = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeRule,
                        allowSynchronizationAttributeLocation);
                    context.ReportDiagnostic(diag);
                }
            }

            if (zeroAllocationsAttributeSymbol != null)
            {
                var zeroAllocationsAttributeLocation = FindAttributeLocation(attributeList, zeroAllocationsAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (zeroAllocationsAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
                {
                    var diag = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedZeroAllocationsAttributeRule,
                        zeroAllocationsAttributeLocation);
                    context.ReportDiagnostic(diag);
                }
            }

            if (allowedCapabilitiesAttributeSymbol != null)
            {
                var allowedCapabilitiesAttributeLocation = FindAttributeLocation(attributeList, allowedCapabilitiesAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (allowedCapabilitiesAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
                {
                    var diag = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeRule,
                        allowedCapabilitiesAttributeLocation);
                    context.ReportDiagnostic(diag);
                }
            }

            if (ensuresAttributeSymbol != null)
            {
                var ensuresAttributeLocation = FindAttributeLocation(attributeList, ensuresAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (ensuresAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
                {
                    var diag = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedEnsuresAttributeRule,
                        ensuresAttributeLocation);
                    context.ReportDiagnostic(diag);
                }
            }

            if (expectedComplexityAttributeSymbol != null)
            {
                var expectedComplexityAttributeLocation = FindAttributeLocation(attributeList, expectedComplexityAttributeSymbol, context.SemanticModel, context.CancellationToken);
                if (expectedComplexityAttributeLocation != null && !IsAllowedPurityTarget(attributeTarget))
                {
                    var diag = Diagnostic.Create(
                        SharpProofDiagnostics.MisplacedExpectedComplexityAttributeRule,
                        expectedComplexityAttributeLocation);
                    context.ReportDiagnostic(diag);
                }
            }
        }

        private static Location? FindAttributeLocation(
            AttributeListSyntax attributeList,
            INamedTypeSymbol targetAttributeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbolInfo = semanticModel.GetSymbolInfo(attribute, cancellationToken);

                if (symbolInfo.Symbol is IMethodSymbol attributeConstructorSymbol &&
                    SymbolEqualityComparer.Default.Equals(attributeConstructorSymbol.ContainingType, targetAttributeSymbol))
                {
                    return attribute.GetLocation();
                }

                else if (symbolInfo.Symbol is INamedTypeSymbol directAttributeSymbol &&
                         SymbolEqualityComparer.Default.Equals(directAttributeSymbol, targetAttributeSymbol))
                {
                    return attribute.GetLocation();
                }

                else if (semanticModel.GetTypeInfo(attribute, cancellationToken).Type is INamedTypeSymbol attributeType &&
                       SymbolEqualityComparer.Default.Equals(attributeType, targetAttributeSymbol))
                {
                    return attribute.GetLocation();
                }
            }
            return null;
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
