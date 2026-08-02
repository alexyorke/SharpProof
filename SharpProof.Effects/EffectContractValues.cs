namespace SharpProof.Effects;
internal static class EffectContractMetadata
{
    internal const string AttributeMetadataName =
        ContractApiMetadata.EffectContract;
    internal const string TrustedAttributeMetadataName =
        ContractApiMetadata.Trusted;
    internal const string CapabilitiesPropertyName = "Capabilities";
    internal const string CompletePropertyName = "Complete";
    internal const string IsDeterministicPropertyName = "IsDeterministic";
    internal const string PreconditionFreePropertyName = "PreconditionFree";
    internal const string ThrownExceptionsPropertyName = "ThrownExceptions";

    internal const EffectContractKind AllEffects =
        (EffectContractKind)((1L << 16) - 1);
    internal const EffectContractCapabilityKind AllCapabilities =
        (EffectContractCapabilityKind)((1 << 13) - 1);
    internal const EffectAnalysisIncompleteReason AllIncompleteReasons =
        (EffectAnalysisIncompleteReason)((1 << 4) - 1);

    internal static bool TryConvertInt64(object? value, out long result)
    {
        try
        {
            result = Convert.ToInt64(
                value,
                System.Globalization.CultureInfo.InvariantCulture);
            return value != null;
        }
        catch (Exception exception) when (
            exception is InvalidCastException or
            FormatException or
            OverflowException)
        {
            result = 0;
            return false;
        }
    }
}
