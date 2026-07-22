using System.Text;
namespace SharpProof.Identity;
internal readonly record struct StructuralParameterIdentity(string Type, string RefKind);
internal static class StructuralRefKinds {
    internal const string None = "none";
    internal const string Ref = "ref";
    internal const string Out = "out";
    internal const string In = "in";
    internal const string RefReadonly = "ref-readonly";
}
internal sealed class StructuralMethodIdentity {
    internal const string KeyPrefix = "spm1";
    internal StructuralMethodIdentity(
        string containingMetadataType,
        string methodKind,
        string name,
        int genericArity,
        IEnumerable<StructuralParameterIdentity> parameters,
        string returnType,
        string returnRefKind) {
        if (string.IsNullOrWhiteSpace(containingMetadataType))
            throw new ArgumentException("Containing metadata type is required.", nameof(containingMetadataType));
        if (string.IsNullOrWhiteSpace(methodKind))
            throw new ArgumentException("Method kind is required.", nameof(methodKind));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Method name is required.", nameof(name));
        if (genericArity < 0) throw new ArgumentOutOfRangeException(nameof(genericArity));
        if (string.IsNullOrWhiteSpace(returnType))
            throw new ArgumentException("Return type is required.", nameof(returnType));
        if (string.IsNullOrWhiteSpace(returnRefKind))
            throw new ArgumentException("Return ref kind is required.", nameof(returnRefKind));
        ContainingMetadataType = containingMetadataType.Trim();
        MethodKind = methodKind.Trim();
        Name = name.Trim();
        GenericArity = genericArity;
        Parameters = parameters?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType.Trim();
        ReturnRefKind = returnRefKind.Trim();
    }
    public string ContainingMetadataType { get; }
    public string MethodKind { get; }
    public string Name { get; }
    public int GenericArity { get; }
    public ImmutableArray<StructuralParameterIdentity> Parameters { get; }
    public string ReturnType { get; }
    public string ReturnRefKind { get; }
    internal string ToCanonicalKey() {
        var values = new List<string> {
            KeyPrefix,
            Encode(ContainingMetadataType),
            Encode(MethodKind),
            Encode(Name),
            GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Parameters.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        foreach (var parameter in Parameters) {
            values.Add(Encode(parameter.RefKind));
            values.Add(Encode(parameter.Type));
        }
        values.Add(Encode(ReturnRefKind));
        values.Add(Encode(ReturnType));
        return string.Join("|", values);
    }
    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
