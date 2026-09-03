namespace SharpProof.Attributes;

/// <summary>Identifies effects tracked by SharpProof summaries.</summary>
[Flags]
public enum SharpProofEffect : long
{
    /// <summary>Performs no tracked effect.</summary>
    None = 0,
    /// <summary>Reads state reachable from the receiver.</summary>
    ReadsReceiverState = 1L << 0,
    /// <summary>Reads state reachable from an argument.</summary>
    ReadsArgumentState = 1L << 1,
    /// <summary>Reads captured state.</summary>
    ReadsCapturedState = 1L << 2,
    /// <summary>Reads static managed state.</summary>
    ReadsStaticState = 1L << 3,
    /// <summary>Reads ambient state.</summary>
    ReadsAmbientState = 1L << 4,
    /// <summary>Writes state reachable from the receiver.</summary>
    WritesReceiverState = 1L << 5,
    /// <summary>Writes state reachable from an argument.</summary>
    WritesArgumentState = 1L << 6,
    /// <summary>Writes captured state.</summary>
    WritesCapturedState = 1L << 7,
    /// <summary>Writes static managed state.</summary>
    WritesStaticState = 1L << 8,
    /// <summary>Writes ambient state.</summary>
    WritesAmbientState = 1L << 9,
    /// <summary>Allocates managed storage.</summary>
    Allocates = 1L << 10,
    /// <summary>May let an exception escape.</summary>
    Throws = 1L << 11,
    /// <summary>Uses synchronization.</summary>
    Synchronizes = 1L << 12,
    /// <summary>Uses nondeterministic behavior.</summary>
    UsesNondeterminism = 1L << 13,
    /// <summary>Uses native code.</summary>
    UsesNativeCode = 1L << 14,
    /// <summary>Uses reflection.</summary>
    UsesReflection = 1L << 15
}
