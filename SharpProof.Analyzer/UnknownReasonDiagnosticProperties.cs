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
            .SetItem(SharpProofDiagnostics.UnknownReasonCodeProperty, info.Code)
            .SetItem(SharpProofDiagnostics.UnknownReasonCategoryProperty, info.Category.ToString())
            .SetItem(SharpProofDiagnostics.UnknownReasonSourceProperty, info.Source.ToString())
            .SetItem(SharpProofDiagnostics.UnknownReasonRawProperty, info.RawReason)
            .SetItem(SharpProofDiagnostics.UnknownReasonRetryableProperty, info.IsRetryable.ToString())
            .SetItem(
                SharpProofDiagnostics.UnknownReasonConfigurationRelatedProperty,
                info.IsConfigurationRelated.ToString());
    }
}
