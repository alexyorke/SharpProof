namespace SharpProof.Analyzer;

internal static class UnknownReasonDiagnosticProperties
{
    internal static ImmutableDictionary<string, string?> Add(
        ImmutableDictionary<string, string?> properties,
        SymbolicUnknownReasonInfo info)
    {
        if (properties == null) throw new ArgumentNullException(nameof(properties));

        if (info == null) throw new ArgumentNullException(nameof(info));

        return properties
            .SetItem("sharpproof.unknown.code", info.Code)
            .SetItem("sharpproof.unknown.category", info.Category.ToString())
            .SetItem("sharpproof.unknown.source", info.Source.ToString())
            .SetItem("sharpproof.unknown.raw_reason", info.RawReason)
            .SetItem("sharpproof.unknown.retryable", info.IsRetryable.ToString())
            .SetItem(
                "sharpproof.unknown.configuration_related",
                info.IsConfigurationRelated.ToString());
    }
}
