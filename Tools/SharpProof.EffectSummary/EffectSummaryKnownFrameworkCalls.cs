internal static class EffectSummaryKnownFrameworkCalls
{
    private const string StringComparerPrefix = "System.StringComparer.";
    private const string StringComparisonPrefix = "System.StringComparison.";

    internal static bool IsArrayDataReference(string callSymbol)
    {
        return callSymbol.StartsWith(
            "System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(",
            StringComparison.Ordinal);
    }

    internal static bool IsByRefLikeRuntimeTypeHelper(string callSymbol)
    {
        return callSymbol.StartsWith(
                   "System.ThrowHelper.ThrowArrayTypeMismatchException()",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith(
                   "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.get_IsValueType()", StringComparison.Ordinal) ||
               callSymbol.StartsWith(
                   "System.Type.op_Inequality(System.Type, System.Type)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("object.GetType()", StringComparison.Ordinal);
    }

    internal static bool TryGetStringComparerName(string getterSymbol, out string comparerName)
    {
        const string getterPrefix = "System.StringComparer.get_";
        const string getterSuffix = "()->System.StringComparer";
        comparerName = string.Empty;
        if (!getterSymbol.StartsWith(getterPrefix, StringComparison.Ordinal) ||
            !getterSymbol.EndsWith(getterSuffix, StringComparison.Ordinal))
            return false;

        var name = getterSymbol[getterPrefix.Length..^getterSuffix.Length];
        return TryNormalizeStringComparerName(name, out comparerName);
    }

    internal static bool TryGetStringComparerName(int comparisonValue, out string comparerName)
    {
        comparerName = string.Empty;
        return Enum.IsDefined(typeof(StringComparison), comparisonValue) &&
               TryNormalizeStringComparerName(((StringComparison)comparisonValue).ToString(), out comparerName);
    }

    internal static bool TryGetStringComparisonName(int value, out string name)
    {
        name = Enum.IsDefined(typeof(StringComparison), value)
            ? StringComparisonPrefix + (StringComparison)value
            : string.Empty;
        return name.Length != 0;
    }

    internal static bool IsDeterministicStringComparison(string type, string value)
    {
        if (type is not ("System.StringComparison" or "System.StringComparer")) return false;

        var prefix = type + ".";
        return value.StartsWith(prefix, StringComparison.Ordinal) && IsDeterministicName(value[prefix.Length..]);
    }

    private static bool TryNormalizeStringComparerName(string name, out string comparerName)
    {
        var valid = Enum.TryParse<StringComparison>(name, false, out var comparison) &&
                    string.Equals(Enum.GetName(typeof(StringComparison), comparison), name, StringComparison.Ordinal);
        comparerName = valid ? StringComparerPrefix + name : string.Empty;
        return valid;
    }

    private static bool IsDeterministicName(string name)
    {
        return name is nameof(StringComparison.InvariantCulture) or
            nameof(StringComparison.InvariantCultureIgnoreCase) or
            nameof(StringComparison.Ordinal) or
            nameof(StringComparison.OrdinalIgnoreCase);
    }
}
