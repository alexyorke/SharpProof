using SharpProof.Identity;

namespace SharpProof.Test;

internal static class ConfiguredMemberKeyTestFactory {
    internal static string Method(
        string containingMetadataType,
        string name,
        string returnType = "named:System.Void",
        int genericArity = 0,
        params (string RefKind, string Type)[] parameters) {
        return Create(
            containingMetadataType,
            "ordinary",
            name,
            returnType,
            genericArity,
            parameters);
    }

    internal static string Getter(
        string containingMetadataType,
        string name,
        string returnType,
        params (string RefKind, string Type)[] indexParameters) {
        return Create(
                   containingMetadataType,
                   "property-get",
                   name,
                   returnType,
                   0,
                   indexParameters) +
               ".get";
    }

    internal static string Setter(
        string containingMetadataType,
        string name,
        string valueType,
        params (string RefKind, string Type)[] indexParameters) {
        var parameters = indexParameters
            .Concat(new[] { ("none", valueType) })
            .ToArray();
        return Create(
                   containingMetadataType,
                   "property-set",
                   name,
                   "named:System.Void",
                   0,
                   parameters) +
               ".set";
    }

    private static string Create(
        string containingMetadataType,
        string methodKind,
        string name,
        string returnType,
        int genericArity,
        IEnumerable<(string RefKind, string Type)> parameters) {
        return new StructuralMethodIdentity(
                containingMetadataType,
                methodKind,
                name,
                genericArity,
                parameters.Select(static parameter =>
                    new StructuralParameterIdentity(parameter.Type, parameter.RefKind)),
                returnType,
                "none")
            .ToCanonicalKey();
    }
}
