namespace SharpProof.Attributes;

[Flags]
public enum SharpProofCapability
{
    None = 0,
    IO = 1 << 0,
    FileRead = 1 << 1,
    FileWrite = 1 << 2,
    Network = 1 << 3,
    Console = 1 << 4,
    Process = 1 << 5,
    Environment = 1 << 6,
    Registry = 1 << 7,
    Clock = 1 << 8,
    Randomness = 1 << 9,
    Reflection = 1 << 10,
    Synchronization = 1 << 11,
    NativeInterop = 1 << 12
}