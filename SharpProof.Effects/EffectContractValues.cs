namespace SharpProof.Effects;
[Flags]
public enum EffectContractKind : long {
    None = 0,
    ReadsReceiverState = 1L << 0, ReadsArgumentState = 1L << 1,
    ReadsCapturedState = 1L << 2, ReadsStaticState = 1L << 3,
    ReadsAmbientState = 1L << 4, WritesReceiverState = 1L << 5,
    WritesArgumentState = 1L << 6, WritesCapturedState = 1L << 7,
    WritesStaticState = 1L << 8, WritesAmbientState = 1L << 9,
    Allocates = 1L << 10, Throws = 1L << 11,
    Synchronizes = 1L << 12, UsesNondeterminism = 1L << 13,
    UsesNativeCode = 1L << 14, UsesReflection = 1L << 15
}
[Flags]
public enum EffectContractCapabilityKind {
    None = 0,
    IO = 1 << 0, FileRead = 1 << 1, FileWrite = 1 << 2,
    Network = 1 << 3, Console = 1 << 4, Process = 1 << 5,
    Environment = 1 << 6, Registry = 1 << 7, Clock = 1 << 8,
    Randomness = 1 << 9, Reflection = 1 << 10,
    Synchronization = 1 << 11,
    NativeInterop = 1 << 12
}
internal static class EffectContractMetadata {
    internal const string AttributeMetadataName =
        "SharpProof.Attributes.EffectContractAttribute";
    internal const string TrustedAttributeMetadataName =
        "SharpProof.Attributes.SharpProofTrustedAttribute";
    internal const string CapabilitiesPropertyName = "Capabilities";
    internal const string CompletePropertyName = "Complete";
    internal const string IsDeterministicPropertyName = "IsDeterministic";
    internal const string ThrownExceptionsPropertyName = "ThrownExceptions";

    internal const EffectContractKind AllEffects =
        (EffectContractKind)((1L << 16) - 1);
    internal const EffectContractCapabilityKind AllCapabilities =
        (EffectContractCapabilityKind)((1 << 13) - 1);

    internal static bool TryConvertInt64(object? value, out long result) {
        try {
            result = Convert.ToInt64(
                value,
                System.Globalization.CultureInfo.InvariantCulture);
            return value != null;
        }
        catch (Exception exception) when (
            exception is InvalidCastException or
            FormatException or
            OverflowException) {
            result = 0;
            return false;
        }
    }
}
