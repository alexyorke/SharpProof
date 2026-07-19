using System.Text.Json;
using SharpProof.Identity;

namespace SharpProof.Analyzer;

internal static class StructuralMethodIdentityJson
{
    internal static bool TryReadMethod(
        JsonElement methodElement,
        out StructuralMethodIdentity identity,
        out string canonicalKey)
    {
        identity = null!;
        canonicalKey = string.Empty;
        if (methodElement.ValueKind != JsonValueKind.Object ||
            !methodElement.TryGetProperty("Identity", out var identityElement) ||
            !TryReadIdentity(identityElement, out identity) ||
            !methodElement.TryGetProperty("CanonicalKey", out var keyElement) ||
            keyElement.ValueKind != JsonValueKind.String)
            return false;

        canonicalKey = keyElement.GetString()?.Trim() ?? string.Empty;
        return StructuralMethodIdentity.TryParseCanonicalKey(canonicalKey, out var parsed) &&
               parsed.Equals(identity) &&
               string.Equals(canonicalKey, identity.ToCanonicalKey(), StringComparison.Ordinal);
    }

    internal static bool TryReadIdentity(JsonElement element, out StructuralMethodIdentity identity)
    {
        identity = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadRequiredString(element, "ContainingMetadataType", out var containingType) ||
            !TryReadRequiredString(element, "MethodKind", out var methodKind) ||
            !TryReadRequiredString(element, "Name", out var name) ||
            !element.TryGetProperty("GenericArity", out var arityElement) ||
            arityElement.ValueKind != JsonValueKind.Number ||
            !arityElement.TryGetInt32(out var genericArity) ||
            genericArity < 0 ||
            !TryReadRequiredString(element, "ReturnType", out var returnType) ||
            !TryReadRequiredString(element, "ReturnRefKind", out var returnRefKind) ||
            !element.TryGetProperty("Parameters", out var parametersElement) ||
            parametersElement.ValueKind != JsonValueKind.Array)
            return false;

        var parameters = ImmutableArray.CreateBuilder<StructuralParameterIdentity>();
        foreach (var parameterElement in parametersElement.EnumerateArray())
        {
            if (!TryReadRequiredString(parameterElement, "Type", out var type) ||
                !TryReadRequiredString(parameterElement, "RefKind", out var refKind))
                return false;
            parameters.Add(new StructuralParameterIdentity(type, refKind));
        }

        try
        {
            identity = new StructuralMethodIdentity(
                containingType,
                methodKind,
                name,
                genericArity,
                parameters,
                returnType,
                returnRefKind);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static ImmutableArray<StructuralMethodIdentity> ReadCallChain(JsonElement element)
    {
        if (!element.TryGetProperty("CallChain", out var callChainElement) ||
            callChainElement.ValueKind != JsonValueKind.Array)
            return ImmutableArray<StructuralMethodIdentity>.Empty;

        var builder = ImmutableArray.CreateBuilder<StructuralMethodIdentity>();
        foreach (var identityElement in callChainElement.EnumerateArray())
        {
            if (!TryReadIdentity(identityElement, out var identity))
                return ImmutableArray<StructuralMethodIdentity>.Empty;
            builder.Add(identity);
        }

        return builder.ToImmutable();
    }

    private static bool TryReadRequiredString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.String)
            return false;

        value = valueElement.GetString()?.Trim() ?? string.Empty;
        return value.Length != 0;
    }
}
