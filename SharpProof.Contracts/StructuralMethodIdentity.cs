using System.Text;

namespace SharpProof.Identity;

internal readonly record struct StructuralParameterIdentity(
    string Type,
    string RefKind);

internal static class StructuralRefKinds
{
    internal const string None = "none";
    internal const string Ref = "ref";
    internal const string Out = "out";
    internal const string In = "in";
    internal const string RefReadonly = "ref-readonly";

    internal static string CollapseUnavailableParameterKind(string refKind)
    {
        return refKind is Out or In or RefReadonly ? Ref : refKind;
    }
}

internal sealed class StructuralMethodIdentity : IEquatable<StructuralMethodIdentity>
{
    internal const int ContractVersion = 1;
    internal const string KeyPrefix = "spm1";

    internal StructuralMethodIdentity(
        string containingMetadataType,
        string methodKind,
        string name,
        int genericArity,
        IEnumerable<StructuralParameterIdentity> parameters,
        string returnType,
        string returnRefKind)
    {
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

    internal StructuralMethodIdentity WithContainingMetadataType(string containingMetadataType)
    {
        return new StructuralMethodIdentity(
            containingMetadataType,
            MethodKind,
            Name,
            GenericArity,
            Parameters,
            ReturnType,
            ReturnRefKind);
    }

    internal StructuralMethodIdentity WithUnavailableParameterRefKindsCollapsed()
    {
        return new StructuralMethodIdentity(
            ContainingMetadataType,
            MethodKind,
            Name,
            GenericArity,
            Parameters.Select(static parameter => parameter with
            {
                RefKind = StructuralRefKinds.CollapseUnavailableParameterKind(parameter.RefKind)
            }),
            ReturnType,
            ReturnRefKind);
    }

    internal string ToCanonicalKey()
    {
        var values = new List<string>
        {
            KeyPrefix,
            Encode(ContainingMetadataType),
            Encode(MethodKind),
            Encode(Name),
            GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Parameters.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        foreach (var parameter in Parameters)
        {
            values.Add(Encode(parameter.RefKind));
            values.Add(Encode(parameter.Type));
        }

        values.Add(Encode(ReturnRefKind));
        values.Add(Encode(ReturnType));
        return string.Join("|", values);
    }

    internal static bool TryParseCanonicalKey(string? key, out StructuralMethodIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var parts = key!.Trim().Split('|');
        if (parts.Length < 8 || !string.Equals(parts[0], KeyPrefix, StringComparison.Ordinal)) return false;
        if (!TryDecode(parts[1], out var containingType) ||
            !TryDecode(parts[2], out var methodKind) ||
            !TryDecode(parts[3], out var name) ||
            !int.TryParse(parts[4], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var arity) ||
            arity < 0 ||
            !int.TryParse(parts[5], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parameterCount) ||
            parameterCount < 0 ||
            parts.Length != 8 + parameterCount * 2)
            return false;

        var parameters = ImmutableArray.CreateBuilder<StructuralParameterIdentity>(parameterCount);
        var partIndex = 6;
        for (var index = 0; index < parameterCount; index++)
        {
            if (!TryDecode(parts[partIndex++], out var refKind) ||
                !TryDecode(parts[partIndex++], out var type))
                return false;
            parameters.Add(new StructuralParameterIdentity(type, refKind));
        }

        if (!TryDecode(parts[partIndex++], out var returnRefKind) ||
            !TryDecode(parts[partIndex], out var returnType))
            return false;

        try
        {
            identity = new StructuralMethodIdentity(
                containingType,
                methodKind,
                name,
                arity,
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

    public bool Equals(StructuralMethodIdentity? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null ||
            GenericArity != other.GenericArity ||
            !string.Equals(ContainingMetadataType, other.ContainingMetadataType, StringComparison.Ordinal) ||
            !string.Equals(MethodKind, other.MethodKind, StringComparison.Ordinal) ||
            !string.Equals(Name, other.Name, StringComparison.Ordinal) ||
            !string.Equals(ReturnType, other.ReturnType, StringComparison.Ordinal) ||
            !string.Equals(ReturnRefKind, other.ReturnRefKind, StringComparison.Ordinal) ||
            Parameters.Length != other.Parameters.Length)
            return false;

        for (var index = 0; index < Parameters.Length; index++)
            if (!Parameters[index].Equals(other.Parameters[index]))
                return false;

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is StructuralMethodIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(ContainingMetadataType);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(MethodKind);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Name);
            hash = hash * 31 + GenericArity;
            foreach (var parameter in Parameters) hash = hash * 31 + parameter.GetHashCode();
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(ReturnType);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(ReturnRefKind);
            return hash;
        }
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static bool TryDecode(string value, out string decoded)
    {
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }
}
