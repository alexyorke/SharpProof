namespace SharpProof.Analyzer.Engine;

internal static class BclPurityFallbackClassifier {
    public const string CatalogSource = BclPurityFallbackHeuristics.CatalogSource;

    public static bool TryClassify(
        ISymbol? symbol,
        out BclPurityFallbackHeuristics.Classification classification) {
        classification = default;
        if (symbol == null) return false;

        var original = symbol.OriginalDefinition;
        if (!IsFrameworkMetadataSymbol(original)) return false;

        if (original is IMethodSymbol methodSymbol &&
            methodSymbol.AssociatedSymbol is IPropertySymbol associatedProperty)
            return TryClassifyProperty(associatedProperty.OriginalDefinition, out classification);

        if (original is IPropertySymbol propertySymbol) return TryClassifyProperty(propertySymbol, out classification);

        if (original is IFieldSymbol fieldSymbol) return TryClassifyField(fieldSymbol, out classification);

        if (original is IMethodSymbol method) {
            var shape = CreateMethodShape(method);
            return TryClassifyShape(shape, out classification);
        }

        return false;
    }

    private static bool TryClassifyProperty(
        IPropertySymbol property,
        out BclPurityFallbackHeuristics.Classification classification) {
        var shape = CreatePropertyShape(property);
        return TryClassifyShape(shape, out classification);
    }

    private static bool IsFrameworkMetadataSymbol(ISymbol symbol) {
        if (!PurityAnalysisEngine.IsMetadataSymbol(symbol)) return false;

        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!BclPurityFallbackHeuristics.IsSystemNamespace(namespaceName)) return false;

        var assemblyName = symbol.ContainingAssembly?.Identity.Name ?? string.Empty;
        return BclPurityFallbackHeuristics.IsFrameworkSystemAssemblyName(assemblyName);
    }

    private static BclPurityFallbackHeuristics.Shape CreateMethodShape(IMethodSymbol method) => new BclPurityFallbackHeuristics.Shape(
            method.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            method.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty,
            method.Name,
            IsFrameworkMetadataSymbol(method),
            false,
            false,
            method.MethodKind == MethodKind.Constructor,
            method.IsStatic,
            method.ReturnsVoid,
            method.ReturnsByRef || method.ReturnsByRefReadonly,
            method.Parameters.Any(static parameter =>
                parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out),
            IsValueLikeType(method.ReturnType),
            method.ContainingType?.IsValueType == true,
            method.Parameters.All(static parameter =>
                IsValueLikeType(parameter.Type) || IsReadOnlyViewType(parameter.Type)),
            false);

    private static BclPurityFallbackHeuristics.Shape CreatePropertyShape(IPropertySymbol property) => new BclPurityFallbackHeuristics.Shape(
            property.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            property.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty,
            property.Name,
            IsFrameworkMetadataSymbol(property),
            true,
            false,
            false,
            property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true,
            false,
            property.ReturnsByRef || property.ReturnsByRefReadonly,
            property.Parameters.Any(static parameter =>
                parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out),
            IsValueLikeType(property.Type),
            property.ContainingType?.IsValueType == true,
            property.Parameters.All(static parameter =>
                IsValueLikeType(parameter.Type) || IsReadOnlyViewType(parameter.Type)),
            property.SetMethod != null && property.GetMethod == null);

    private static BclPurityFallbackHeuristics.Shape CreateFieldShape(IFieldSymbol field) => new BclPurityFallbackHeuristics.Shape(
            field.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            field.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty,
            field.Name,
            IsFrameworkMetadataSymbol(field),
            false,
            true,
            false,
            field.IsStatic,
            false,
            false,
            false,
            IsValueLikeType(field.Type),
            field.ContainingType?.IsValueType == true,
            true,
            false,
            field.IsReadOnly || field.IsConst);

    private static bool TryClassifyField(
        IFieldSymbol field,
        out BclPurityFallbackHeuristics.Classification classification) {
        var shape = CreateFieldShape(field);
        return TryClassifyShape(shape, out classification);
    }

    private static bool IsValueLikeType(ITypeSymbol type) {
        if (type.TypeKind == TypeKind.Enum ||
            type.IsValueType)
            return true;

        if (type.SpecialType == SpecialType.System_String ||
            type.SpecialType == SpecialType.System_Object)
            return true;

        var displayName = type.OriginalDefinition.ToDisplayString();
        return BclPurityFallbackHeuristics.IsValueLikeTypeName(displayName);
    }

    private static bool IsReadOnlyViewType(ITypeSymbol type) {
        var displayName = type.OriginalDefinition.ToDisplayString();
        return BclPurityFallbackHeuristics.IsReadOnlyViewTypeName(displayName);
    }

    private static bool TryClassifyShape(
        BclPurityFallbackHeuristics.Shape shape,
        out BclPurityFallbackHeuristics.Classification classification) => BclPurityFallbackHeuristics.TryClassify(shape, out classification);
}
