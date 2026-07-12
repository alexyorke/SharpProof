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

        if (TryGetConfiguredKnownPureMember(symbol, out _)) return true;

        var methodSymbol = symbol as IMethodSymbol ??
                           (symbol is IPropertySymbol propertySymbol
                               ? propertySymbol.GetMethod ?? propertySymbol.SetMethod
                               : null);
        if (TryGetGeneratedMethodPurity(methodSymbol, compilation, out var generatedClassification) &&
            generatedClassification.IsPure)
            return true;

        if (IsSemanticallyPureMathMember(symbol)) return true;

        return false;
    }

    internal static bool IsConfiguredKnownPureMember(ISymbol symbol)
    {
        return TryGetConfiguredKnownPureMember(symbol, out _);
    }

    internal static bool TryGetConfiguredKnownPureMember(ISymbol symbol, out string configuredValue)
    {
        return TryGetConfiguredMember(symbol, ExtraPureMethods, out configuredValue);
    }

    internal static bool TryGetConfiguredKnownPureMember(
        ISymbol symbol,
        AnalyzerConfiguration configuration,
        out string configuredValue)
    {
        return TryGetConfiguredMember(symbol, configuration.ExtraKnownPureMethods, out configuredValue);
    }

    private static bool TryGetConfiguredMember(
        ISymbol symbol,
        ImmutableHashSet<string> configuredMembers,
        out string configuredValue)
    {
        configuredValue = string.Empty;
        if (!ConfiguredMemberKey.TryCreate(symbol, out var key) || !configuredMembers.Contains(key)) return false;

        configuredValue = key;
        return true;
    }

    private static bool TryGetGeneratedMethodPurity(
        IMethodSymbol? methodSymbol,
        Compilation? compilation,
        out GeneratedPurityCatalog.PurityEntry classification)
    {
        classification = default;
        if (compilation == null || methodSymbol == null) return false;

        return GeneratedPurityCatalog.Current.TryGetPurity(methodSymbol, compilation, out classification);
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

        if (TryGetConfiguredKnownImpureMember(symbol, out _)) return "config_known_impure";

        var signature = symbol.OriginalDefinition.ToDisplayString();
        if (symbol.Kind == SymbolKind.Property)
            if (!signature.EndsWith(".get") && !signature.EndsWith(".set"))
                signature += ".get";

        if (Constants.KnownImpureMethods.Contains(signature)) return "known_impure";


        if (symbol.ContainingType != null)
        {
            var simplifiedName = $"{symbol.ContainingType.Name}.{symbol.Name}";
            if (Constants.KnownImpureMethods.Contains(simplifiedName)) return "known_impure";
        }

        if (symbol is IMethodSymbol genericMethodSymbol && genericMethodSymbol.IsGenericMethod)
        {
            signature = genericMethodSymbol.ConstructedFrom.ToDisplayString();
            if (Constants.KnownImpureMethods.Contains(signature)) return "known_impure";
        }

        return null;
    }

    internal static bool TryGetConfiguredKnownImpureMember(ISymbol symbol, out string configuredValue)
    {
        return TryGetConfiguredMember(symbol, ExtraImpureMethods, out configuredValue);
    }

    internal static bool TryGetConfiguredKnownImpureMember(
        ISymbol symbol,
        AnalyzerConfiguration configuration,
        out string configuredValue)
    {
        return TryGetConfiguredMember(symbol, configuration.ExtraKnownImpureMethods, out configuredValue);
    }

    internal static bool TryGetConfiguredImpureBoundary(ISymbol symbol, out string source, out string configuredValue)
    {
        return TryGetConfiguredImpureBoundary(
            symbol,
            ExtraImpureTypes,
            ExtraImpureNamespaces,
            out source,
            out configuredValue);
    }

    internal static bool TryGetConfiguredImpureBoundary(
        ISymbol symbol,
        AnalyzerConfiguration configuration,
        out string source,
        out string configuredValue)
    {
        return TryGetConfiguredImpureBoundary(
            symbol,
            configuration.ExtraKnownImpureTypes,
            configuration.ExtraKnownImpureNamespaces,
            out source,
            out configuredValue);
    }

    private static bool TryGetConfiguredImpureBoundary(
        ISymbol symbol,
        ImmutableHashSet<string> configuredTypes,
        ImmutableHashSet<string> configuredNamespaces,
        out string source,
        out string configuredValue)
    {
        source = string.Empty;
        configuredValue = string.Empty;
        if (symbol == null) return false;

        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        while (containingType != null)
        {
            var typeName = containingType.OriginalDefinition.ToDisplayString();
            if (configuredTypes.Contains(typeName))
            {
                source = "config_known_impure_type";
                configuredValue = typeName;
                return true;
            }

            var ns = containingType.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                var namespaceName = ns.ToDisplayString();
                if (configuredNamespaces.Contains(namespaceName))
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
