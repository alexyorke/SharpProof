using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Engine;

internal static partial class ImpurityCatalog
{
    private static readonly AsyncLocal<AnalyzerConfiguration?> _configuredOverrides = new();

    private static ImmutableHashSet<string> ExtraImpureMethods =>
        _configuredOverrides.Value?.ExtraKnownImpureMethods ?? ImmutableHashSet<string>.Empty;

    private static ImmutableHashSet<string> ExtraPureMethods =>
        _configuredOverrides.Value?.ExtraKnownPureMethods ?? ImmutableHashSet<string>.Empty;

    private static ImmutableHashSet<string> ExtraImpureNamespaces =>
        _configuredOverrides.Value?.ExtraKnownImpureNamespaces ?? ImmutableHashSet<string>.Empty;

    private static ImmutableHashSet<string> ExtraImpureTypes =>
        _configuredOverrides.Value?.ExtraKnownImpureTypes ?? ImmutableHashSet<string>.Empty;

    internal static bool IsStrictPurityProfile =>
        string.Equals(_configuredOverrides.Value?.PurityProfile, "strict", StringComparison.OrdinalIgnoreCase);

    internal static IDisposable UseConfiguredOverrides(AnalyzerConfiguration config)
    {
        var previous = _configuredOverrides.Value;
        _configuredOverrides.Value = config;
        return new ConfiguredOverrideScope(previous);
    }

    public static bool IsKnownPureBCLMember(ISymbol symbol, Compilation? compilation)
    {
        if (symbol == null) return false;

        if (IsInConfiguredImpureNamespaceOrType(symbol) && !IsConfiguredKnownPureMember(symbol)) return false;

        if (IsMutableImmutableBuilderMember(symbol)) return false;

        if (IsImmutableInterlockedMember(symbol)) return false;

        var methodSymbol = symbol as IMethodSymbol ??
                           (symbol is IPropertySymbol propertySymbol
                               ? propertySymbol.GetMethod ?? propertySymbol.SetMethod
                               : null);
        if (TryGetGeneratedMethodPurity(methodSymbol, compilation, out var generatedSignature,
                out var generatedClassification) &&
            generatedClassification.IsPure)
            return true;

        if (IsSemanticallyPureMathMember(symbol)) return true;

        var signature = symbol.OriginalDefinition.ToDisplayString();
        if (symbol is IPropertySymbol signatureProperty)
            if (!signature.EndsWith(".get") && !signature.EndsWith(".set"))
                signature += GetExistingAccessorSuffix(signatureProperty);

        var isKnownPure = MatchesConfiguredKnownPureSignature(signature);

        if (!isKnownPure && symbol is IMethodSymbol genericMethod && genericMethod.IsGenericMethod)
        {
            signature = genericMethod.ConstructedFrom.ToDisplayString();
            isKnownPure = MatchesConfiguredKnownPureSignature(signature);
        }
        else if (!isKnownPure && symbol is IPropertySymbol genericProperty &&
                 genericProperty.ContainingType.IsGenericType)
        {
            if (genericProperty.IsIndexer)
                signature = genericProperty.OriginalDefinition.ToDisplayString();
            else
                signature =
                    $"{genericProperty.ContainingType.ConstructedFrom.ToDisplayString()}.{genericProperty.Name}{GetExistingAccessorSuffix(genericProperty)}";
            isKnownPure = MatchesConfiguredKnownPureSignature(signature);
        }

        if (isKnownPure)
        {
        }

        return isKnownPure;
    }

    private static string GetExistingAccessorSuffix(IPropertySymbol propertySymbol)
    {
        return propertySymbol.GetMethod != null ? ".get" : ".set";
    }

    internal static bool IsConfiguredKnownPureMember(ISymbol symbol)
    {
        return TryGetConfiguredKnownPureMember(symbol, out _);
    }

    internal static bool TryGetConfiguredKnownPureMember(ISymbol symbol, out string configuredValue)
    {
        configuredValue = string.Empty;
        var signature = symbol.OriginalDefinition.ToDisplayString();
        if (TryMatchConfiguredKnownPureSignature(signature, out configuredValue)) return true;

        foreach (var accessorSignature in GetPropertyAccessorSignatureCandidates(symbol))
            if (TryMatchConfiguredKnownPureSignature(accessorSignature, out configuredValue))
                return true;

        if (symbol is IMethodSymbol accessorSymbol &&
            accessorSymbol.AssociatedSymbol is IPropertySymbol associatedProperty)
        {
            var accessorSuffix = accessorSymbol.MethodKind == MethodKind.PropertySet ? ".set" : ".get";
            var associatedSignature = associatedProperty.OriginalDefinition.ToDisplayString();
            if (TryMatchConfiguredKnownPureSignature(
                    associatedSignature + accessorSuffix,
                    out configuredValue))
                return true;
        }

        if (symbol is IMethodSymbol propertyAccessorSymbol &&
            propertyAccessorSymbol.ContainingType != null &&
            (propertyAccessorSymbol.MethodKind == MethodKind.PropertyGet ||
             propertyAccessorSymbol.MethodKind == MethodKind.PropertySet))
        {
            var accessorName = propertyAccessorSymbol.Name;
            var propertyName = accessorName.StartsWith("get_", StringComparison.Ordinal) ||
                               accessorName.StartsWith("set_", StringComparison.Ordinal)
                ? accessorName.Substring(4)
                : accessorName;
            var accessorSuffix = propertyAccessorSymbol.MethodKind == MethodKind.PropertySet ? ".set" : ".get";
            var propertyStyleSignature =
                $"{propertyAccessorSymbol.ContainingType.OriginalDefinition.ToDisplayString()}.{propertyName}{accessorSuffix}";
            if (TryMatchConfiguredKnownPureSignature(propertyStyleSignature, out configuredValue)) return true;
        }

        if (symbol.Kind == SymbolKind.Property &&
            !signature.EndsWith(".get", StringComparison.Ordinal) &&
            !signature.EndsWith(".set", StringComparison.Ordinal))
            signature += ".get";

        if (TryMatchConfiguredKnownPureSignature(signature, out configuredValue)) return true;

        if (symbol is IMethodSymbol methodSymbol && methodSymbol.IsGenericMethod)
            return TryMatchConfiguredKnownPureSignature(
                methodSymbol.ConstructedFrom.ToDisplayString(),
                out configuredValue);

        if (symbol is IPropertySymbol propertySymbol && propertySymbol.ContainingType.IsGenericType)
        {
            signature = propertySymbol.IsIndexer
                ? propertySymbol.OriginalDefinition.ToDisplayString()
                : $"{propertySymbol.ContainingType.ConstructedFrom.ToDisplayString()}.{propertySymbol.Name}.get";

            return TryMatchConfiguredKnownPureSignature(signature, out configuredValue);
        }

        return false;
    }

    private static IEnumerable<string> GetPropertyAccessorSignatureCandidates(ISymbol symbol)
    {
        if (symbol is IPropertySymbol propertySymbol)
        {
            foreach (var containingTypeName in GetContainingTypeNames(propertySymbol.ContainingType))
            {
                yield return $"{containingTypeName}.{propertySymbol.Name}.get";
                yield return $"{containingTypeName}.get_{propertySymbol.Name}";
                yield return $"{containingTypeName}.get_{propertySymbol.Name}()";
            }

            yield break;
        }

        if (symbol is IFieldSymbol fieldSymbol)
        {
            foreach (var containingTypeName in GetContainingTypeNames(fieldSymbol.ContainingType))
            {
                yield return $"{containingTypeName}.{fieldSymbol.Name}";
                yield return $"{containingTypeName}.{fieldSymbol.Name}.get";
            }

            yield break;
        }

        if (symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.ContainingType == null)
            yield break;

        var accessorSuffix = methodSymbol.MethodKind == MethodKind.PropertySet ? ".set" : ".get";
        if (methodSymbol.AssociatedSymbol is IPropertySymbol associatedProperty)
            foreach (var containingTypeName in GetContainingTypeNames(associatedProperty.ContainingType))
            {
                yield return $"{containingTypeName}.{associatedProperty.Name}{accessorSuffix}";
                yield return $"{containingTypeName}.{methodSymbol.Name}";
                yield return $"{containingTypeName}.{methodSymbol.Name}()";
            }

        if (!methodSymbol.Name.StartsWith("get_", StringComparison.Ordinal) &&
            !methodSymbol.Name.StartsWith("set_", StringComparison.Ordinal))
            yield break;

        var propertyName = methodSymbol.Name.Substring(4);
        foreach (var containingTypeName in GetContainingTypeNames(methodSymbol.ContainingType))
        {
            yield return $"{containingTypeName}.{propertyName}{accessorSuffix}";
            yield return $"{containingTypeName}.{methodSymbol.Name}";
            yield return $"{containingTypeName}.{methodSymbol.Name}()";
        }
    }

    private static IEnumerable<string> GetContainingTypeNames(INamedTypeSymbol? containingType)
    {
        if (containingType == null) yield break;

        yield return containingType.ToDisplayString();
        var originalDefinition = containingType.OriginalDefinition.ToDisplayString();
        if (!string.Equals(originalDefinition, containingType.ToDisplayString(), StringComparison.Ordinal))
            yield return originalDefinition;

        if (containingType.IsGenericType)
        {
            var constructedFrom = containingType.ConstructedFrom.ToDisplayString();
            if (!string.Equals(constructedFrom, containingType.ToDisplayString(), StringComparison.Ordinal) &&
                !string.Equals(constructedFrom, originalDefinition, StringComparison.Ordinal))
                yield return constructedFrom;
        }
    }

    private static bool MatchesConfiguredKnownPureSignature(string signature)
    {
        return MatchesSignature(ExtraPureMethods, NormalizeSignatures(ExtraPureMethods), signature);
    }

    private static bool TryMatchConfiguredKnownPureSignature(string signature, out string configuredValue)
    {
        return TryMatchSignature(
            ExtraPureMethods,
            NormalizeSignatures(ExtraPureMethods),
            signature,
            out configuredValue);
    }

    private static bool TryGetGeneratedMethodPurity(
        IMethodSymbol? methodSymbol,
        Compilation? compilation,
        out string signature,
        out GeneratedPurityCatalog.PurityEntry classification)
    {
        signature = methodSymbol?.ToDisplayString() ?? string.Empty;
        classification = default;
        if (compilation == null || methodSymbol == null) return false;

        if (!GeneratedPurityCatalog.Current.TryGetPurity(methodSymbol, compilation, out classification)) return false;

        signature = methodSymbol.OriginalDefinition.ToDisplayString();
        return true;
    }

    private static bool MatchesSignature(
        IEnumerable<string> signatures,
        ImmutableHashSet<string> normalizedSignatures,
        string signature)
    {
        return TryMatchSignature(signatures, normalizedSignatures, signature, out _);
    }

    private static bool TryMatchSignature(
        IEnumerable<string> signatures,
        ImmutableHashSet<string> normalizedSignatures,
        string signature,
        out string matchedSignature)
    {
        matchedSignature = string.Empty;
        if (signatures.Contains(signature))
        {
            matchedSignature = signature;
            return true;
        }

        var normalizedSignature = NormalizeSignature(signature);
        if (!string.Equals(normalizedSignature, signature, StringComparison.Ordinal) &&
            signatures.Contains(normalizedSignature))
        {
            matchedSignature = normalizedSignature;
            return true;
        }

        if (!normalizedSignatures.Contains(normalizedSignature)) return false;

        matchedSignature = signatures
            .Where(candidate => string.Equals(
                NormalizeSignature(candidate),
                normalizedSignature,
                StringComparison.Ordinal))
            .OrderBy(static candidate => candidate, StringComparer.Ordinal)
            .FirstOrDefault() ?? normalizedSignature;
        return true;
    }

    private static ImmutableHashSet<string> NormalizeSignatures(IEnumerable<string> signatures)
    {
        return signatures
            .Select(NormalizeSignature)
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeSignature(string signature)
    {
        return signature.IndexOf('?') >= 0
            ? signature.Replace("?", string.Empty)
            : signature;
    }

    public static bool IsKnownImpure(ISymbol symbol)
    {
        if (symbol == null) return false;

        if (GetKnownImpureMemberSource(symbol) != null) return true;

        if (symbol is IPropertySymbol property && IsInImpureNamespaceOrType(property.ContainingType))
        {
        }

        return false;
    }

    public static string? GetKnownImpureMemberSource(ISymbol symbol)
    {
        if (symbol == null) return null;

        if (IsMutableImmutableBuilderMember(symbol)) return "known_impure";

        if (IsImmutableInterlockedMember(symbol)) return "known_impure";

        if (symbol is IMethodSymbol objectEqualsMethodSymbol &&
            objectEqualsMethodSymbol.ContainingType?.SpecialType == SpecialType.System_Object &&
            objectEqualsMethodSymbol.Name == nameof(object.Equals) &&
            objectEqualsMethodSymbol.Parameters.Length == 1)
            return "known_impure";

        if (symbol is IMethodSymbol staticObjectEqualsSymbol &&
            staticObjectEqualsSymbol.ContainingType?.SpecialType == SpecialType.System_Object &&
            staticObjectEqualsSymbol.Name == nameof(object.Equals) &&
            staticObjectEqualsSymbol.IsStatic &&
            staticObjectEqualsSymbol.Parameters.Length == 2)
            return "known_impure";

        if (symbol is IMethodSymbol staticTypeGetTypeSymbol &&
            staticTypeGetTypeSymbol.IsStatic &&
            staticTypeGetTypeSymbol.ContainingType?.ToDisplayString().Equals("System.Type", StringComparison.Ordinal) ==
            true &&
            staticTypeGetTypeSymbol.Name == nameof(Type.GetType) &&
            staticTypeGetTypeSymbol.Parameters.Length >= 1 &&
            staticTypeGetTypeSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
            return "known_impure";

        if (IsRandomSemanticImpure(symbol)) return "random_semantic_rule";

        if (IsStringBuilderSemanticImpure(symbol)) return "string_builder_semantic_rule";

        if (IsArrayMutationSemanticImpure(symbol)) return "array_mutation_semantic_rule";

        if (IsThreadingSemanticImpure(symbol)) return "threading_semantic_rule";

        if (IsXmlLinqSemanticImpure(symbol)) return "xml_linq_semantic_rule";

        if (IsDiagnosticsTracingSemanticImpure(symbol)) return "diagnostics_tracing_semantic_rule";

        if (IsIoStreamTextSemanticImpure(symbol)) return "io_stream_text_semantic_rule";

        if (IsAssemblyLoadContextSemanticImpure(symbol)) return "assembly_load_context_semantic_rule";

        var signature = symbol.OriginalDefinition.ToDisplayString();
        if (symbol.Kind == SymbolKind.Property)
            if (!signature.EndsWith(".get") && !signature.EndsWith(".set"))
                signature += ".get";

        if (ExtraImpureMethods.Contains(signature)) return "config_known_impure";

        if (Constants.KnownImpureMethods.Contains(signature)) return "known_impure";


        if (symbol.ContainingType != null)
        {
            var simplifiedName = $"{symbol.ContainingType.Name}.{symbol.Name}";
            if (ExtraImpureMethods.Contains(simplifiedName)) return "config_known_impure";

            if (Constants.KnownImpureMethods.Contains(simplifiedName)) return "known_impure";
        }

        if (symbol is IMethodSymbol genericMethodSymbol && genericMethodSymbol.IsGenericMethod)
        {
            signature = genericMethodSymbol.ConstructedFrom.ToDisplayString();
            if (ExtraImpureMethods.Contains(signature)) return "config_known_impure";

            if (Constants.KnownImpureMethods.Contains(signature)) return "known_impure";
        }

        return null;
    }

    internal static bool TryGetConfiguredKnownImpureMember(ISymbol symbol, out string configuredValue)
    {
        configuredValue = string.Empty;
        if (symbol == null) return false;

        var signature = symbol.OriginalDefinition.ToDisplayString();
        if (symbol.Kind == SymbolKind.Property &&
            !signature.EndsWith(".get", StringComparison.Ordinal) &&
            !signature.EndsWith(".set", StringComparison.Ordinal))
            signature += ".get";

        if (ExtraImpureMethods.Contains(signature))
        {
            configuredValue = signature;
            return true;
        }

        if (symbol.ContainingType != null)
        {
            var simplifiedName = $"{symbol.ContainingType.Name}.{symbol.Name}";
            if (ExtraImpureMethods.Contains(simplifiedName))
            {
                configuredValue = simplifiedName;
                return true;
            }
        }

        if (symbol is IMethodSymbol genericMethodSymbol && genericMethodSymbol.IsGenericMethod)
        {
            signature = genericMethodSymbol.ConstructedFrom.ToDisplayString();
            if (ExtraImpureMethods.Contains(signature))
            {
                configuredValue = signature;
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetConfiguredImpureBoundary(ISymbol symbol, out string source, out string configuredValue)
    {
        source = string.Empty;
        configuredValue = string.Empty;
        if (symbol == null) return false;

        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        while (containingType != null)
        {
            var typeName = containingType.OriginalDefinition.ToDisplayString();
            if (ExtraImpureTypes.Contains(typeName))
            {
                source = "config_known_impure_type";
                configuredValue = typeName;
                return true;
            }

            var ns = containingType.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                var namespaceName = ns.ToDisplayString();
                if (ExtraImpureNamespaces.Contains(namespaceName))
                {
                    source = "config_known_impure_namespace";
                    configuredValue = namespaceName;
                    return true;
                }

                ns = ns.ContainingNamespace;
            }

            containingType = containingType.ContainingType;
        }

        return false;
    }

    internal static bool TryGetBuiltInKnownPureMember(ISymbol symbol, out string catalogValue)
    {
        catalogValue = string.Empty;
        if (!IsSemanticallyPureMathMember(symbol)) return false;

        catalogValue = "semantic_math_member";
        return true;
    }

    public static bool IsInImpureNamespaceOrType(ISymbol symbol)
    {
        if (symbol == null) return false;

        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        while (containingType != null)
        {
            var typeName = containingType.OriginalDefinition.ToDisplayString();
            if (Constants.KnownImpureTypeNames.Contains(typeName) || ExtraImpureTypes.Contains(typeName)) return true;

            var ns = containingType.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                var namespaceName = ns.ToDisplayString();
                if (Constants.KnownImpureNamespaces.Contains(namespaceName) ||
                    ExtraImpureNamespaces.Contains(namespaceName)) return true;
                ns = ns.ContainingNamespace;
            }

            containingType = containingType.ContainingType;
        }

        return false;
    }

    public static bool IsInConfiguredImpureNamespaceOrType(ISymbol symbol)
    {
        if (symbol == null) return false;

        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        while (containingType != null)
        {
            var typeName = containingType.OriginalDefinition.ToDisplayString();
            if (ExtraImpureTypes.Contains(typeName)) return true;

            var ns = containingType.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                if (ExtraImpureNamespaces.Contains(ns.ToDisplayString())) return true;

                ns = ns.ContainingNamespace;
            }

            containingType = containingType.ContainingType;
        }

        return false;
    }

    private static bool IsMutableImmutableBuilderMember(ISymbol symbol)
    {
        if (!IsImmutableBuilderType(symbol.ContainingType)) return false;

        if (symbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.MethodKind == MethodKind.PropertySet ||
                methodSymbol.MethodKind == MethodKind.EventAdd ||
                methodSymbol.MethodKind == MethodKind.EventRemove)
                return true;

            return methodSymbol.Name is "Add"
                or "AddRange"
                or "Clear"
                or "Insert"
                or "InsertRange"
                or "Remove"
                or "RemoveAll"
                or "RemoveAt"
                or "RemoveRange"
                or "Reverse"
                or "Sort"
                or "UnionWith"
                or "IntersectWith"
                or "ExceptWith"
                or "SymmetricExceptWith";
        }

        if (symbol is IPropertySymbol propertySymbol) return propertySymbol.SetMethod != null;

        return false;
    }

    private static bool IsImmutableBuilderType(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol == null || !string.Equals(typeSymbol.Name, "Builder", StringComparison.Ordinal)) return false;

        return typeSymbol.ContainingNamespace?.ToString()
            .StartsWith("System.Collections.Immutable", StringComparison.Ordinal) == true;
    }

    private static bool IsImmutableInterlockedMember(ISymbol symbol)
    {
        return string.Equals(symbol.ContainingType?.ToDisplayString(),
            "System.Collections.Immutable.ImmutableInterlocked", StringComparison.Ordinal);
    }

    private sealed class ConfiguredOverrideScope : IDisposable
    {
        private readonly AnalyzerConfiguration? _previous;
        private bool _disposed;

        public ConfiguredOverrideScope(AnalyzerConfiguration? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _configuredOverrides.Value = _previous;
            _disposed = true;
        }
    }
}
