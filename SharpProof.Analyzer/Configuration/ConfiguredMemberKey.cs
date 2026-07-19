using SharpProof.Identity;

namespace SharpProof.Analyzer.Configuration;

internal static class ConfiguredMemberKey
{
    internal const string GetterSuffix = ".get";
    internal const string SetterSuffix = ".set";

    internal static bool TryCreate(ISymbol symbol, out string key)
    {
        if (symbol is IPropertySymbol property)
        {
            if (property.GetMethod == null)
            {
                key = string.Empty;
                return false;
            }

            symbol = property.GetMethod;
        }

        if (symbol is not IMethodSymbol method)
        {
            key = string.Empty;
            return false;
        }

        if (method.ContainingType == null ||
            string.IsNullOrWhiteSpace(RoslynStructuralMethodIdentity.GetMetadataTypeName(method.ContainingType)))
        {
            key = string.Empty;
            return false;
        }

        key = Create(method);
        return true;
    }

    internal static string Create(IMethodSymbol method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));

        var key = RoslynStructuralMethodIdentity.GetCanonicalKey(method.OriginalDefinition);
        return method.MethodKind switch
        {
            MethodKind.PropertyGet => key + GetterSuffix,
            MethodKind.PropertySet => key + SetterSuffix,
            _ => key
        };
    }

    internal static bool TryParse(string? value, out StructuralMethodIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var key = value!.Trim();
        var accessorSuffix = string.Empty;
        if (key.EndsWith(GetterSuffix, StringComparison.Ordinal))
        {
            accessorSuffix = GetterSuffix;
            key = key.Substring(0, key.Length - GetterSuffix.Length);
        }
        else if (key.EndsWith(SetterSuffix, StringComparison.Ordinal))
        {
            accessorSuffix = SetterSuffix;
            key = key.Substring(0, key.Length - SetterSuffix.Length);
        }

        if (!StructuralMethodIdentity.TryParseCanonicalKey(key, out identity)) return false;

        return identity.MethodKind switch
        {
            "property-get" => accessorSuffix == GetterSuffix,
            "property-set" => accessorSuffix == SetterSuffix,
            _ => accessorSuffix.Length == 0
        };
    }
}
