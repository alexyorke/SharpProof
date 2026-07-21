namespace SharpProof.Attributes;

[Flags]
public enum SharpProofEffect : long {
    None = 0,
    ReadsAmbientState = 1L << 0,
    WritesReceiverState = 1L << 1,
    WritesArgumentState = 1L << 2,
    WritesCapturedState = 1L << 3,
    WritesStaticState = 1L << 4,
    Allocates = 1L << 5,
    Throws = 1L << 6,
    Synchronizes = 1L << 7,
    UsesNondeterminism = 1L << 8,
    UsesNativeCode = 1L << 9,
    UsesReflection = 1L << 10,
    Unknown = 1L << 11,
    ReadsReceiverState = 1L << 12,
    ReadsArgumentState = 1L << 13,
    ReadsCapturedState = 1L << 14,
    ReadsStaticState = 1L << 15,
    WritesAmbientState = 1L << 16,
    WritesFreshOwnedState = 1L << 17,
    DirectCall = 1L << 18,
    DispatchUncertainty = 1L << 19,
    UnsupportedOperation = 1L << 20,
    BudgetExhaustion = 1L << 21
}
