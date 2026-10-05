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

    private static string Describe(IrFactory factory, OperationId site)
    { return factory.GetOperationInfo(site).Description is { } description ? factory.GetString(description) : ""; }
}
