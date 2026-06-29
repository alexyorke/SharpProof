using System.Linq;
using Microsoft.CodeAnalysis;

namespace PurelySharp.Analyzer.Engine
{
    internal static class BclPurityFallbackClassifier
    {
        public const string CatalogSource = BclPurityFallbackHeuristics.CatalogSource;
        public const string ProbablyPure = BclPurityFallbackHeuristics.ProbablyPure;
        public const string ProbablyImpure = BclPurityFallbackHeuristics.ProbablyImpure;
        public const string Unknown = BclPurityFallbackHeuristics.Unknown;

        public readonly struct Classification
        {
            public Classification(string guess, string confidence, string reason, string category)
            {
                Guess = guess;
                Confidence = confidence;
                Reason = reason;
                Category = category;
            }

            public string Guess { get; }
            public string Confidence { get; }
            public string Reason { get; }
            public string Category { get; }
        }

        public static bool TryClassify(ISymbol? symbol, out Classification classification)
        {
            classification = default;
            if (symbol == null)
            {
                return false;
            }

            var original = symbol.OriginalDefinition;
            if (!IsFrameworkMetadataSymbol(original))
            {
                return false;
            }

            if (original is IMethodSymbol methodSymbol &&
                methodSymbol.AssociatedSymbol is IPropertySymbol associatedProperty)
            {
                return TryClassifyProperty(associatedProperty.OriginalDefinition, out classification);
            }

            if (original is IPropertySymbol propertySymbol)
            {
                return TryClassifyProperty(propertySymbol, out classification);
            }

            if (original is IMethodSymbol method)
            {
                var shape = CreateMethodShape(method);
                return TryClassifyShape(shape, out classification);
            }

            return false;
        }

        private static bool TryClassifyProperty(IPropertySymbol property, out Classification classification)
        {
            var shape = CreatePropertyShape(property);
            return TryClassifyShape(shape, out classification);
        }

        private static bool IsFrameworkMetadataSymbol(ISymbol symbol)
        {
            if (!PurityAnalysisEngine.IsMetadataSymbol(symbol))
            {
                return false;
            }

            var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!BclPurityFallbackHeuristics.IsSystemNamespace(namespaceName))
            {
                return false;
            }

            var assemblyName = symbol.ContainingAssembly?.Identity.Name ?? string.Empty;
            return BclPurityFallbackHeuristics.IsFrameworkSystemAssemblyName(assemblyName);
        }

        private static BclPurityFallbackHeuristics.Shape CreateMethodShape(IMethodSymbol method)
        {
            return new BclPurityFallbackHeuristics.Shape(
                namespaceName: method.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                typeName: method.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty,
                memberName: method.Name,
                isFrameworkMetadataSymbol: IsFrameworkMetadataSymbol(method),
                isProperty: false,
                isConstructor: method.MethodKind == MethodKind.Constructor,
                isStatic: method.IsStatic,
                returnsVoid: method.ReturnsVoid,
                returnsByRef: method.ReturnsByRef || method.ReturnsByRefReadonly,
                hasRefOrOutParameter: method.Parameters.Any(static parameter =>
                    parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out),
                hasValueLikeReturn: IsValueLikeType(method.ReturnType),
                hasOnlyValueLikeOrReadOnlyViewParameters: method.Parameters.All(static parameter =>
                    IsValueLikeType(parameter.Type) || IsReadOnlyViewType(parameter.Type)),
                isSetterOnlyProperty: false);
        }

        private static BclPurityFallbackHeuristics.Shape CreatePropertyShape(IPropertySymbol property)
        {
            return new BclPurityFallbackHeuristics.Shape(
                namespaceName: property.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                typeName: property.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty,
                memberName: property.Name,
                isFrameworkMetadataSymbol: IsFrameworkMetadataSymbol(property),
                isProperty: true,
                isConstructor: false,
                isStatic: property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true,
                returnsVoid: false,
                returnsByRef: property.ReturnsByRef || property.ReturnsByRefReadonly,
                hasRefOrOutParameter: property.Parameters.Any(static parameter =>
                    parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out),
                hasValueLikeReturn: IsValueLikeType(property.Type),
                hasOnlyValueLikeOrReadOnlyViewParameters: property.Parameters.All(static parameter =>
                    IsValueLikeType(parameter.Type) || IsReadOnlyViewType(parameter.Type)),
                isSetterOnlyProperty: property.SetMethod != null && property.GetMethod == null);
        }

        private static bool IsValueLikeType(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum ||
                type.IsValueType)
            {
                return true;
            }

            if (type.SpecialType == SpecialType.System_String ||
                type.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            var displayName = type.OriginalDefinition.ToDisplayString();
            return BclPurityFallbackHeuristics.IsValueLikeTypeName(displayName);
        }

        private static bool IsReadOnlyViewType(ITypeSymbol type)
        {
            var displayName = type.OriginalDefinition.ToDisplayString();
            return BclPurityFallbackHeuristics.IsReadOnlyViewTypeName(displayName);
        }

        private static bool TryClassifyShape(
            BclPurityFallbackHeuristics.Shape shape,
            out Classification classification)
        {
            if (!BclPurityFallbackHeuristics.TryClassify(shape, out var sharedClassification))
            {
                classification = default;
                return false;
            }

            classification = new Classification(
                sharedClassification.Guess,
                sharedClassification.Confidence,
                sharedClassification.Reason,
                sharedClassification.Category);
            return true;
        }
    }
}
