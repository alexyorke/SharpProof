using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

internal sealed class SharpProofAttributeIdentityPolicy
{
    internal const string OfficialNamespace = "SharpProof.Attributes";
    internal const string GlobalNamespaceToken = "<global>";

    private static readonly ImmutableHashSet<string> SharpProofAttributeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AllowedCapabilitiesAttribute",
            "AllowedExceptionsAttribute",
            "AllowSynchronizationAttribute",
            "DoesNotThrowAttribute",
            "EnforcePureAttribute",
            "EnsuresAttribute",
            "ExpectedComplexityAttribute",
            "ImpureAttribute",
            "PureAttribute",
            "PureExternalAttribute",
            "RequiresAttribute",
            "ZeroAllocationsAttribute");

    private readonly ImmutableHashSet<string> _acceptedNamespaces;

    private SharpProofAttributeIdentityPolicy(ImmutableHashSet<string> acceptedNamespaces)
    {
        _acceptedNamespaces = acceptedNamespaces;
    }

    internal string AcceptedNamespacesDisplay =>
        string.Join(
            ";",
            _acceptedNamespaces
                .Select(static value => value.Length == 0 ? GlobalNamespaceToken : value)
                .OrderBy(static value => value, StringComparer.Ordinal));

    internal static SharpProofAttributeIdentityPolicy Create(ImmutableHashSet<string> configuredStubNamespaces)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        builder.Add(OfficialNamespace);
        foreach (var configuredNamespace in configuredStubNamespaces)
        {
            var normalized = NormalizeConfiguredNamespace(configuredNamespace);
            if (normalized != null) builder.Add(normalized);
        }

        return new SharpProofAttributeIdentityPolicy(builder.ToImmutable());
    }

    internal INamedTypeSymbol? ResolveAttributeSymbol(
        Compilation compilation,
        string attributeTypeName)
    {
        var official = compilation.GetTypeByMetadataName(OfficialNamespace + "." + attributeTypeName);
        if (official != null) return official;

        foreach (var namespaceName in _acceptedNamespaces.OrderBy(static value => value, StringComparer.Ordinal))
        {
            if (string.Equals(namespaceName, OfficialNamespace, StringComparison.Ordinal)) continue;

            var metadataName = namespaceName.Length == 0
                ? attributeTypeName
                : namespaceName + "." + attributeTypeName;
            var symbol = compilation.GetTypeByMetadataName(metadataName);
            if (symbol != null) return symbol;
        }

        return null;
    }

    internal bool HasAttribute(
        ISymbol symbol,
        string attributeTypeName)
    {
        return GetAcceptedAttributes(symbol, attributeTypeName).Any();
    }

    internal INamedTypeSymbol? GetAppliedAttributeSymbol(
        ISymbol symbol,
        string attributeTypeName)
    {
        return GetAcceptedAttributes(symbol, attributeTypeName)
            .Select(static attribute => attribute.AttributeClass?.OriginalDefinition)
            .FirstOrDefault(static attributeClass => attributeClass != null);
    }

    internal IEnumerable<AttributeData> GetAcceptedAttributes(
        ISymbol symbol,
        string attributeTypeName)
    {
        var associatedAttributePolicy = GetAssociatedAttributePolicy(attributeTypeName);
        foreach (var attribute in SymbolAttributeTraversal.GetAttributes(symbol, associatedAttributePolicy))
            if (IsAccepted(attribute, attributeTypeName))
                yield return attribute;
    }

    internal bool IsAccepted(
        AttributeData attribute,
        string attributeTypeName)
    {
        return IsAccepted(attribute.AttributeClass, attributeTypeName);
    }

    internal bool IsAccepted(
        INamedTypeSymbol? attributeClass,
        string attributeTypeName)
    {
        var originalDefinition = attributeClass?.OriginalDefinition;
        if (originalDefinition == null ||
            !string.Equals(originalDefinition.Name, attributeTypeName, StringComparison.Ordinal))
            return false;

        return _acceptedNamespaces.Contains(GetNamespaceName(originalDefinition));
    }


    internal bool IsUnrecognizedSharpProofLikeAttribute(
        INamedTypeSymbol? attributeClass)
    {
        attributeClass = attributeClass?.OriginalDefinition;
        if (attributeClass == null ||
            !SharpProofAttributeNames.Contains(attributeClass.Name))
            return false;

        if (IsRecognizedExternalPureAttribute(attributeClass)) return false;

        return !IsAccepted(attributeClass, attributeClass.Name);
    }

    internal static bool IsRecognizedExternalPureAttribute(INamedTypeSymbol attributeClass)
    {
        return string.Equals(attributeClass.Name, "PureAttribute", StringComparison.Ordinal) &&
               (string.Equals(GetNamespaceName(attributeClass), "JetBrains.Annotations", StringComparison.Ordinal) ||
                string.Equals(GetNamespaceName(attributeClass), "System.Diagnostics.Contracts",
                    StringComparison.Ordinal));
    }

    internal static string GetDisplayName(INamedTypeSymbol attributeClass)
    {
        var display = attributeClass.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return string.IsNullOrWhiteSpace(display)
            ? attributeClass.Name
            : display;
    }

    internal static string GetNamespaceName(INamedTypeSymbol attributeClass)
    {
        var namespaceName = attributeClass.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : attributeClass.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return namespaceName ?? string.Empty;
    }

    private static AssociatedAttributePolicy GetAssociatedAttributePolicy(string attributeTypeName)
    {
        if (attributeTypeName == "ImpureAttribute")
            return AssociatedAttributePolicy.PropertyForAnyAccessor;

        return attributeTypeName is
            "AllowedCapabilitiesAttribute" or
            "AllowedExceptionsAttribute" or
            "DoesNotThrowAttribute" or
            "EnforcePureAttribute" or
            "EnsuresAttribute" or
            "ExpectedComplexityAttribute" or
            "PureAttribute" or
            "PureExternalAttribute" or
            "ZeroAllocationsAttribute"
            ? AssociatedAttributePolicy.PropertyForGetter
            : AssociatedAttributePolicy.None;
    }

    private static string? NormalizeConfiguredNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, GlobalNamespaceToken, StringComparison.OrdinalIgnoreCase)) return string.Empty;

        return trimmed.Trim('.');
    }
}
