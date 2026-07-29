namespace SharpProof.Attributes;
[Flags]
public enum SharpProofEffect : long
{
    None = 0,
    ReadsReceiverState = 1L << 0,
    ReadsArgumentState = 1L << 1,
    ReadsCapturedState = 1L << 2,
    ReadsStaticState = 1L << 3,
    ReadsAmbientState = 1L << 4,
    WritesReceiverState = 1L << 5,
    WritesArgumentState = 1L << 6,
    WritesCapturedState = 1L << 7,
    WritesStaticState = 1L << 8,
    WritesAmbientState = 1L << 9,
    Allocates = 1L << 10,
    Throws = 1L << 11,
    Synchronizes = 1L << 12,
    UsesNondeterminism = 1L << 13,
    UsesNativeCode = 1L << 14,
    UsesReflection = 1L << 15
}
