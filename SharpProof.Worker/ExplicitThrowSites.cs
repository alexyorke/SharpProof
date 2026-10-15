namespace SharpProof.Worker;

// An explicit throw site's description lists the thrown static type and its
// base classes, most derived first; "exact:" marks a freshly created
// exception, whose static type is its runtime type.
internal static class ExplicitThrowSites
{
    private const string Prefix = "explicit-throw:";
    private const string ExactPrefix = Prefix + "exact:";

    internal static string[] Types(IrFactory factory, OperationId site)
    {
        var description = Describe(factory, site);
        if (!description.StartsWith(Prefix, StringComparison.Ordinal))
        { return []; }
        description = IsExact(factory, site) ? description[ExactPrefix.Length..] : description[Prefix.Length..];
        return [.. description.Split(';').Where(name => name.Length != 0).Distinct(StringComparer.Ordinal)];
    }

    internal static bool IsExact(IrFactory factory, OperationId site)
    { return Describe(factory, site).StartsWith(ExactPrefix, StringComparison.Ordinal); }

    // Identities are assembly-qualified as "assembly::type"; a witness shows the type.
    internal static string TypeName(string identity)
    {
        var separator = identity.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? identity : identity[(separator + 2)..];
    }

    internal static string TypeName(IrExceptionKind kind)
    {
        return kind switch
        {
            IrExceptionKind.NullReference => "System.NullReferenceException",
            IrExceptionKind.DivideByZero => "System.DivideByZeroException",
            IrExceptionKind.Overflow => "System.OverflowException",
            IrExceptionKind.IndexOutOfRange => "System.IndexOutOfRangeException",
            IrExceptionKind.InvalidCast => "System.InvalidCastException",
            IrExceptionKind.Argument => "System.ArgumentException",
            _ => "System.Exception"
        };
    }

    private static string Describe(IrFactory factory, OperationId site)
    { return factory.GetOperationInfo(site).Description is { } description ? factory.GetString(description) : ""; }
}
