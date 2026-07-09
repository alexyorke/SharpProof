using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer
{
    internal static class MethodAllocationAnalyzer
    {
        private static readonly SymbolDisplayFormat AllocationSymbolDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static void AnalyzeSymbolForZeroAllocations(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            var zeroAllocationsAttributeSymbol =
                AttributeSymbolResolution.ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.ZeroAllocationsAttribute", "ZeroAllocationsAttribute")
                ?? AttributeSymbolResolution.GetAppliedAttributeSymbol(methodSymbol, "ZeroAllocationsAttribute");
            var hasZeroAllocationsAttribute =
                (zeroAllocationsAttributeSymbol != null && HasDirectAttribute(methodSymbol, zeroAllocationsAttributeSymbol))
                || HasDirectAttributeByName(methodSymbol, "ZeroAllocationsAttribute");
            if (!hasZeroAllocationsAttribute)
            {
                return;
            }

            var rootOperation = MethodBodyOperationResolver.GetMethodBodyRootOperation(context.Node, context.SemanticModel, context.CancellationToken, includeConversionOperators: false);
            if (rootOperation == null)
            {
                return;
            }

            foreach (var allocationSite in CollectAllocationSites(rootOperation))
            {
                var location = allocationSite.Syntax.GetLocation();
                var properties = CreateAllocationProperties(allocationSite, methodSymbol, context.Node.SyntaxTree);
                var diagnostic = Diagnostic.Create(
                    SharpProofDiagnostics.AllocationInZeroAllocationMethodRule,
                    location,
                    additionalLocations: null,
                    properties: properties,
                    messageArgs: new object[]
                    {
                        allocationSite.Syntax.ToString(),
                        methodSymbol.Name
                    });
                if (baseline.IsSuppressed(diagnostic))
                {
                    continue;
                }

                context.ReportDiagnostic(diagnostic);
            }
        }

        private static ImmutableDictionary<string, string?> CreateAllocationProperties(
            AllocationSite allocationSite,
            IMethodSymbol methodSymbol,
            SyntaxTree syntaxTree)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.AllocationKindProperty, allocationSite.AllocationKind)
                .Add(SharpProofDiagnostics.AllocationOperationKindProperty, allocationSite.Operation.Kind.ToString());

            if (allocationSite.Symbol != null)
            {
                properties = properties.Add(
                    SharpProofDiagnostics.AllocationSymbolProperty,
                    allocationSite.Symbol.ToDisplayString(AllocationSymbolDisplayFormat));
            }

            properties = BaselineDiagnosticProperties.Add(
                properties,
                methodSymbol,
                syntaxTree,
                allocationSite.Operation.Kind.ToString(),
                evidenceKey: CreateAllocationEvidenceKey(allocationSite));
            return ExplainDiagnosticProperties.Add(
                properties,
                allocationSite.Syntax.GetLocation(),
                "[ZeroAllocations]",
                "violated",
                allocationSite.AllocationKind);
        }

        private static string CreateAllocationEvidenceKey(AllocationSite allocationSite)
        {
            return allocationSite.AllocationKind +
                "@" +
                allocationSite.Syntax.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                allocationSite.Syntax.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "|" +
                (allocationSite.Symbol?.ToDisplayString(AllocationSymbolDisplayFormat) ?? string.Empty);
        }

        private static IEnumerable<AllocationSite> CollectAllocationSites(IOperation rootOperation)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                if (!TryCreateAllocationSite(operation, out var allocationSite))
                {
                    continue;
                }

                var key = allocationSite.Syntax.SpanStart.ToString() +
                    ":" +
                    allocationSite.Syntax.Span.End.ToString() +
                    ":" +
                    allocationSite.AllocationKind;
                if (seen.Add(key))
                {
                    yield return allocationSite;
                }
            }
        }

        private static bool TryCreateAllocationSite(IOperation operation, out AllocationSite allocationSite)
        {
            allocationSite = default;

            switch (operation)
            {
                case IObjectCreationOperation objectCreationOperation
                    when IsHeapAllocatedObjectType(objectCreationOperation.Type):
                    allocationSite = new AllocationSite(
                        objectCreationOperation.Syntax,
                        objectCreationOperation,
                        "object_creation",
                        objectCreationOperation.Constructor ?? (ISymbol?)objectCreationOperation.Type);
                    return true;

                case ITypeParameterObjectCreationOperation typeParameterObjectCreationOperation
                    when IsHeapAllocatedObjectType(typeParameterObjectCreationOperation.Type):
                    allocationSite = new AllocationSite(
                        typeParameterObjectCreationOperation.Syntax,
                        typeParameterObjectCreationOperation,
                        "object_creation",
                        typeParameterObjectCreationOperation.Type);
                    return true;

                case IArrayCreationOperation arrayCreationOperation
                    when !arrayCreationOperation.IsImplicit:
                    allocationSite = new AllocationSite(
                        arrayCreationOperation.Syntax,
                        arrayCreationOperation,
                        "array_creation",
                        arrayCreationOperation.Type);
                    return true;

                case IAnonymousObjectCreationOperation anonymousObjectCreationOperation:
                    allocationSite = new AllocationSite(
                        anonymousObjectCreationOperation.Syntax,
                        anonymousObjectCreationOperation,
                        "anonymous_object_creation",
                        anonymousObjectCreationOperation.Type);
                    return true;

                case ICollectionExpressionOperation collectionExpressionOperation
                    when collectionExpressionOperation.Type != null &&
                         !IsStackOnlyCollectionExpressionTarget(collectionExpressionOperation.Type):
                    allocationSite = new AllocationSite(
                        collectionExpressionOperation.Syntax,
                        collectionExpressionOperation,
                        "collection_expression",
                        collectionExpressionOperation.Type);
                    return true;

                case IDelegateCreationOperation delegateCreationOperation:
                    allocationSite = new AllocationSite(
                        delegateCreationOperation.Syntax,
                        delegateCreationOperation,
                        "delegate_creation",
                        delegateCreationOperation.Type);
                    return true;

                case IConversionOperation conversionOperation
                    when IsBoxingConversion(conversionOperation):
                    allocationSite = new AllocationSite(
                        conversionOperation.Syntax,
                        conversionOperation,
                        "boxing_conversion",
                        conversionOperation.Type);
                    return true;

                case IWithOperation withOperation
                    when IsHeapAllocatedObjectType(withOperation.Type):
                    allocationSite = new AllocationSite(
                        withOperation.Syntax,
                        withOperation,
                        "with_expression",
                        withOperation.Type);
                    return true;

                default:
                    return false;
            }
        }

        private static bool IsHeapAllocatedObjectType(ITypeSymbol? type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.IsReferenceType)
            {
                return true;
            }

            return type is ITypeParameterSymbol typeParameter && typeParameter.HasReferenceTypeConstraint;
        }

        private static bool IsBoxingConversion(IConversionOperation conversionOperation)
        {
            if (conversionOperation.Conversion.MethodSymbol != null)
            {
                return false;
            }

            var sourceType = conversionOperation.Operand?.Type;
            var targetType = conversionOperation.Type;
            if (sourceType == null || targetType == null || !sourceType.IsValueType)
            {
                return false;
            }

            if (targetType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            if (targetType.SpecialType == SpecialType.System_Object ||
                targetType.SpecialType == SpecialType.System_ValueType ||
                targetType.SpecialType == SpecialType.System_Enum)
            {
                return true;
            }

            if (targetType.TypeKind == TypeKind.Interface)
            {
                return true;
            }

            return targetType is ITypeParameterSymbol typeParameter && typeParameter.HasReferenceTypeConstraint;
        }

        private static bool IsStackOnlyCollectionExpressionTarget(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            var originalDefinition = namedType.OriginalDefinition;
            return originalDefinition.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System" &&
                (originalDefinition.Name == "Span" || originalDefinition.Name == "ReadOnlySpan");
        }

        private static bool HasDirectAttribute(IMethodSymbol methodSymbol, INamedTypeSymbol attributeType)
        {
            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                var attributeClass = attributeData.AttributeClass?.OriginalDefinition;
                if (SymbolEqualityComparer.Default.Equals(attributeClass, attributeType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDirectAttributeByName(IMethodSymbol methodSymbol, string attributeTypeName)
        {
            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                var attributeClass = attributeData.AttributeClass;
                if (attributeClass != null && string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly record struct AllocationSite(
            SyntaxNode Syntax,
            IOperation Operation,
            string AllocationKind,
            ISymbol? Symbol);
    }
}
