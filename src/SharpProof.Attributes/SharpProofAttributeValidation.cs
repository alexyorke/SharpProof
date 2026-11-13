namespace SharpProof.Attributes;

internal static class SharpProofAttributeValidation
{
    // The masks are the union of the declared enum members, so a new member
    // is accepted everywhere without a second list to update.
    private static readonly long s_knownEffects = Union(typeof(SharpProofEffect));
    private static readonly long s_knownCapabilities = Union(typeof(SharpProofCapability));

    internal static SharpProofEffect RequireKnown(SharpProofEffect effects,
        /* SPINT001: all values valid - Any name, including null, is accepted by the exception constructors. */ string parameterName)
    {
        return ((long)effects & ~s_knownEffects) != 0
            ? throw new ArgumentOutOfRangeException(parameterName)
            : effects;
    }

    internal static SharpProofCapability RequireKnown(SharpProofCapability capabilities,
        /* SPINT001: all values valid - Any name, including null, is accepted by the exception constructors. */ string parameterName)
    {
        return ((long)capabilities & ~s_knownCapabilities) != 0
            ? throw new ArgumentOutOfRangeException(parameterName)
            : capabilities;
    }

    // Callers get their own array: the stored one cannot be changed through
    // a value handed in or out.
    internal static Type[] Copy(Type[] types,
        /* SPINT001: all values valid - Any name, including null, is accepted by the exception constructors. */ string parameterName)
    {
        return types is null ? throw new ArgumentNullException(parameterName) : (Type[])types.Clone();
    }

    private static long Union(Type flags)
    {
        if (flags is null)
        {
            throw new ArgumentNullException(nameof(flags));
        }

        long union = 0;
        foreach (var value in Enum.GetValues(flags))
        {
            union |= Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return union;
    }

    internal static string RequireReason(
        string reason,
        /* SPINT001: all values valid - Every message, including null, is accepted by ArgumentException. */ string message)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException(message, nameof(reason))
            : reason;
    }
}
